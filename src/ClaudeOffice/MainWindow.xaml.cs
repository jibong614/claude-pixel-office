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
///   C# → 화면 : PostWebMessageAsJson(스냅샷 | {type:"cloud"} 휴대폰 보기 상태 | {type:"cloud-setup", error} | {type:"app"} app.json 설정)
///   화면 → C# : { cmd: "ready" | "topmost" | "hooks" | "window" | "share" | "cloud-setup" | "cloud-enable" | "cloud-rekey" | "app-setting", ... }
/// </summary>
public partial class MainWindow : Window
{
    private const string VirtualHost = "office.local";

    private readonly TranscriptWatcher _watcher = new(TranscriptWatcher.DefaultRoot());
    private readonly HookServer _hooks = new(Path.Combine(App.DataDir, "hooks.log"));
    private readonly CloudSync _cloud = new(App.DataDir);
    private readonly TrayIcon? _tray;
    private bool _pageReady;
    private bool _exiting, _cleanedUp;

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

        // 알림 영역 🏢 아이콘 — 견본·점검용(화면 밖) 실행에서는 만들지 않는다(사용자 알림 영역을 어지럽히지 않게)
        if (!App.DemoMode && !App.Offscreen)
        {
            _tray = new TrayIcon(ShowFromTray, ExitApp);
            _tray.SetText(Title);
        }

        Loaded += OnLoaded;   // 숨긴 채 시작하면 처음 열 때 불린다(화면 WebView2 는 그때 준비)
        Closing += (_, e) =>
        {
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
        _watcher.Dispose();
        _hooks.Dispose();
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
        // 훅이 설정에 없어도 수신기는 켜 둔다(가볍고, 🔔로 연결하면 바로 받기 시작한다)
        _hooks.Start();
        _watcher.AttachHooks(_hooks);
        _watcher.SnapshotChanged += json =>
        {
            _cloud.OnSnapshot(json);   // 휴대폰 보기를 켰으면 암호화해 올린다(꺼져 있으면 아무것도 안 함)
            Dispatcher.InvokeAsync(() => Post(json));
        };
        _cloud.StatusChanged += json => Dispatcher.InvokeAsync(() => Post(json));
        _cloud.Start();
        _watcher.Start();
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

    /// <summary>앱 쪽 설정(app.json·시작 프로그램 등록)을 화면 설정 패널에 알린다.</summary>
    private static string AppSettingsJson() =>
        JsonSerializer.Serialize(new
        {
            type = "app",
            startMinimized = App.Settings.StartMinimized,
            closeToTray = App.Settings.CloseToTray,
            autoStart = AutoStart.Enabled,
            autoStartElsewhere = AutoStart.PointsElsewhere,
        });

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
                    if (!App.DemoMode) Post(_cloud.StatusJson());
                    if (!App.DemoMode) Post(AppSettingsJson());
                    break;
                case "app-setting":   // 앱 쪽 설정: startMinimized·closeToTray(app.json), autoStart(시작 프로그램 등록)
                    if (!App.DemoMode && root.TryGetProperty("key", out var key))
                    {
                        bool on = root.TryGetProperty("value", out var val) && val.ValueKind == JsonValueKind.True;
                        try
                        {
                            switch (key.GetString())
                            {
                                case "startMinimized": App.Settings.StartMinimized = on; App.Settings.Save(); break;
                                case "closeToTray": App.Settings.CloseToTray = on; App.Settings.Save(); break;
                                case "autoStart": AutoStart.Set(on); break;
                            }
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                        {
                            MessageBox.Show(this, $"설정을 저장하지 못했습니다.\r\n\r\n{ex.Message}", "Claude Office", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                        Post(AppSettingsJson());   // 실제로 바뀐 값으로 스위치를 맞춘다
                    }
                    break;
                case "share":   // 사무실 이름·별명·메모 — 휴대폰에서도 같은 이름으로 보이게 함께 올린다
                    if (root.TryGetProperty("data", out var share) && share.ValueKind == JsonValueKind.Object)
                        _cloud.OnShare(share.GetRawText());
                    break;
                case "cloud-setup":
                    if (!App.DemoMode && root.TryGetProperty("text", out var text))
                        Post(JsonSerializer.Serialize(new { type = "cloud-setup", error = _cloud.Setup(text.GetString() ?? "") }));
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
              $"훅은 이 프로그램(127.0.0.1:{HookServer.Port})으로 알림만 보냅니다(Git Bash의 curl 사용).\r\n" +
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
