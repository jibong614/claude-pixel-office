using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace ClaudeOffice;

public partial class App : Application
{
    /// <summary>--autostart : Windows 로그인 때 시작 프로그램으로 켜짐 — 늘 알림 영역에 숨긴 채로 시작한다.</summary>
    public static bool AutoStarted { get; private set; }

    // 한 데이터 폴더에 하나만 실행(숨겨 둔 걸 잊고 또 켜도 새로 뜨지 않고 기존 창을 보여 준다)
    private Mutex? _single;
    private EventWaitHandle? _showSignal, _exitSignal;
    /// <summary>--demo 로 실행하면 실제 기록 대신 모든 상태를 돌아가며 보여 주는 견본 화면을 띄운다.</summary>
    public static bool DemoMode { get; private set; }

    /// <summary>--select=demo-b1 : 데모에서 해당 직원의 상세 패널을 연 채로 시작(화면 점검용).</summary>
    public static string? DemoSelect { get; private set; }

    /// <summary>
    /// --offscreen : 점검 도구용. 창을 화면 밖에 띄우고 포커스를 뺏지 않으며 작업표시줄에도 안 나온다
    /// (시험이 돌 때 작업 중인 화면 앞에 창이 불쑥 뜨지 않게). 최소화와 달리 화면은 계속 그려져 캡처할 수 있다.
    /// </summary>
    public static bool Offscreen { get; private set; }

    internal static AppSettings Settings { get; private set; } = new();

    /// <summary>표시용 버전(예: 1.0.0). 원본은 csproj 의 Version.</summary>
    public static string Version { get; } = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "?";

    /// <summary>
    /// 데이터 폴더(훅 기록, 화면 설정, 휴대폰 보기 키). CLAUDEOFFICE_DATA_DIR 이 있으면 그곳 —
    /// 점검 도구(tools\pagetest.ps1)가 평소 쓰는 설정·키를 건드리지 않게 하려는 것.
    /// </summary>
    public static string DataDir { get; } =
        Environment.GetEnvironmentVariable("CLAUDEOFFICE_DATA_DIR") is { Length: > 0 } dir
            ? dir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClaudeOffice");

    protected override void OnStartup(StartupEventArgs e)
    {
        bool Has(string flag) => e.Args.Any(a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));
        DemoMode = Has("--demo");
        DemoSelect = e.Args.FirstOrDefault(a => a.StartsWith("--select=", StringComparison.OrdinalIgnoreCase))?["--select=".Length..];
        Offscreen = Has("--offscreen");
        AutoStarted = Has("--autostart");
        DispatcherUnhandledException += OnUnhandled;
        base.OnStartup(e);

        // 견본은 몇 개를 띄워도 된다. 실제 감시는 데이터 폴더마다 하나(훅 포트·휴대폰 업로드가 겹치지 않게)
        if (!DemoMode)
        {
            var name = "ClaudeOffice-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(DataDir.ToLowerInvariant())))[..16];
            _single = new Mutex(true, name, out bool first);
            if (!first)
            {
                // 이미 켜져 있다: 그쪽 창을 보이게 하거나(--exit 면 끝내게) 하고 이쪽은 바로 끝낸다.
                // 사용자가 방금 실행한 이쪽에 맨 앞으로 올 권한이 있으니 그쪽 창이 앞으로 올 수 있게 넘겨 준다
                AllowSetForegroundWindow(-1);
                try { EventWaitHandle.OpenExisting(name + (Has("--exit") ? "-exit" : "-show")).Set(); }
                catch (WaitHandleCannotBeOpenedException) { }
                Shutdown();
                return;
            }
            if (Has("--exit")) { Shutdown(); return; }   // 끌 것이 없다

            _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, name + "-show");
            _exitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, name + "-exit");
            ThreadPool.RegisterWaitForSingleObject(_showSignal, (_, _) => Dispatcher.BeginInvoke(() => (MainWindow as MainWindow)?.ShowFromTray()), null, -1, false);
            ThreadPool.RegisterWaitForSingleObject(_exitSignal, (_, _) => Dispatcher.BeginInvoke(() => (MainWindow as MainWindow)?.ExitApp()), null, -1, false);
        }

        Settings = AppSettings.Load();
        SessionEnding += (_, _) => (MainWindow as MainWindow)?.PrepareExit();   // Windows 로그오프·종료: 숨기지 말고 끝낸다

        var window = new MainWindow();
        MainWindow = window;
        // 자동 실행이거나 "시작 시 작업표시줄로"면 창을 띄우지 않고 알림 영역에만(화면은 아이콘을 누를 때 처음 만든다)
        if (!window.StartHidden) window.Show();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);   // -1 = ASFW_ANY

    protected override void OnExit(ExitEventArgs e)
    {
        try { _single?.ReleaseMutex(); } catch (ApplicationException) { }
        _single?.Dispose();
        base.OnExit(e);
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            File.AppendAllText(Path.Combine(DataDir, "error.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {e.Exception}\r\n\r\n");
        }
        catch (IOException) { }

        MessageBox.Show($"예기치 못한 오류가 발생했습니다.\r\n\r\n{e.Exception.Message}\r\n\r\n자세한 내용: {Path.Combine(DataDir, "error.log")}",
            "Claude Office", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
