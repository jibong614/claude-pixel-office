using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using ClaudeOffice.Cloud;
using ClaudeOffice.Hooks;
using ClaudeOffice.Monitoring;
using Microsoft.Web.WebView2.Core;

namespace ClaudeOffice;

/// <summary>
/// 사무실 화면(wwwroot/index.html)을 WebView2로 띄우고, 감시기가 만든 스냅샷을 화면에 넘긴다.
///   C# → 화면 : PostWebMessageAsJson(스냅샷 | {type:"cloud"} 휴대폰 보기 상태 | {type:"cloud-setup", error} | {type:"cloud-check"} 설정 도우미 점검
///               | {type:"app"} app.json 설정·새 버전 | {type:"share"} 저장해 둔 사무실 이름·별명·메모 | {type:"select", id} 이 직원을 열기)
///   화면 → C# : { cmd: "ready" | "topmost" | "hooks" | "window" | "share" | "cloud-setup" | "cloud-enable" | "cloud-rekey" | "cloud-check" | "cloud-page"
///               | "app-setting" | "notify-test" | "update-check" | "open-url" | "open-data-dir", ... }
/// </summary>
public partial class MainWindow : Window
{
    private const string VirtualHost = "office.local";

    private readonly TranscriptWatcher _watcher = new(TranscriptWatcher.DefaultRoot());
    private readonly HookServer _hooks = new(Path.Combine(App.DataDir, "hooks.log"));
    private readonly CloudSync _cloud = new(App.DataDir);
    private readonly Notifier _notifier = new();
    private readonly UpdateChecker _updates = new();
    private readonly TrayIcon? _tray;
    private bool _pageReady;
    private bool _exiting, _cleanedUp;
    private string? _pendingSelect;   // 알림을 눌러 창을 열었는데 화면이 아직 준비 중이면, 준비되는 대로 그 직원을 연다

    // 사무실 이름·별명·메모 — 화면(localStorage)이 원본이고 데이터 폴더 share.json 에 사본을 둔다.
    // 창을 띄우지 않고 시작해도 휴대폰·알림에 별명이 보이고, 화면 저장소가 지워져도 되살릴 수 있게.
    private static string SharePath => Path.Combine(App.DataDir, "share.json");
    private readonly object _shareLock = new();
    private Dictionary<string, string> _nicks = new(StringComparer.Ordinal);

    public MainWindow()
    {
        InitializeComponent();
        Title = WithVersion("Claude Office");   // 화면이 뜨기 전에도 버전이 보이게

        if (App.Offscreen)
        {
            // 점검 도구용: 화면 밖에, 포커스·작업표시줄 없이(작업 중인 사람 앞에 불쑥 뜨지 않게)
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = -20000;
            Top = -20000;
            ShowActivated = false;
            ShowInTaskbar = false;
        }
        else if (!App.DemoMode)
        {
            RestorePlacement();
        }

        // 알림 영역 🏢 아이콘 — 견본·점검용(화면 밖) 실행에서는 만들지 않는다(사용자 알림 영역을 어지럽히지 않게)
        if (!App.DemoMode && !App.Offscreen)
        {
            _tray = new TrayIcon(ShowFromTray, ExitApp);
            _tray.SetText(Title);
        }

        Loaded += OnLoaded;   // 숨긴 채 시작하면 처음 열 때 불린다(화면 WebView2 는 그때 준비)
        Closing += (_, e) =>
        {
            SavePlacement();
            // ✕ = 숨기기(설정). 정말 끝내는 건 알림 영역 메뉴 "끝내기"·Windows 종료·--exit
            if (_exiting || App.DemoMode || !App.Settings.CloseToTray) return;
            e.Cancel = true;
            Hide();
            _tray?.HintOnce();
        };
        Closed += (_, _) => Cleanup();

        if (!App.DemoMode) StartMonitoring();
    }

    /// <summary>자동 실행이거나 "시작 시 작업표시줄로"면 창을 띄우지 않고 알림 영역에만 둔다.</summary>
    public bool StartHidden => !App.DemoMode && (App.AutoStarted || App.Settings.StartMinimized);

    /// <summary>알림 영역 아이콘·다시 실행(중복 실행)으로 창을 연다.</summary>
    public void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        if (!App.Offscreen) Activate();   // 점검용 화면 밖 창은 포커스를 가져가지 않는다
    }

    /// <summary>숨기지 말고 정말 끝낼 준비(Windows 종료 등).</summary>
    public void PrepareExit() => _exiting = true;

    /// <summary>완전히 끝낸다 — 휴대폰에 "PC에서 꺼짐"을 남기고 감시를 멈춘다.</summary>
    public void ExitApp()
    {
        PrepareExit();
        Cleanup();   // 한 번도 띄우지 않은 창은 닫아도 Closed 가 안 올 수 있어 직접 정리(여기서 Shutdown 도 부른다)
        Close();
    }

    private void Cleanup()
    {
        if (_cleanedUp) return;
        _cleanedUp = true;
        if (IsVisible) SavePlacement();
        _watcher.Dispose();
        _hooks.Dispose();
        _updates.Dispose();
        _cloud.Dispose();                              // 올리기 루프를 먼저 멈춘다(생존 신호가 "꺼짐"을 덮어쓰지 않게)
        _cloud.SendOffline(TimeSpan.FromSeconds(3));   // 휴대폰에 1분 반을 기다리지 않고 바로 "꺼짐"
        _tray?.Dispose();
        Application.Current.Shutdown();
    }

    /// <summary>
    /// 대화 기록 감시·훅 수신·휴대폰 보기 업로드. 화면(WebView2)과 상관없이 돌아야 하므로
    /// 창이 그려지기를 기다리지 않고 바로 시작한다(작업표시줄로 내려 둔 채 켜도 휴대폰에는 올라가게).
    /// </summary>
    private void StartMonitoring()
    {
        // 저장해 둔 사무실 이름·별명·메모(창을 띄우기 전부터 휴대폰·알림에 쓴다)
        if (LoadShare() is { } share) ApplyShare(share);

        // 훅이 설정에 없어도 수신기는 켜 둔다(가볍고, 🔔로 연결하면 바로 받기 시작한다)
        _hooks.Start();
        _watcher.AttachHooks(_hooks);
        _watcher.SnapshotChanged += json =>
        {
            _cloud.OnSnapshot(json);   // 휴대폰 보기를 켰으면 암호화해 올린다(꺼져 있으면 아무것도 안 함)
            List<Notice> notices;
            try { notices = _notifier.OnSnapshot(json, NameOf); }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException) { notices = []; }
            Dispatcher.InvokeAsync(() =>
            {
                Post(json);
                foreach (var n in notices) ShowNotice(n);
            });
        };
        _cloud.StatusChanged += json => Dispatcher.InvokeAsync(() => Post(json));
        _cloud.Start();
        _watcher.Start();

        _updates.Checked += () => Dispatcher.InvokeAsync(OnUpdateChecked);
        _updates.Start();
    }

    // ───────────────────────── 알림 ─────────────────────────

    private string NameOf(string id, string name)
    {
        lock (_shareLock) return _nicks.TryGetValue(id, out var nick) ? nick : name;
    }

    /// <summary>PC 알림을 띄운다 — 창을 보고 있거나 방해 금지 시간이면 띄우지 않는다. 누르면 창을 열고 그 직원을 보여 준다.</summary>
    private void ShowNotice(Notice n)
    {
        if (_tray is null) return;
        if (IsVisible && IsActive && WindowState != WindowState.Minimized) return;
        if (App.Settings.InQuietHours(DateTime.Now)) return;
        _tray.Notify(n.Title, n.Text, silent: !App.Settings.NotifySound, onClick: () =>
        {
            ShowFromTray();
            if (n.SessionId != null) SelectSession(n.SessionId);
        });
    }

    private void SelectSession(string id)
    {
        if (_pageReady) Post(JsonSerializer.Serialize(new { type = "select", id }));
        else _pendingSelect = id;
    }

    private void OnUpdateChecked()
    {
        Post(AppSettingsJson());
        if (!_updates.IsNewer || _tray is null || _updates.Latest == App.Settings.UpdateNotified) return;
        App.Settings.UpdateNotified = _updates.Latest;   // 같은 버전은 한 번만 알린다
        TrySaveSettings();
        _tray.Notify($"새 버전 {_updates.Latest} 이 나왔어요", $"지금은 {App.Version} — 누르면 내려받는 페이지가 열립니다", silent: true, onClick: () => OpenUrl(_updates.Url));
    }

    // ───────────────────────── 사무실 이름·별명·메모 사본 ─────────────────────────

    private static string? LoadShare()
    {
        try { return File.Exists(SharePath) ? File.ReadAllText(SharePath) : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>{office, notes:{id:{nick, memo, t}}} — 휴대폰 업로드에 싣고, 알림에 쓸 별명을 뽑아 둔다.</summary>
    private void ApplyShare(string json)
    {
        var nicks = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return;
            if (doc.RootElement.TryGetProperty("notes", out var notes) && notes.ValueKind == JsonValueKind.Object)
                foreach (var p in notes.EnumerateObject())
                    if (p.Value.ValueKind == JsonValueKind.Object && p.Value.TryGetProperty("nick", out var nick) && nick.GetString() is { Length: > 0 } s)
                        nicks[p.Name] = s;
        }
        catch (JsonException) { return; }
        lock (_shareLock) _nicks = nicks;
        _cloud.OnShare(json);
    }

    private static void SaveShare(string json)
    {
        try
        {
            Directory.CreateDirectory(App.DataDir);
            var tmp = SharePath + ".tmp";
            File.WriteAllText(tmp, json, new System.Text.UTF8Encoding(false));
            File.Move(tmp, SharePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    // ───────────────────────── 창 위치·크기 ─────────────────────────

    private void RestorePlacement()
    {
        if (App.Settings.Window is not { } w || w.Width < MinWidth || w.Height < MinHeight) return;
        // 제목 표시줄 가운데가 화면 안에 있어야 쓴다(모니터를 뺐거나 해상도가 바뀐 경우)
        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                              SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        if (!screen.IntersectsWith(new Rect(w.Left + 60, w.Top, Math.Max(1, w.Width - 120), 24))) return;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = w.Left;
        Top = w.Top;
        Width = w.Width;
        Height = w.Height;
        if (w.Maximized) WindowState = WindowState.Maximized;
    }

    private void SavePlacement()
    {
        if (App.Offscreen || App.DemoMode || !IsLoaded) return;
        var b = WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
        if (b.IsEmpty || b.Width < MinWidth || b.Height < MinHeight) return;
        App.Settings.Window = new AppSettings.WindowPlacement
        {
            Left = b.Left, Top = b.Top, Width = b.Width, Height = b.Height,
            Maximized = WindowState == WindowState.Maximized,
        };
        TrySaveSettings();
    }

    private static void TrySaveSettings()
    {
        try { App.Settings.Save(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>바깥 브라우저로 연다 — 정해 둔 https 주소만(화면이 아무 주소나 열게 하지 않는다).</summary>
    private static void OpenUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps) return;
        if (u.Host is not ("console.firebase.google.com" or "github.com")) return;
        try { Process.Start(new ProcessStartInfo(u.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            Web.DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 0x1E, 0x1B, 0x2E);   // 로딩 중 흰 화면 깜빡임 방지
            var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(App.DataDir, "WebView2"));
            await Web.EnsureCoreWebView2Async(env);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            MessageBox.Show("Microsoft Edge WebView2 런타임이 필요합니다.\r\nhttps://developer.microsoft.com/microsoft-edge/webview2/ 에서 설치한 뒤 다시 실행하세요.",
                "Claude Office", MessageBoxButton.OK, MessageBoxImage.Warning);
            ExitApp();
            return;
        }

        var core = Web.CoreWebView2;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
#if !DEBUG
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
#endif
        core.SetVirtualHostNameToFolderMapping(VirtualHost, FindWebRoot(), CoreWebView2HostResourceAccessKind.Allow);
        core.WebMessageReceived += OnWebMessage;
        core.NavigationStarting += (_, _) => _pageReady = false;   // F5 새로고침하면 화면이 다시 ready를 보낸다
        core.DocumentTitleChanged += (_, _) =>                     // "(✋2) Claude Office v1.0.0" 처럼 작업표시줄·알림 영역에서도 보이게
        {
            Title = WithVersion(string.IsNullOrWhiteSpace(core.DocumentTitle) || core.DocumentTitle.Contains(VirtualHost)
                ? "Claude Office"
                : core.DocumentTitle);
            _tray?.SetText(Title);
        };

        var query = App.DemoMode ? "?demo" + (App.DemoSelect != null ? "&select=" + Uri.EscapeDataString(App.DemoSelect) : "") : "";
        core.Navigate($"https://{VirtualHost}/index.html{query}");
    }

    /// <summary>앱 쪽 설정(app.json·시작 프로그램 등록)과 새 버전 확인 결과를 화면 설정 패널에 알린다.</summary>
    private string AppSettingsJson()
    {
        var s = App.Settings;
        return JsonSerializer.Serialize(new
        {
            type = "app",
            version = App.Version,
            startMinimized = s.StartMinimized,
            closeToTray = s.CloseToTray,
            autoStart = AutoStart.Enabled,
            autoStartElsewhere = AutoStart.PointsElsewhere,
            notifyWaiting = s.NotifyWaiting,
            notifyDone = s.NotifyDone,
            notifyDoneMinutes = s.NotifyDoneMinutes,
            notifySound = s.NotifySound,
            quietEnabled = s.QuietEnabled,
            quietStart = s.QuietStart,
            quietEnd = s.QuietEnd,
            updateCheck = s.UpdateCheck,
            update = new { latest = _updates.Latest, newer = _updates.IsNewer, url = _updates.Url, error = _updates.Error, checkedAt = _updates.CheckedMs },
            tray = _tray != null,
        });
    }

    private static string WithVersion(string title) => $"{title} v{App.Version}";

    private void Post(string json)
    {
        if (_pageReady) Web.CoreWebView2?.PostWebMessageAsJson(json);
    }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;
            switch (root.GetProperty("cmd").GetString())
            {
                case "ready":
                    _pageReady = true;
                    if (_watcher.LastSnapshot is { } snap) Post(snap);
                    if (!App.DemoMode)
                    {
                        Post(_cloud.StatusJson());
                        Post(AppSettingsJson());
                        // 저장해 둔 별명·메모를 화면에 돌려준다(화면이 자기 것과 합친 뒤 다시 share 로 보낸다)
                        Post("{\"type\":\"share\",\"data\":" + (LoadShare() is { } saved && IsJsonObject(saved) ? saved : "null") + "}");
                    }
                    if (_pendingSelect is { } sel) { _pendingSelect = null; SelectSession(sel); }
                    break;
                case "app-setting":   // 앱 쪽 설정(app.json), autoStart 는 시작 프로그램 등록
                    if (!App.DemoMode && root.TryGetProperty("key", out var key) && root.TryGetProperty("value", out var val))
                    {
                        bool on = val.ValueKind == JsonValueKind.True;
                        int num = val.ValueKind == JsonValueKind.Number && val.TryGetInt32(out var n) ? n : 0;
                        var s = App.Settings;
                        try
                        {
                            switch (key.GetString())
                            {
                                case "startMinimized": s.StartMinimized = on; break;
                                case "closeToTray": s.CloseToTray = on; break;
                                case "notifyWaiting": s.NotifyWaiting = on; break;
                                case "notifyDone": s.NotifyDone = on; break;
                                case "notifyDoneMinutes": s.NotifyDoneMinutes = Math.Clamp(num, 1, 600); break;
                                case "notifySound": s.NotifySound = on; break;
                                case "quietEnabled": s.QuietEnabled = on; break;
                                case "quietStart": s.QuietStart = Math.Clamp(num, 0, 23); break;
                                case "quietEnd": s.QuietEnd = Math.Clamp(num, 0, 23); break;
                                case "updateCheck":
                                    s.UpdateCheck = on;
                                    if (on) _ = _updates.CheckAsync();
                                    break;
                                case "autoStart": AutoStart.Set(on); break;
                            }
                            s.Save();
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                        {
                            MessageBox.Show(this, $"설정을 저장하지 못했습니다.\r\n\r\n{ex.Message}", "Claude Office", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                        Post(AppSettingsJson());   // 실제로 바뀐 값으로 스위치를 맞춘다
                    }
                    break;
                case "share":   // 사무실 이름·별명·메모 — 휴대폰에서도 같은 이름으로 보이게 함께 올리고, 사본을 파일로
                    if (!App.DemoMode && root.TryGetProperty("data", out var share) && share.ValueKind == JsonValueKind.Object)
                    {
                        var json = share.GetRawText();
                        ApplyShare(json);
                        SaveShare(json);
                    }
                    break;
                case "cloud-setup":
                    if (!App.DemoMode && root.TryGetProperty("text", out var text))
                    {
                        var error = _cloud.Setup(text.GetString() ?? "");
                        Post(JsonSerializer.Serialize(new { type = "cloud-setup", error }));
                        if (error is null) RunCloudCheck();   // 다음 단계(데이터베이스·규칙·로그인)를 바로 확인
                    }
                    break;
                case "cloud-check":
                    if (!App.DemoMode) RunCloudCheck();
                    break;
                case "cloud-page":
                    if (!App.DemoMode && root.TryGetProperty("value", out var page)) _cloud.SetPage(page.GetString() ?? "public");
                    break;
                case "notify-test":
                    if (_tray is null) break;
                    _tray.Notify("✋ 알림 시험", "이렇게 알려 드려요 — 누르면 창이 열립니다", silent: !App.Settings.NotifySound, onClick: ShowFromTray);
                    break;
                case "update-check":
                    if (!App.DemoMode) _ = _updates.CheckAsync();
                    break;
                case "open-url":
                    if (root.TryGetProperty("url", out var url)) OpenUrl(url.GetString());
                    break;
                case "open-folder":   // 직원의 작업 폴더 — 화면이 보낸 경로가 아니라 지금 스냅샷에 있는 그 세션의 cwd 만 연다
                    if (!App.DemoMode && root.TryGetProperty("id", out var fid) && CwdOf(fid.GetString()) is { } cwd && Directory.Exists(cwd))
                        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{cwd}\"") { UseShellExecute = true });
                    break;
                case "open-data-dir":
                    if (!App.DemoMode)
                    {
                        Directory.CreateDirectory(App.DataDir);
                        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{App.DataDir}\"") { UseShellExecute = true });
                    }
                    break;
                case "cloud-enable":
                    if (!App.DemoMode) _cloud.SetEnabled(root.TryGetProperty("value", out var on) && on.ValueKind == JsonValueKind.True);
                    break;
                case "cloud-rekey":
                    if (!App.DemoMode && MessageBox.Show(this,
                            "휴대폰 보기용 암호 키를 새로 만들까요?\r\n\r\n지금 휴대폰에 열려 있는 화면은 더 이상 해독하지 못하고, QR을 다시 찍어야 합니다.\r\n" +
                            "QR이나 주소가 다른 사람에게 보였을 때 쓰세요.",
                            "Claude Office — 휴대폰 보기", MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK)
                        _cloud.Rekey();
                    break;
                case "topmost":
                    Topmost = root.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.True;
                    break;
                case "hooks":
                    if (!App.DemoMode) ToggleHooks();
                    break;
                case "window":   // 설정 "표시 기간"(시간). 화면이 저장해 두고 시작할 때마다 다시 보낸다
                    if (root.TryGetProperty("hours", out var h) && h.TryGetDouble(out var hours))
                        _watcher.SetWindow(TimeSpan.FromHours(Math.Clamp(hours, 1, 24 * 7)));
                    break;
            }
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // 모르는 메시지는 무시
        }
    }

    private void RunCloudCheck() => _ = Task.Run(async () =>
    {
        var json = await _cloud.CheckAsync();
        if (json != null) await Dispatcher.InvokeAsync(() => Post(json));
    });

    private string? CwdOf(string? id)
    {
        if (id is null || _watcher.LastSnapshot is not { } snap) return null;
        using var doc = JsonDocument.Parse(snap);
        foreach (var s in doc.RootElement.GetProperty("sessions").EnumerateArray())
            if (s.GetProperty("id").GetString() == id)
                return s.TryGetProperty("cwd", out var c) ? c.GetString() : null;
        return null;
    }

    private static bool IsJsonObject(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException) { return false; }
    }

    /// <summary>
    /// 🔔 버튼: Claude Code 전역 설정에 훅을 넣거나 뺀다. 전역 설정을 바꾸는 일이라 반드시 확인을 받는다.
    /// </summary>
    private void ToggleHooks()
    {
        var path = HookSetup.SettingsPath;
        bool installed = HookSetup.IsInstalled();
        var question = installed
            ? $"Claude Code 설정에서 Claude Office 훅을 뺄까요?\r\n\r\n{path}\r\n\r\n" +
              "Claude Office가 넣은 훅만 지우고 다른 설정은 그대로 둡니다.\r\n" +
              "빼고 나면 승인 대기는 다시 '추정'으로만 표시됩니다."
            : $"Claude Code 설정에 훅 3개를 추가할까요?\r\n\r\n{path}\r\n\r\n" +
              "  · PermissionRequest — 승인 창이 뜨면 손을 듭니다\r\n" +
              "  · Notification — 승인·입력 요청 알림\r\n" +
              "  · SessionEnd — 세션을 닫으면 바로 퇴근으로 표시\r\n\r\n" +
              $"훅은 이 프로그램(127.0.0.1:{HookServer.Port})으로 알림만 보냅니다({HookSetup.ShellDescription}).\r\n" +
              "승인을 대신하거나 작업을 막지 않고, 프로그램이 꺼져 있으면 조용히 넘어갑니다.\r\n" +
              "고치기 전 원본은 settings.json.claudeoffice.bak 으로 남깁니다.";
        if (MessageBox.Show(this, question, "Claude Office — 훅 연결", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            return;

        try
        {
            if (installed) HookSetup.Uninstall();
            else HookSetup.Install();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            MessageBox.Show(this, $"settings.json 을 고치지 못했습니다. 파일은 그대로입니다.\r\n\r\n{ex.Message}",
                "Claude Office — 훅 연결", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        _watcher.RefreshHookStatus();
    }

    /// <summary>
    /// 화면 파일 위치. 개발 중(Debug)에는 소스 폴더의 wwwroot를 직접 써서, 고친 뒤 F5만 누르면 바로 보이게 한다.
    /// </summary>
    private static string FindWebRoot()
    {
        var baseDir = AppContext.BaseDirectory;
#if DEBUG
        var dir = new DirectoryInfo(baseDir);
        for (int i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ClaudeOffice.csproj"))
                && File.Exists(Path.Combine(dir.FullName, "wwwroot", "index.html")))
                return Path.Combine(dir.FullName, "wwwroot");
        }
#endif
        return Path.Combine(baseDir, "wwwroot");
    }
}
