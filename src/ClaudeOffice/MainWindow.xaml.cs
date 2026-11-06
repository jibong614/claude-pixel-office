using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using ClaudeOffice.Cloud;
using ClaudeOffice.Hooks;
using ClaudeOffice.Monitoring;
using ClaudeOffice.Remote;
using Microsoft.Web.WebView2.Core;

namespace ClaudeOffice;

/// <summary>
/// 사무실 화면(wwwroot/index.html)을 WebView2로 띄우고, 감시기가 만든 스냅샷을 화면에 넘긴다.
///   C# → 화면 : PostWebMessageAsJson(스냅샷 | {type:"cloud"} 휴대폰 연결 상태 | {type:"remote"} 휴대폰 지시·승인 상태 | {type:"app"} app.json 설정·새 버전
///               | {type:"share"} 저장해 둔 사무실 이름·별명·메모 | {type:"select", id} 이 직원을 열기 | {type:"reset"} 설정 초기화)
///   화면 → C# : { cmd: "ready" | "topmost" | "hooks" | "window" | "share" | "cloud-enable" | "cloud-rekey" | "cloud-phone-edit" | "app-setting"
///               | "notify-test" | "update-check" | "update-install" | "open-url" | "open-folder" | "open-data-dir" | "widget" | "pet" | … , ... }
///               "cloud-setup" 은 점검 도구 전용(가짜 무료 서버에 잇기).
/// </summary>
public partial class MainWindow : Window
{
    private const string VirtualHost = "office.local";

    private readonly TranscriptWatcher _watcher = new(TranscriptWatcher.DefaultRoot());
    private readonly HookServer _hooks = new(Path.Combine(App.DataDir, "hooks.log"));
    private readonly CloudSync _cloud = new(App.DataDir);
    private readonly Notifier _notifier = new();
    private readonly UpdateChecker _updates = new();
    private readonly Updater _updater = new();
    private readonly RemoteHub _remote = new(Path.Combine(App.DataDir, "remote.log"));
    private bool _awayByLock;         // 화면 잠금으로 켠 자리 비움(잠금을 풀면 끈다)
    private bool _locked;             // Windows 화면이 잠겨 있음
    private bool _leftWhileAway;      // 자리 비움 중에 PC 가 한동안 비어 있었음 → 그 뒤 입력이 오면 "돌아옴"
    private readonly System.Windows.Threading.DispatcherTimer _presence = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly TrayIcon? _tray;
    private WidgetWindow? _widgetWindow;
    private SettingsWindow? _settingsWindow;
    private bool _pageReady;
    private bool _exiting, _cleanedUp;
    private string? _pendingSelect;   // 알림을 눌러 창을 열었는데 화면이 아직 준비 중이면, 준비되는 대로 그 직원을 연다
    private string? _pendingSettings;

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

        // 알림 영역 🧑‍💻 아이콘 — 견본·점검용(화면 밖) 실행에서는 만들지 않는다(사용자 알림 영역을 어지럽히지 않게)
        if (!App.DemoMode && !App.Offscreen)
        {
            _tray = new TrayIcon(
                ShowFromTray,
                sid => { ShowFromTray(); SelectSession(sid); },
                ShowLargeView,
                ShowCompactView,
                TurnOffPet,
                TurnOnPetNormal,
                TurnOnPetIsland,
                () => OpenSettingsWindow(),
                ExitApp);
            _tray.SetText(Title);
            _tray.SetViewMode(App.Settings.Widget);
            _tray.SetPetMode(App.Settings.Pet ? (App.Settings.PetIsland ? TrayIcon.PetTrayMode.Island : TrayIcon.PetTrayMode.Normal) : TrayIcon.PetTrayMode.None);
            if (App.Updated)
                _tray.Notify(L.T($"{App.Version} 버전으로 업데이트했어요", $"Updated to {App.Version}"),
                             L.T("바뀐 내용은 ⚙️ → 정보 → 버전에서 GitHub 릴리스로", "See what's new: ⚙️ → About → Version (GitHub release)"),
                             silent: true, onClick: ShowFromTray);
        }

        IsVisibleChanged += (_, _) =>
        {
            if (!App.Offscreen && _pageReady)
                Web.CoreWebView2?.PostWebMessageAsJson(IsVisible ? "{\"type\":\"visible\",\"value\":true}" : "{\"type\":\"visible\",\"value\":false}");
        };
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
        _widgetWindow?.CloseForce();
        _widgetWindow = null;
        _settingsWindow?.CloseForce();
        _settingsWindow = null;
        Microsoft.Win32.SystemEvents.SessionSwitch -= OnSessionSwitch;
        _presence.Stop();
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

        // 예전 버전이 넣은 휴대폰 지시 훅은 새 명령으로(1.1.9: 앱이 꺼져 있던 동안에도 포기하지 않고 다시 붙음)
        if (!App.DemoMode)
        {
            try { HookSetup.UpgradeRemote(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException) { }
        }

        // 훅이 설정에 없어도 수신기는 켜 둔다(가볍고, 🔔로 연결하면 바로 받기 시작한다)
        _remote.SetPet(App.Settings.Pet, App.Settings.PetSessionId);
        _hooks.Remote = _remote;
        _hooks.Received += ev => { if (ev.Event == "SessionEnd") _remote.OnSessionEnd(ev.SessionId); };
        _hooks.Start();
        _watcher.AttachHooks(_hooks);
        _watcher.SnapshotChanged += json =>
        {
            _cloud.OnSnapshot(json);   // 휴대폰 보기를 켰으면 암호화해 올린다(꺼져 있으면 아무것도 안 함)
            List<Notice> notices;
            try { notices = _notifier.OnSnapshot(json, NameOf); }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException) { notices = []; }
            int waitCount = 0;
            string? firstWaitId = null, firstWaitName = null;
            try { (waitCount, firstWaitId, firstWaitName) = ParseWaitingInfo(json); }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException) { }
            Dispatcher.InvokeAsync(() =>
            {
                Post(json);
                _tray?.SetWaiting(waitCount, firstWaitId, firstWaitName);
                foreach (var n in notices)
                {
                    ShowNotice(n);
                    var kind = n.Title.Contains('✅') ? "done" : "ask";
                    Post(JsonSerializer.Serialize(new { type = "sound", kind }));
                }
            });
        };
        _cloud.StatusChanged += json => Dispatcher.InvokeAsync(() => Post(json));
        _cloud.NotesFromPhone += edits => Dispatcher.InvokeAsync(() => ApplyPhoneEdits(edits));
        _cloud.RemoteFromPhone += _remote.FromPhone;
        _remote.StopRequested += sid => Dispatcher.InvokeAsync(() => StopSession(sid));
        _remote.SetPet(App.Settings.Pet, App.Settings.PetSessionId);
        _remote.Changed += OnRemoteChanged;
        OnRemoteChanged();
        Microsoft.Win32.SystemEvents.SessionSwitch += OnSessionSwitch;
        if (!App.DemoMode)
        {
            _presence.Tick += (_, _) => CheckPresence();
            _presence.Start();
        }
        _cloud.SetLanguage(L.Code);   // 휴대폰도 PC와 같은 언어로
        _cloud.Start();
        _watcher.Start();

        _updates.Checked += () => Dispatcher.InvokeAsync(OnUpdateChecked);
        _updater.Changed += () => Dispatcher.InvokeAsync(() => Post(AppSettingsJson()));   // 받는 중 진행률
        _updates.Start();

        if (App.Settings.Pet) ApplyWidgetWindow();
    }

    // ───────────────────────── 알림 ─────────────────────────

    private string NameOf(string id, string name)
    {
        lock (_shareLock) return _nicks.TryGetValue(id, out var nick) ? nick : name;
    }

    private (int Count, string? FirstId, string? FirstName) ParseWaitingInfo(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("sessions", out var sessions) || sessions.ValueKind != JsonValueKind.Array)
            return (0, null, null);
        int count = 0;
        string? firstId = null, firstName = null;
        foreach (var e in sessions.EnumerateArray())
        {
            var act = e.TryGetProperty("act", out var a) ? a.GetString() : null;
            if (act is not ("waiting" or "asking")) continue;
            count++;
            if (firstId is null)
            {
                firstId = e.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                var rawName = e.TryGetProperty("name", out var nmEl) ? nmEl.GetString() ?? "" : "";
                firstName = firstId != null ? NameOf(firstId, rawName) : rawName;
            }
        }
        return (count, firstId, firstName);
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
        if (_pageReady) Web.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "select", id }));
        else _pendingSelect = id;
    }

    private void OnUpdateChecked()
    {
        Post(AppSettingsJson());
        if (!_updates.IsNewer || _tray is null || _updates.Latest == App.Settings.UpdateNotified) return;
        App.Settings.UpdateNotified = _updates.Latest;   // 같은 버전은 한 번만 알린다
        TrySaveSettings();
        bool canInstall = CanInstallUpdate();
        _tray.Notify(L.T($"새 버전 {_updates.Latest} 이 나왔어요", $"Version {_updates.Latest} is out"),
                     canInstall
                         ? L.T($"지금은 {App.Version} — 누르면 업데이트할지 묻습니다", $"You have {App.Version} — click to update")
                         : L.T($"지금은 {App.Version} — 누르면 내려받는 페이지가 열립니다", $"You have {App.Version} — click to open the download page"),
                     silent: true, onClick: () =>
                     {
                         if (!canInstall) { OpenUrl(_updates.Url); return; }
                         ShowFromTray();
                         StartUpdate();
                     });
    }

    /// <summary>앱 안에서 바로 바꿀 수 있는지: 새 버전이 있고, 릴리스에 zip·SHA-256 이 있고, 이 실행 파일이 배포본이며 폴더에 쓸 수 있을 때.</summary>
    private bool CanInstallUpdate() =>
        _updates.IsNewer && _updates.ZipUrl != null && _updates.ShaUrl != null && Updater.CannotReason() is null;

    /// <summary>
    /// "업데이트할까요?" → 켜진 채로 받기·대조·풀기(진행률은 설정 → 정보) → 파일 바꾸기 → 새 버전 실행 → 이 프로그램 끝내기.
    /// 바로 바꿀 수 없으면(개발용 빌드·쓰기 권한 없음·옛 릴리스) 릴리스 페이지를 연다.
    /// </summary>
    private async void StartUpdate()
    {
        if (!CanInstallUpdate()) { OpenUrl(_updates.Url); return; }
        if (_updater.State is "downloading" or "installing") return;
        if (!App.Offscreen && MessageBox.Show(this,
                L.T($"새 버전 {_updates.Latest} 으로 업데이트할까요?\r\n\r\n받는 동안(약 65MB) 계속 쓸 수 있고, 다 받아 확인하면 잠깐 꺼졌다가 새 버전으로 다시 켜집니다.\r\n설정·별명·메모·휴대폰 연결는 그대로입니다.",
                    $"Update to version {_updates.Latest}?\r\n\r\nYou can keep working while it downloads (about 65 MB). When it's verified, the app closes for a moment and reopens as the new version.\r\nSettings, nicknames, memos and the phone connection stay as they are."),
                "Claude Office", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            return;
        var extra = App.Offscreen ? new[] { "--offscreen" } : Array.Empty<string>();   // 점검 도구로 시험할 때는 새 버전도 화면 밖에
        if (await _updater.RunAsync(_updates.Latest!, _updates.ZipUrl!, _updates.ShaUrl!, extra))
            ExitApp();
        else if (!App.Offscreen)
            MessageBox.Show(this, L.T($"업데이트하지 못했습니다. 지금 버전은 그대로입니다.\r\n\r\n{_updater.Error}", $"The update failed. Your current version is unchanged.\r\n\r\n{_updater.Error}"),
                "Claude Office", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    // ───────────────────────── 자리 비움 원격 · 바탕화면 미니펫 ─────────────────────────

    /// <summary>원격 상태를 휴대폰 업로드·화면에 알린다(아무 스레드에서나).</summary>
    private void OnRemoteChanged()
    {
        var json = _remote.StateJson();
        _cloud.OnRemote(json, RemoteHub.Installed, _remote.Away);
        Dispatcher.InvokeAsync(() => Post("{\"type\":\"remote\",\"data\":" + json + "}"));
    }

    private void SetPet(bool on, string? sid)
    {
        if (sid != null) App.Settings.PetSessionId = string.IsNullOrWhiteSpace(sid) ? null : sid.Trim();
        App.Settings.Pet = on;
        TrySaveSettings();
        _tray?.SetPetMode(on ? (App.Settings.PetIsland ? TrayIcon.PetTrayMode.Island : TrayIcon.PetTrayMode.Normal) : TrayIcon.PetTrayMode.None);
        _remote.SetPet(on, App.Settings.PetSessionId);
        ApplyWidgetWindow();
        Post(AppSettingsJson());
    }

    private void TurnOffPet()
    {
        SetPet(false, null);
    }

    private void TurnOnPetNormal()
    {
        App.Settings.PetIsland = false;
        TrySaveSettings();
        SetPet(true, null);
        _widgetWindow?.SetPetMode(false);
        _widgetWindow?.Post(JsonSerializer.Serialize(new { type = "pet-mode", mode = "normal" }));
        _tray?.SetPetMode(TrayIcon.PetTrayMode.Normal);
    }

    private void TurnOnPetIsland()
    {
        App.Settings.PetIsland = true;
        TrySaveSettings();
        SetPet(true, null);
        _widgetWindow?.SetPetMode(true);
        _widgetWindow?.Post(JsonSerializer.Serialize(new { type = "pet-mode", mode = "island" }));
        _tray?.SetPetMode(TrayIcon.PetTrayMode.Island);
    }

    private void OpenSettings(string? tab = null)
    {
        ShowFromTray();
        if (_pageReady)
        {
            Web.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "settings", tab }));
        }
        else
        {
            _pendingSettings = tab ?? "";
        }
    }


    private void OpenSettingsWindow(string? tab = null)

    {

        if (_settingsWindow is null)

        {

            _settingsWindow = new SettingsWindow(VirtualHost, FindWebRoot(), e => OnWebMessage(null, e));

        }

        _settingsWindow.ShowSettings(tab);

        if (_settingsWindow.IsPageReady)

        {

            _settingsWindow.Post(AppSettingsJson());

            _settingsWindow.Post(_cloud.StatusJson());

        }

    }


    private void ApplyWidgetWindow()
    {
        if (App.DemoMode) return;
        if (App.Settings.Pet)
        {
            if (_widgetWindow is null)
                _widgetWindow = new WidgetWindow(VirtualHost, FindWebRoot(), e => OnWebMessage(null, e), () => SetPet(false, null));
            _widgetWindow.ApplyBackdrop(App.Settings.PetNoBg);
            if (!_widgetWindow.IsVisible)
            {
                _widgetWindow.RestorePlacement();
                _widgetWindow.Show();
            }
        }
        else if (_widgetWindow is { IsVisible: true })
        {
            _widgetWindow.SavePlacement();
            _widgetWindow.Hide();
        }
    }

    // 자리 비움은 손으로 켜고 끄지 않는다(1.1.6) — 켜기: 입력 없음·화면 잠금(여기), 휴대폰(PC 가 비어 있을 때만, RemoteHub) / 끄기: 돌아와 입력·잠금 풀기
    private static readonly TimeSpan LeftAfter = TimeSpan.FromSeconds(30);   // 이만큼 입력이 없으면 "자리에 없음" — 휴대폰이 자리 비움을 켤 수 있다

    /// <summary>
    /// 2초마다: 마우스·키보드 입력이 설정한 분만큼 없으면 자리 비움을 켜고, 자리 비움 중에 한동안 비어 있던 PC 에 다시 입력이 오면 끈다(돌아옴).
    /// "돌아옴"은 자리 비움 중 PC 가 한동안 비어 있었던 뒤의 입력만. 잠겨 있는 동안의 입력은 보지 않는다(풀면 OnSessionSwitch).
    /// </summary>
    private void CheckPresence()
    {
        int auto = Math.Clamp(App.Settings.RemoteAwayIdleMinutes, 0, 60);
        if (!RemoteHub.Installed) { _remote.SetPresence(false, auto); return; }
        var idle = IdleTime();
        bool left = _locked || idle >= LeftAfter;
        _remote.SetPresence(left, auto);
        if (!_remote.Away)
        {
            _leftWhileAway = false;
            if (auto > 0 && idle >= TimeSpan.FromMinutes(auto)) _remote.SetAway(true, "idle");
            return;
        }
        if (_locked) return;
        if (left) _leftWhileAway = true;
        else if (_leftWhileAway && idle < TimeSpan.FromSeconds(5))
        {
            _leftWhileAway = _awayByLock = false;
            _remote.SetAway(false, "back");
        }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct LastInputInfo { public uint Size; public uint Time; }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);

    /// <summary>이 Windows 세션에서 마지막 마우스·키보드 입력 뒤 지난 시간.</summary>
    private static TimeSpan IdleTime()
    {
        var info = new LastInputInfo { Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<LastInputInfo>() };
        return GetLastInputInfo(ref info) ? TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.Time)) : TimeSpan.Zero;
    }

    /// <summary>화면을 잠그면 자리 비움, 풀면 돌아옴(잠금으로 켠 것만 — 직접 켜 둔 것은 그대로).</summary>
    private void OnSessionSwitch(object? sender, Microsoft.Win32.SessionSwitchEventArgs e)
    {
        if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionLock) _locked = true;
        else if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionUnlock) _locked = false;
        if (!App.Settings.RemoteAwayOnLock || !RemoteHub.Installed) return;
        if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionLock && !_remote.Away)
        {
            _remote.SetAway(true, "lock");
            _awayByLock = true;
        }
        else if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionUnlock && _awayByLock)
        {
            _awayByLock = false;
            _remote.SetAway(false, "unlock");
        }
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

    /// <summary>
    /// 휴대폰에서 고친 별명·메모를 사본(share.json)에 고친 시각(t) 기준으로 합친다. 둘 다 비었으면 지운 것 — 빈 기록 {t} 로 남겨
    /// 화면이 합칠 때 지우게 한다. 화면이 떠 있으면 합친 것을 보내 화면 저장소에도 반영(화면이 다시 share 로 돌려줘 사본이 맞춰진다).
    /// </summary>
    private void ApplyPhoneEdits(List<NoteEdit> edits)
    {
        System.Text.Json.Nodes.JsonObject root;
        try { root = (LoadShare() is { } j ? System.Text.Json.Nodes.JsonNode.Parse(j) as System.Text.Json.Nodes.JsonObject : null) ?? new(); }
        catch (JsonException) { root = new(); }
        if (root["notes"] is not System.Text.Json.Nodes.JsonObject notes) root["notes"] = notes = new System.Text.Json.Nodes.JsonObject();

        int applied = 0;
        foreach (var e in edits)
        {
            long cur = 0;
            var prev = notes[e.Id] as System.Text.Json.Nodes.JsonObject;
            try { cur = prev?["t"]?.GetValue<long>() ?? 0; } catch (Exception ex) when (ex is InvalidOperationException or FormatException) { }
            if (e.T <= cur) continue;
            var n = new System.Text.Json.Nodes.JsonObject();
            if (!string.IsNullOrWhiteSpace(e.Nick)) n["nick"] = e.Nick;
            if (!string.IsNullOrWhiteSpace(e.Memo)) n["memo"] = e.Memo;
            if (prev?["look"] is { } lk) n["look"] = lk.DeepClone();
            if (prev?["deco"] is { } dc) n["deco"] = dc.DeepClone();
            n["t"] = e.T;
            notes[e.Id] = n;
            applied++;
        }
        if (applied == 0) return;
        var json = root.ToJsonString();
        SaveShare(json);
        ApplyShare(json);
        Post("{\"type\":\"share\",\"data\":" + json + "}");
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

    private const double NormalMinWidth = 480, NormalMinHeight = 360, WidgetMinWidth = 260, WidgetMinHeight = 110;

    private void RestorePlacement()
    {
        WindowStartupLocation = WindowStartupLocation.Manual;
        if (App.Settings.Widget) ApplyWidgetPlacement();
        else if (!ApplyNormalPlacement()) WindowStartupLocation = WindowStartupLocation.CenterScreen;
    }

    /// <summary>제목 표시줄 가운데가 화면 안에 있어야 그 위치를 쓴다(모니터를 뺐거나 해상도가 바뀐 경우).</summary>
    private static bool OnScreen(AppSettings.WindowPlacement w)
    {
        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                              SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        return screen.IntersectsWith(new Rect(w.Left + 60, w.Top, Math.Max(1, w.Width - 120), 24));
    }

    /// <summary>보통 창: 기억해 둔 위치·크기(없거나 화면 밖이면 false — 기본 크기).</summary>
    private bool ApplyNormalPlacement()
    {
        MinWidth = NormalMinWidth;
        MinHeight = NormalMinHeight;
        if (App.Settings.Window is not { } w || w.Width < NormalMinWidth || w.Height < NormalMinHeight || !OnScreen(w))
        {
            Width = 1180;
            Height = 760;
            return false;
        }
        if (!App.Offscreen) { Left = w.Left; Top = w.Top; }
        Width = w.Width;
        Height = w.Height;
        if (w.Maximized) WindowState = WindowState.Maximized;
        return true;
    }

    /// <summary>작게 보기: 기억해 둔 위치·폭, 처음이면 화면 오른쪽 위. 늘 맨 위에. 높이는 화면이 목록 높이를 알려 주면 맞춘다.</summary>
    private void ApplyWidgetPlacement()
    {
        WindowState = WindowState.Normal;
        MinWidth = WidgetMinWidth;
        MinHeight = WidgetMinHeight;
        Topmost = true;
        var w = App.Settings.CompactPlacement;
        Width = w is { Width: >= WidgetMinWidth and <= 1000 } ? w.Width : 340;
        Height = w is { Height: >= WidgetMinHeight } ? w.Height : 260;
        if (App.Offscreen) return;   // 점검용 창은 화면 밖 그대로
        var wa = SystemParameters.WorkArea;
        if (w != null && OnScreen(w)) { Left = w.Left; Top = w.Top; }
        else { Left = wa.Right - Width - 16; Top = wa.Top + 16; }
    }

    private void ShowLargeView()
    {
        SetWidget(false);
        ShowFromTray();
    }

    private void ShowCompactView()
    {
        SetWidget(true);
        ShowFromTray();
    }

    private void SetWidget(bool on)
    {
        if (on != App.Settings.Widget)
        {
            SavePlacement();   // 지금 모양의 위치·크기를 먼저 기억
            App.Settings.Widget = on;
            TrySaveSettings();
            _tray?.SetViewMode(on);
            if (on)
            {
                ApplyWidgetPlacement();
                ShowFromTray();
            }
            else
            {
                Topmost = false;   // 화면이 "항상 위" 설정을 다시 보낸다
                if (!ApplyNormalPlacement() && !App.Offscreen)
                {
                    var wa = SystemParameters.WorkArea;
                    Left = wa.Left + (wa.Width - Width) / 2;
                    Top = wa.Top + (wa.Height - Height) / 2;
                }
                ShowFromTray();
            }
        }
        else
        {
            ShowFromTray();
        }
        Post(AppSettingsJson());
    }

    /// <summary>작게 보기 목록 높이(CSS 픽셀 = WPF 단위)에 창 높이를 맞춘다.</summary>
    private void FitWidgetHeight(double contentHeight)
    {
        if (!App.Settings.Widget || WindowState != WindowState.Normal) return;
        double chrome = Math.Max(0, ActualHeight - Web.ActualHeight);   // 제목 표시줄·테두리
        Height = Math.Clamp(contentHeight + chrome, WidgetMinHeight, SystemParameters.WorkArea.Height - 40);
    }

    private void SavePlacement()
    {
        if (App.Offscreen || App.DemoMode || !IsLoaded) return;
        var b = WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
        if (b.IsEmpty || b.Width < MinWidth || b.Height < MinHeight) return;
        var p = new AppSettings.WindowPlacement
        {
            Left = b.Left, Top = b.Top, Width = b.Width, Height = b.Height,
            Maximized = WindowState == WindowState.Maximized,
        };
        if (App.Settings.Widget) App.Settings.CompactPlacement = p;
        else App.Settings.Window = p;
        TrySaveSettings();
    }

    private static void TrySaveSettings()
    {
        try { App.Settings.Save(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>바깥 브라우저로 연다 — GitHub(릴리스 페이지)의 https 주소만(화면이 아무 주소나 열게 하지 않는다).</summary>
    private static void OpenUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps) return;
        if (u.Host != "github.com") return;
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
            MessageBox.Show(L.T("Microsoft Edge WebView2 런타임이 필요합니다.\r\nhttps://developer.microsoft.com/microsoft-edge/webview2/ 에서 설치한 뒤 다시 실행하세요.",
                                "The Microsoft Edge WebView2 Runtime is required.\r\nInstall it from https://developer.microsoft.com/microsoft-edge/webview2/ and run again."),
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

        core.Navigate(PageUrl());

        // 처음 한 번: Claude Code 와 연결할지 묻는다(설정에서 찾지 않아도 되게). 화면이 먼저 뜨도록 조금 뒤에
        if (!App.DemoMode && !App.Offscreen && !App.Settings.ConnectAsked)
            _ = Dispatcher.BeginInvoke(AskConnectOnce, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    private void AskConnectOnce()
    {
        App.Settings.ConnectAsked = true;
        TrySaveSettings();
        if (HookSetup.IsInstalled() && HookSetup.IsRemoteInstalled()) return;
        ToggleHooks(firstRun: true);
    }

    /// <summary>화면 주소: 견본이면 ?demo(&amp;select=), 영어 화면이면 lang=en(한국어는 붙이지 않는다 — 예전 주소 그대로).</summary>
    private static string PageUrl()
    {
        var q = new List<string>();
        if (App.DemoMode)
        {
            q.Add("demo");
            if (App.DemoSelect != null) q.Add("select=" + Uri.EscapeDataString(App.DemoSelect));
        }
        if (L.En) q.Add("lang=en");
        return $"https://{VirtualHost}/index.html" + (q.Count > 0 ? "?" + string.Join("&", q) : "");
    }

    /// <summary>
    /// 화면 언어를 바꿨을 때: 말풍선·기록 문구는 기록 파일을 다시 읽어 새 언어로 만들고(감시기), 알림 영역 메뉴·휴대폰 요약도 맞춘 뒤
    /// 화면을 새 주소로 다시 연다(화면 글은 열 때 정해지므로).
    /// </summary>
    private void ApplyLanguage()
    {
        L.En = App.Settings.Language == "en";
        _tray?.ApplyLanguage();
        _cloud.SetLanguage(L.Code);
        _watcher.Reparse();
        Web.CoreWebView2?.Navigate(PageUrl());
        _widgetWindow?.ReloadLanguage();
        _settingsWindow?.ReloadLanguage();

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
            widget = s.Widget,
            pet = s.Pet,
            petSessionId = s.PetSessionId ?? "",
            petCompact = s.PetCompact,
            petNoBg = s.PetNoBg,
            petIsland = s.PetIsland,
            petTopmost = s.PetTopmost,
            appMode = s.AppMode,
            widgetSize = s.WidgetSize,
            language = s.Language == "en" ? "en" : "ko",
            remoteAwayOnLock = s.RemoteAwayOnLock,
            remoteAwayIdleMinutes = s.RemoteAwayIdleMinutes,
            update = new
            {
                latest = _updates.Latest, newer = _updates.IsNewer, url = _updates.Url, error = _updates.Error, checkedAt = _updates.CheckedMs,
                canInstall = CanInstallUpdate(),
                install = new { state = _updater.State, percent = _updater.Percent, error = _updater.Error },
            },
            tray = _tray != null,
        });
    }

    private static string WithVersion(string title) => $"{title} v{App.Version}";

    private void Post(string json)
    {
        if (_pageReady) Web.CoreWebView2?.PostWebMessageAsJson(json);
        _widgetWindow?.Post(json);
        _settingsWindow?.Post(json);

    }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        // 앱 자체 화면(office.local)이 보낸 것만 — 다른 주소의 페이지(휴대폰 페이지 등)가 설정을 바꾸지 못하게
        if (!Uri.TryCreate(e.Source, UriKind.Absolute, out var source) || !source.Host.Equals(VirtualHost, StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;
            switch (root.GetProperty("cmd").GetString())
            {
                case "ready":
                    if (sender != null) _pageReady = true;
                    if (_watcher.LastSnapshot is { } snap) Post(snap);
                    if (!App.DemoMode)
                    {
                        Post(_cloud.StatusJson());
                        Post(AppSettingsJson());
                        // 저장해 둔 별명·메모를 화면에 돌려준다(화면이 자기 것과 합친 뒤 다시 share 로 보낸다)
                        Post("{\"type\":\"share\",\"data\":" + (LoadShare() is { } saved && IsJsonObject(saved) ? saved : "null") + "}");
                        Post("{\"type\":\"remote\",\"data\":" + _remote.StateJson() + "}");
                    }
                    if (sender != null && _pendingSelect is { } sel) { _pendingSelect = null; SelectSession(sel); }
                    if (sender != null && _pendingSettings is { } setTab) { _pendingSettings = null; OpenSettings(setTab); }
                    break;
                case "app-setting":   // 앱 쪽 설정(app.json), autoStart 는 시작 프로그램 등록
                    if (!App.DemoMode && root.TryGetProperty("key", out var key) && root.TryGetProperty("value", out var val))
                    {
                        bool on = val.ValueKind == JsonValueKind.True;
                        int num = val.ValueKind == JsonValueKind.Number && val.TryGetInt32(out var n) ? n : 0;
                        var s = App.Settings;
                        bool langChanged = false;
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
                                case "remoteAwayOnLock": s.RemoteAwayOnLock = on; break;
                                case "remoteAwayIdleMinutes": s.RemoteAwayIdleMinutes = Math.Clamp(num, 0, 60); break;
                                case "updateCheck":
                                    s.UpdateCheck = on;
                                    if (on) _ = _updates.CheckAsync();
                                    break;
                                case "autoStart": AutoStart.Set(on); break;
                                case "language":   // 화면 언어(값은 글자 "ko" | "en") — 저장한 뒤 새 언어로 다시 연다(아래)
                                {
                                    var lang = val.ValueKind == JsonValueKind.String && val.GetString() == "en" ? "en" : "ko";
                                    langChanged = lang != (s.Language == "en" ? "en" : "ko");
                                    s.Language = lang;
                                    break;
                                }
                            }
                            s.Save();
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                        {
                            MessageBox.Show(this, L.T($"설정을 저장하지 못했습니다.\r\n\r\n{ex.Message}", $"Couldn't save the setting.\r\n\r\n{ex.Message}"),
                                "Claude Office", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                        Post(AppSettingsJson());   // 실제로 바뀐 값으로 스위치를 맞춘다
                        if (langChanged) ApplyLanguage();
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
                case "cloud-setup":   // 점검 도구 전용: 이 PC 의 시험 서버를 쓴다
                    if (!App.DemoMode && root.TryGetProperty("text", out var text))
                        Post(JsonSerializer.Serialize(new { type = "cloud-setup", error = _cloud.Setup(text.GetString() ?? "") }));
                    break;
                case "cloud-phone-edit":
                    if (!App.DemoMode) _cloud.SetPhoneEdit(root.TryGetProperty("value", out var pe) && pe.ValueKind == JsonValueKind.True);
                    break;
                case "widget":
                    if (!App.DemoMode) SetWidget(root.TryGetProperty("value", out var wv) && wv.ValueKind == JsonValueKind.True);
                    break;
                case "widget-size":
                    if (root.TryGetProperty("height", out var wh) && wh.TryGetDouble(out var wheight)) FitWidgetHeight(wheight);
                    break;
                case "pet":
                    if (!App.DemoMode)
                    {
                        var sid = root.TryGetProperty("sid", out var ps) && ps.ValueKind == JsonValueKind.String ? ps.GetString() : null;
                        SetPet(root.TryGetProperty("value", out var pv) && pv.ValueKind == JsonValueKind.True, sid);
                    }
                    break;
                case "pet-session":
                {
                    if (!App.DemoMode)
                    {
                        var raw = root.TryGetProperty("id", out var psi) && psi.ValueKind == JsonValueKind.String ? psi.GetString()
                                : root.TryGetProperty("value", out var psv) && psv.ValueKind == JsonValueKind.String ? psv.GetString()
                                : null;
                        if (raw != null)
                        {
                            App.Settings.PetSessionId = string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
                            TrySaveSettings();
                            _remote.SetPet(App.Settings.Pet, App.Settings.PetSessionId);
                            Post(AppSettingsJson());
                        }
                    }
                    break;
                }
                case "pet-compact":
                    if (!App.DemoMode && root.TryGetProperty("value", out var pcv))
                    {
                        App.Settings.PetCompact = pcv.ValueKind == JsonValueKind.True;
                        TrySaveSettings();
                        Post(AppSettingsJson());
                    }
                    break;
                case "pet-nobg":
                    if (!App.DemoMode && root.TryGetProperty("value", out var pnb))
                    {
                        App.Settings.PetNoBg = pnb.ValueKind == JsonValueKind.True;
                        TrySaveSettings();
                        _widgetWindow?.ApplyBackdrop(App.Settings.PetNoBg);
                        Post(AppSettingsJson());
                    }
                    break;
                case "pet-topmost":
                    if (!App.DemoMode && root.TryGetProperty("value", out var ptop))
                    {
                        bool on = ptop.ValueKind == JsonValueKind.True;
                        App.Settings.PetTopmost = on;
                        TrySaveSettings();
                        _widgetWindow?.SetTopmost(on);
                        _widgetWindow?.Post(JsonSerializer.Serialize(new { type = "pet-topmost", value = on }));
                        Post(AppSettingsJson());
                    }
                    break;
                case "app-mode":
                    if (root.TryGetProperty("mode", out var mElem) && mElem.GetString() is { Length: > 0 } m)
                    {
                        bool initial = root.TryGetProperty("initial", out var ini) && ini.GetBoolean();
                        App.Settings.AppMode = m;
                        TrySaveSettings();
                        _widgetWindow?.Post(JsonSerializer.Serialize(new { type = "app-mode", mode = m }));
                        if (_widgetWindow != null)
                        {
                            _widgetWindow.ApplyBackdrop(m == "office" ? false : App.Settings.PetNoBg);
                            _widgetWindow.SetAppMode(m, initial);
                        }
                    }
                    break;
                case "widget-size-mode":

                    if (!App.DemoMode && root.TryGetProperty("size", out var wszElem) && wszElem.GetString() is { Length: > 0 } wsz)

                    {

                        App.Settings.WidgetSize = wsz;

                        TrySaveSettings();

                        _widgetWindow?.SetWidgetSize(wsz);

                        _widgetWindow?.Post(JsonSerializer.Serialize(new { type = "widget-size-mode", size = wsz }));

                        Post(JsonSerializer.Serialize(new { type = "widget-size-mode", size = wsz }));

                    }

                    break;

                case "pet-backdrop":
                    if (!App.DemoMode && root.TryGetProperty("noBg", out var pbd))
                    {
                        _widgetWindow?.ApplyBackdrop(pbd.ValueKind == JsonValueKind.True);
                    }
                    break;
                case "pet-mode":
                    if (!App.DemoMode && root.TryGetProperty("mode", out var pmd) && pmd.GetString() is { } modeStr)
                    {
                        bool isIsland = modeStr == "island";
                        App.Settings.PetIsland = isIsland;
                        TrySaveSettings();
                        _widgetWindow?.SetPetMode(isIsland);
                        _tray?.SetPetMode(App.Settings.Pet ? (isIsland ? TrayIcon.PetTrayMode.Island : TrayIcon.PetTrayMode.Normal) : TrayIcon.PetTrayMode.None);
                    }
                    break;
                case "pet-say":
                    if (!App.DemoMode && root.TryGetProperty("sid", out var saySid) && root.TryGetProperty("text", out var sayTxt)
                        && saySid.GetString() is { Length: > 0 } sId && sayTxt.GetString() is { Length: > 0 } sText)
                    {
                        _remote.SayFromPc(sId, sText);
                    }
                    break;
                case "broadcast-say":
                {
                    if (!App.DemoMode && root.TryGetProperty("text", out var bTxt) && bTxt.GetString() is { Length: > 0 } broadcastText)
                    {
                        int sentCount = 0;
                        if (_watcher.LastSnapshot is { } bSnap)
                        {
                            using var snapDoc = JsonDocument.Parse(bSnap);
                            if (snapDoc.RootElement.TryGetProperty("sessions", out var sessArr))
                            {
                                foreach (var sItem in sessArr.EnumerateArray())
                                {
                                    var sid = sItem.TryGetProperty("id", out var sidProp) ? sidProp.GetString() : null;
                                    var act = sItem.TryGetProperty("act", out var actProp) ? actProp.GetString() : null;
                                    if (!string.IsNullOrEmpty(sid) && act != "away")
                                    {
                                        _remote.SayFromPc(sid, broadcastText);
                                        sentCount++;
                                    }
                                }
                            }
                        }
                        Post(JsonSerializer.Serialize(new { type = "broadcast-result", count = sentCount }));
                    }
                    break;
                }
                case "pet-cancel-say":
                    if (!App.DemoMode && root.TryGetProperty("sid", out var cancelSid) && cancelSid.GetString() is { Length: > 0 } cSid)
                    {
                        _remote.CancelQueued(cSid);
                    }
                    break;
                case "pet-decide":
                    if (!App.DemoMode && root.TryGetProperty("id", out var askId) && askId.GetString() is { Length: > 0 } aId)
                    {
                        bool? allow = root.TryGetProperty("allow", out var al)
                            ? al.ValueKind == JsonValueKind.True ? true : al.ValueKind == JsonValueKind.False ? false : null
                            : null;
                        var why = root.TryGetProperty("text", out var wt) && wt.ValueKind == JsonValueKind.String ? wt.GetString() : null;
                        _remote.DecideFromPc(aId, allow, why);
                    }
                    break;
                case "pet-open-office":
                    ShowFromTray();
                    if (root.TryGetProperty("id", out var openId) && openId.GetString() is { Length: > 0 } oId)
                        SelectSession(oId);
                    break;
                case "pet-drag":
                    _widgetWindow?.StartDrag();
                    break;
                case "pet-resize-start":
                    if (root.TryGetProperty("edge", out var rse) && rse.GetString() is { } startEdge)
                        _widgetWindow?.OnResizeStart(startEdge);
                    break;
                case "pet-resize-move":
                    if (root.TryGetProperty("edge", out var rme) && rme.GetString() is { } moveEdge
                        && root.TryGetProperty("deltaY", out var rmd) && rmd.TryGetDouble(out var moveDy))
                        _widgetWindow?.OnResizeMove(moveEdge, moveDy);
                    break;
                case "pet-resize-end":
                    if (root.TryGetProperty("edge", out var ree) && ree.GetString() is { } endEdge
                        && root.TryGetProperty("deltaY", out var red) && red.TryGetDouble(out var endDy))
                        _widgetWindow?.OnResizeEnd(endEdge, endDy);
                    break;
                case "pet-restore-height":
                    _widgetWindow?.RestoreSavedHeight();
                    break;

                case "pet-size":
                    if (root.TryGetProperty("height", out var ph) && ph.TryGetDouble(out var pheight))
                        _widgetWindow?.FitHeight(pheight);
                    if (root.TryGetProperty("width", out var pw) && pw.TryGetDouble(out var pwidth))
                        _widgetWindow?.FitWidth(pwidth);
                    break;
                case "notify-test":
                    if (_tray is null) break;
                    _tray.Notify(L.T("✋ 알림 시험", "✋ Test notification"), L.T("이렇게 알려 드려요 — 누르면 창이 열립니다", "This is how you'll be notified — click to open the window"),
                                 silent: !App.Settings.NotifySound, onClick: ShowFromTray);
                    Post(JsonSerializer.Serialize(new { type = "sound", kind = "test" }));
                    break;
                case "update-install":
                    if (!App.DemoMode) StartUpdate();
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
                case "focus-session":   // 이 세션이 돌아가는 VS Code·터미널 창을 찾아 맨 앞으로 가져온다
                    if (!App.DemoMode && !App.Offscreen && root.TryGetProperty("id", out var fsid) && fsid.GetString() is { Length: > 0 } targetSid)
                    {
                        FocusSessionWindow(targetSid);
                        SelectSession(targetSid);
                    }
                    break;
                case "stop-session":   // 진행 중인 세션에 작업 중단(Ctrl+C) 신호를 보낸다
                    if (!App.DemoMode && !App.Offscreen && root.TryGetProperty("id", out var stid) && stid.GetString() is { Length: > 0 } stopSid)
                    {
                        StopSession(stopSid);
                    }
                    break;
                case "share-export":
                    if (!App.DemoMode) ExportShare();
                    break;
                case "share-import":
                    if (!App.DemoMode) ImportShare();
                    break;
                case "reset-settings":
                    if (!App.DemoMode) ResetSettings();
                    break;
                case "report-issue":
                    OpenUrl(IssueUrl());
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
                            L.T("휴대폰 연결용 열쇠(암호 키)를 새로 만들까요?\r\n\r\n지금 휴대폰에 열려 있는 화면은 더 이상 해독하지 못하고, QR을 다시 찍어야 합니다.\r\n" +
                                "QR이나 주소가 다른 사람에게 보였을 때 쓰세요.",
                                "Make a new key (encryption key) for the phone connection?\r\n\r\nPhones that have the page open can no longer decrypt it and must scan the QR again.\r\n" +
                                "Use this if someone else saw the QR code or link."),
                            L.T("Claude Office — 휴대폰 연결", "Claude Office — Phone connection"), MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK)
                        _cloud.Rekey();
                    break;
                case "topmost":
                    // 작게 보기 중에는 늘 맨 위(나오면 화면이 이 설정을 다시 보낸다)
                    Topmost = App.Settings.Widget || (root.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.True);
                    break;
                case "hooks":   // Claude Code 연결·해제(상태 알림과 휴대폰 지시·승인을 한 번에)
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

    // ───────────────────────── 별명·메모 내보내기/가져오기 · 설정 초기화 · 문제 보고 ─────────────────────────

    private void ExportShare()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = L.T("별명·메모 내보내기", "Export nicknames & memos"),
            FileName = $"claude-office-notes-{DateTime.Now:yyyyMMdd}.json",
            Filter = L.T("JSON 파일|*.json", "JSON files|*.json"),
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            File.WriteAllText(dlg.FileName, LoadShare() ?? "{\"office\":\"\",\"notes\":{}}", new System.Text.UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, L.T($"저장하지 못했습니다.\r\n\r\n{ex.Message}", $"Couldn't save.\r\n\r\n{ex.Message}"), "Claude Office", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>내보낸 파일을 읽어 화면에 넘긴다 — 화면이 고친 시각(t) 기준으로 합친다(이 PC에 없거나 파일 쪽이 더 나중 것만).</summary>
    private void ImportShare()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Title = L.T("별명·메모 가져오기", "Import nicknames & memos"), Filter = L.T("JSON 파일|*.json", "JSON files|*.json") };
        if (dlg.ShowDialog(this) != true) return;
        string text;
        int count;
        try
        {
            text = File.ReadAllText(dlg.FileName);
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("notes", out var notes) || notes.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException(L.T("Claude Office 에서 내보낸 별명·메모 파일이 아닙니다.", "This is not a nickname & memo file exported from Claude Office."));
            count = notes.EnumerateObject().Count();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            MessageBox.Show(this, L.T($"가져오지 못했습니다.\r\n\r\n{ex.Message}", $"Couldn't import.\r\n\r\n{ex.Message}"), "Claude Office", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Post("{\"type\":\"share\",\"data\":" + text + "}");
        MessageBox.Show(this, L.T($"별명·메모 {count}개를 읽었습니다. 이 PC에 없거나 파일 쪽이 더 나중에 고친 것만 반영합니다.",
                                  $"Read {count} nicknames & memos. Only the ones missing on this PC or edited later in the file are applied."),
            "Claude Office", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>화면·알림·창 설정을 처음 값으로. 별명·메모·사무실 이름·휴대폰 보기 설정·자동 실행은 그대로 둔다.</summary>
    private void ResetSettings()
    {
        if (MessageBox.Show(this,
                L.T("설정을 처음 값으로 되돌릴까요?\r\n\r\n직원 표시·창·알림·새 버전 알림 설정이 처음 값이 됩니다.\r\n" +
                    "별명·메모, 사무실 이름, 팀 이름·순서, 화면 언어, 휴대폰 보기 설정, 훅 연결, Windows 자동 실행은 그대로 둡니다.",
                    "Restore the default settings?\r\n\r\nEmployee display, window, notification and update alert settings go back to defaults.\r\n" +
                    "Nicknames, memos, the office name, team names and order, phone view, hooks, language and Windows startup stay as they are."),
                L.T("Claude Office — 설정 초기화", "Claude Office — Reset settings"), MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            return;
        bool wasWidget = App.Settings.Widget;
        App.Settings.StartMinimized = false;
        App.Settings.CloseToTray = true;
        App.Settings.NotifyWaiting = App.Settings.NotifyDone = App.Settings.NotifySound = true;
        App.Settings.NotifyDoneMinutes = 10;
        App.Settings.QuietEnabled = false;
        App.Settings.QuietStart = 22;
        App.Settings.QuietEnd = 8;
        App.Settings.UpdateCheck = true;
        if (wasWidget) SetWidget(false);
        App.Settings.PetCompact = App.Settings.PetNoBg = false;
        _widgetWindow?.ApplyBackdrop(false);
        if (App.Settings.Pet) SetPet(false, "");
        else App.Settings.PetSessionId = null;
        Topmost = false;
        TrySaveSettings();
        Post("{\"type\":\"reset\"}");   // 화면이 자기 설정(localStorage)을 지우고 다시 읽는다
        Post(AppSettingsJson());
    }

    /// <summary>GitHub 이슈 작성 화면 — 버전·Windows·WebView2 정보만 미리 채운다(오류 기록은 사용자가 확인하고 붙이게).</summary>
    private string IssueUrl()
    {
        string webview;
        try { webview = CoreWebView2Environment.GetAvailableBrowserVersionString(); }
        catch (WebView2RuntimeNotFoundException) { webview = L.T("없음", "none"); }
        bool hasErrorLog = File.Exists(Path.Combine(App.DataDir, "error.log"));
        var body = L.En
            ? "**What happened?**\n\n\n**How can it be reproduced?**\n\n\n---\n" +
              $"- Version: {App.Version}\n- Windows: {Environment.OSVersion.Version}\n- WebView2: {webview}\n" +
              $"- Hooks: {(HookSetup.IsInstalled() ? "connected" : "not connected")} · Git Bash: {(HookSetup.FindGitBash() != null ? "yes" : "no")}\n" +
              (hasErrorLog ? "- Error log: please paste the relevant part of error.log in the data folder (⚙️ → About → Data folder) — check it first, it may contain folder paths and your user name\n" : "")
            : "**무슨 일이 있었나요?**\n\n\n**어떻게 하면 다시 생기나요?**\n\n\n---\n" +
              $"- 버전: {App.Version}\n- Windows: {Environment.OSVersion.Version}\n- WebView2: {webview}\n" +
              $"- 훅: {(HookSetup.IsInstalled() ? "연결됨" : "안 됨")} · Git Bash: {(HookSetup.FindGitBash() != null ? "있음" : "없음")}\n" +
              (hasErrorLog ? "- 오류 기록: 데이터 폴더(⚙️ → 정보 → 데이터 폴더)의 error.log 에서 관련 부분을 붙여 주세요 — 폴더 경로·사용자 이름이 들어 있을 수 있으니 확인 후\n" : "");
        return $"https://github.com/{UpdateChecker.Repo}/issues/new?body={Uri.EscapeDataString(body)}";
    }

    private string? CwdOf(string? id)
    {
        if (id is null || _watcher.LastSnapshot is not { } snap) return null;
        using var doc = JsonDocument.Parse(snap);
        foreach (var s in doc.RootElement.GetProperty("sessions").EnumerateArray())
            if (s.GetProperty("id").GetString() == id)
                return s.TryGetProperty("cwd", out var c) ? c.GetString() : null;
        return null;
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    private static readonly HashSet<string> TerminalOrIdeProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Code", "Cursor", "Windsurf", "WindowsTerminal", "pwsh", "powershell", "cmd",
        "mintty", "wezterm-gui", "alacritty", "wt", "devenv", "rider64", "idea64", "claude", "aider", "node",
    };

    /// <summary>세션의 작업 폴더명·세션명·실행 위치(VS Code/터미널)를 바탕으로 해당 실행 창을 찾아 맨 앞으로 활성화한다.</summary>
    private void FocusSessionWindow(string? id)
    {
        if (string.IsNullOrEmpty(id) || _watcher.LastSnapshot is not { } snap) return;
        string? cwd = null, name = null, title = null, entry = null, team = null;
        using (var doc = JsonDocument.Parse(snap))
        {
            foreach (var s in doc.RootElement.GetProperty("sessions").EnumerateArray())
            {
                if (s.GetProperty("id").GetString() != id) continue;
                cwd = s.TryGetProperty("cwd", out var c) ? c.GetString() : null;
                name = s.TryGetProperty("name", out var n) ? n.GetString() : null;
                title = s.TryGetProperty("title", out var t) ? t.GetString() : null;
                entry = s.TryGetProperty("entry", out var e) ? e.GetString() : null;
                team = s.TryGetProperty("team", out var tm) ? tm.GetString() : null;
                break;
            }
        }

        var leaf = !string.IsNullOrWhiteSpace(cwd)
            ? Path.GetFileName(cwd.TrimEnd('\\', '/'))
            : (!string.IsNullOrWhiteSpace(team) ? team.Split('/').LastOrDefault() : null);

        uint selfPid = (uint)Environment.ProcessId;
        IntPtr bestHwnd = IntPtr.Zero;
        int bestScore = 0;
        var sb = new System.Text.StringBuilder(512);

        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd)) return true;
            GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == 0 || pid == selfPid) return true;
            sb.Clear();
            if (GetWindowText(hWnd, sb, sb.Capacity) <= 0) return true;
            var wTitle = sb.ToString();

            string procName = "";
            try { procName = Process.GetProcessById((int)pid).ProcessName; }
            catch { }

            bool isTermOrIde = TerminalOrIdeProcesses.Contains(procName);
            int score = 0;

            if (!string.IsNullOrWhiteSpace(cwd) && wTitle.Contains(cwd, StringComparison.OrdinalIgnoreCase))
                score += 100;
            if (!string.IsNullOrWhiteSpace(leaf) && leaf.Length >= 2 && wTitle.Contains(leaf, StringComparison.OrdinalIgnoreCase))
                score += 60;
            if (!string.IsNullOrWhiteSpace(name) && name.Length >= 2 && wTitle.Contains(name, StringComparison.OrdinalIgnoreCase))
                score += 45;
            if (!string.IsNullOrWhiteSpace(title) && title.Length >= 2 && wTitle.Contains(title, StringComparison.OrdinalIgnoreCase))
                score += 35;

            if (isTermOrIde)
            {
                score += 20;
                if (entry != null && entry.Contains("vscode", StringComparison.OrdinalIgnoreCase)
                    && procName is "Code" or "Cursor" or "Windsurf")
                    score += 15;
                else if (entry != null && entry.Contains("cli", StringComparison.OrdinalIgnoreCase)
                    && procName is "WindowsTerminal" or "pwsh" or "powershell" or "cmd" or "mintty" or "wezterm-gui")
                    score += 15;
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestHwnd = hWnd;
            }
            return true;
        }, IntPtr.Zero);

        bool isVsCode = entry != null && entry.Contains("vscode", StringComparison.OrdinalIgnoreCase);
        if (isVsCode && Guid.TryParse(id, out _))
        {
            // VS Code 확장(claude-vscode)의 공식 URI 프로토콜로 해당 세션 탭을 직접 열고 선택한다
            try
            {
                Process.Start(new ProcessStartInfo($"vscode://anthropic.claude-code/open?session={id}") { UseShellExecute = true });
            }
            catch { }
        }

        if (bestHwnd != IntPtr.Zero && bestScore >= 20)
        {
            if (IsIconic(bestHwnd)) ShowWindow(bestHwnd, 9);   // SW_RESTORE
            SetForegroundWindow(bestHwnd);
        }
        else if (!isVsCode && !string.IsNullOrWhiteSpace(cwd) && Directory.Exists(cwd))
        {
            // 일치하는 터미널·VS Code 창이 하나도 없으면 작업 폴더를 열어 준다
            try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{cwd}\"") { UseShellExecute = true }); }
            catch { }
        }
    }

    /// <summary>세션의 실행 중인 명령을 긴급 중단한다. 훅 플래그 설정 및 실행 중인 도구 프로세스 트리를 즉시 강제 종료한다.</summary>
    private void StopSession(string? id)
    {
        if (string.IsNullOrEmpty(id)) return;
        _remote.RequestStop(id);
        string? cwd = null;
        if (_watcher.LastSnapshot is { } snap)
        {
            try
            {
                using var doc = JsonDocument.Parse(snap);
                foreach (var s in doc.RootElement.GetProperty("sessions").EnumerateArray())
                {
                    if (s.GetProperty("id").GetString() == id)
                    {
                        cwd = s.TryGetProperty("cwd", out var c) ? c.GetString() : null;
                        break;
                    }
                }
            }
            catch { }
        }
        ApprovalProbe.TerminateToolProcesses(id, cwd);
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
    /// Claude Code 연결·해제(설정 → 🖥️ 일반, 상단 🔕 버튼, 처음 실행할 때 한 번). Claude Code 전역 설정에 훅을 넣거나 빼는 일이라 반드시 확인을 받는다.
    /// 연결은 한 번에 전부: 상태 알림(승인·질문·세션 종료)과 휴대폰 지시·승인. 예전에 상태 알림만 넣어 둔 것이면 다시 연결해 나머지를 채운다.
    /// </summary>
    private void ToggleHooks(bool firstRun = false)
    {
        var path = HookSetup.SettingsPath;
        bool connected = HookSetup.IsInstalled() && HookSetup.IsRemoteInstalled();
        var question = L.En
            ? connected
                ? $"Disconnect from Claude Code?\r\n\r\nOnly the lines Claude Office added to the Claude Code settings are removed; nothing else changes.\r\n{path}\r\n\r\n" +
                  "Afterwards, whether Claude is waiting is only guessed, and the phone can't send instructions or approve."
                : (firstRun ? "Welcome! " : "") + "Connect Claude Office to Claude Code? (recommended)\r\n\r\n" +
                  "Once connected:\r\n" +
                  "  · an employee raises a hand the moment Claude waits for an approval or asks a question\r\n" +
                  "  · closing a session shows the employee going home right away\r\n" +
                  "  · with the phone connected, you can send instructions and approve from the phone while you're away\r\n\r\n" +
                  $"A few lines are added to the Claude Code settings (the original is kept as settings.json.claudeoffice.bak):\r\n{path}\r\n" +
                  "They only talk to this app on this PC and do nothing while the app is closed. Disconnect any time in ⚙️ → 🖥️ General.\r\n\r\n" +
                  "Keep your phone QR to yourself — whoever has it can give Claude on this PC work while you're away."
            : connected
                ? $"Claude Code 연결을 끊을까요?\r\n\r\nClaude Code 설정에서 Claude Office 가 넣은 줄만 빼고 다른 설정은 그대로 둡니다.\r\n{path}\r\n\r\n" +
                  "끊으면 승인을 기다리는지는 짐작으로만 보이고, 휴대폰에서 지시·승인을 할 수 없어요."
                : (firstRun ? "처음 오셨네요! " : "") + "Claude Code와 연결할까요? (권장)\r\n\r\n" +
                  "연결하면\r\n" +
                  "  · Claude가 승인을 기다리거나 질문하면 직원이 바로 손을 들고\r\n" +
                  "  · 세션을 닫으면 바로 퇴근으로 보이고\r\n" +
                  "  · 휴대폰을 연결해 두면, 자리를 비운 동안 휴대폰에서 지시·승인도 할 수 있어요\r\n\r\n" +
                  $"Claude Code 설정 파일에 몇 줄을 넣습니다(원래 파일은 settings.json.claudeoffice.bak 으로 남겨요):\r\n{path}\r\n" +
                  "이 PC 안에서 이 프로그램과만 주고받고, 프로그램이 꺼져 있으면 아무 일도 하지 않아요. ⚙️ → 🖥️ 일반에서 언제든 끊을 수 있어요.\r\n\r\n" +
                  "휴대폰 QR은 남에게 보여 주지 마세요 — 가진 사람은 자리를 비운 동안 이 PC의 Claude에게 일을 시킬 수 있어요.";
        var title = L.T("Claude Office — Claude Code 연결", "Claude Office — Claude Code connection");
        var answer = MessageBox.Show(this, question, title, firstRun ? MessageBoxButton.YesNo : MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (answer is not (MessageBoxResult.OK or MessageBoxResult.Yes))
            return;

        try
        {
            if (connected)
            {
                _remote.SetAway(false, "hooks-off");
                HookSetup.Uninstall();   // 상태 알림·휴대폰 지시 모두
                _remote.ReleaseListeners();   // 기다리던 지시 대기 훅도 끝나게(안 그러면 이 프로그램을 끌 때까지 붙어 있다)
            }
            else HookSetup.InstallRemote();   // 상태 알림이 없으면 그것부터 넣고 휴대폰 지시·승인까지
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            MessageBox.Show(this, L.T($"Claude Code 설정 파일을 고치지 못했습니다. 파일은 그대로입니다.\r\n\r\n{ex.Message}",
                                      $"Couldn't edit the Claude Code settings file. It is unchanged.\r\n\r\n{ex.Message}"),
                title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        _watcher.RefreshHookStatus();
        OnRemoteChanged();
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
