using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using ClaudeOffice.Monitoring;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace ClaudeOffice;

/// <summary>
/// 작업표시줄 알림 영역(트레이 아이콘) 우클릭 메뉴의 '⚙️ 설정'에서
/// 메인 오피스 대시보드 창을 띄우지 않고 오직 설정 UI만 단독으로 띄워 주는 독립 윈도우.
/// 기존 설정 팝업의 라운드 모달 카드와 소프트 그림자를 그대로 재현하는 WindowStyle.None 테두리 없는 창.
/// </summary>
internal sealed class SettingsWindow : Window
{
    private const int DWMNCRP_DISABLED = 1;
    private const int DWMWCP_DONOTROUND = 1;
    private const int DWMWA_NCRENDERING_POLICY = 2;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWA_COLOR_NONE = unchecked((int)0xFFFFFFFE);

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins { public int Left, Right, Top, Bottom; }

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins pMarInset);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);

    private readonly WebView2 _web = new();
    private readonly string _virtualHost;
    private readonly string _webRoot;
    private readonly Action<CoreWebView2WebMessageReceivedEventArgs> _onMessage;
    private bool _ready;
    private bool _allowClose;
    private string? _pendingTab;

    public SettingsWindow(string virtualHost, string webRoot, Action<CoreWebView2WebMessageReceivedEventArgs> onMessage)
    {
        _virtualHost = virtualHost;
        _webRoot = webRoot;
        _onMessage = onMessage;

        Title = L.T("설정 — Claude Office", "Settings — Claude Office");
        Width = 720;
        Height = 640;
        MinWidth = 720;
        MinHeight = 640;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Brushes.Transparent;
        ShowInTaskbar = true;

        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 0,
            ResizeBorderThickness = new Thickness(0),
            GlassFrameThickness = new Thickness(-1),
            UseAeroCaptionButtons = false,
        });

        try
        {
            Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/office.ico"));
        }
        catch { }

        Content = _web;

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            ApplyBackdrop(hwnd);
        };

        Loaded += OnLoaded;
        Closing += (_, e) =>
        {
            if (_allowClose) return;
            e.Cancel = true;
            Hide();
        };
    }

    private static void ApplyBackdrop(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        try
        {
            var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            DwmExtendFrameIntoClientArea(hwnd, ref margins);

            int ncrp = DWMNCRP_DISABLED;
            DwmSetWindowAttribute(hwnd, DWMWA_NCRENDERING_POLICY, ref ncrp, sizeof(int));

            int corner = DWMWCP_DONOTROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));

            int border = DWMWA_COLOR_NONE;
            DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref border, sizeof(int));

            InvalidateRect(hwnd, IntPtr.Zero, true);
        }
        catch { }
    }

    public void StartDrag()
    {
        if (App.Offscreen) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        ReleaseCapture();
        SendMessage(hwnd, 0x00A1 /* WM_NCLBUTTONDOWN */, new IntPtr(2 /* HTCAPTION */), IntPtr.Zero);
    }

    public bool IsPageReady => _ready;

    public void ShowSettings(string? tab = null)
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();

        if (_ready)
        {
            if (tab != null)
                _web.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "settings", tab }));
        }
        else
        {
            _pendingTab = tab;
        }
    }

    public void Post(string json)
    {
        if (_ready)
        {
            _web.CoreWebView2?.PostWebMessageAsJson(json);
        }
    }

    public void ReloadLanguage()
    {
        Title = L.T("설정 — Claude Office", "Settings — Claude Office");
        _ready = false;
        _web.CoreWebView2?.Navigate(PageUrl());
    }

    public void CloseForce()
    {
        _allowClose = true;
        Close();
    }

    private string PageUrl() => $"https://{_virtualHost}/index.html?settings_window=1" + (L.En ? "&lang=en" : "");

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _web.DefaultBackgroundColor = System.Drawing.Color.Transparent;
            var env = await App.GetSharedEnvironmentAsync();
            try
            {
                await _web.EnsureCoreWebView2Async(env);
            }
            catch (System.Runtime.InteropServices.COMException ex) when ((uint)ex.HResult == 0x80010108)
            {
                App.ResetSharedEnvironment();
                env = await App.GetSharedEnvironmentAsync();
                await _web.EnsureCoreWebView2Async(env);
            }
        }
        catch (WebView2RuntimeNotFoundException)
        {
            return;
        }

        var core = _web.CoreWebView2;
        core.Settings.IsStatusBarEnabled = false;
#if !DEBUG
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
#endif
        core.SetVirtualHostNameToFolderMapping(_virtualHost, _webRoot, CoreWebView2HostResourceAccessKind.Allow);

        core.WebMessageReceived += (_, args) =>
        {
            try
            {
                using var doc = JsonDocument.Parse(args.WebMessageAsJson);
                if (doc.RootElement.TryGetProperty("cmd", out var cmd))
                {
                    var cmdStr = cmd.GetString();
                    if (cmdStr == "ready")
                    {
                        _ready = true;
                        if (_pendingTab != null)
                        {
                            var tab = _pendingTab;
                            _pendingTab = null;
                            core.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "settings", tab }));
                        }
                    }
                    else if (cmdStr == "close-settings")
                    {
                        Hide();
                        return;
                    }
                    else if (cmdStr == "settings-drag")
                    {
                        StartDrag();
                        return;
                    }
                }
            }
            catch { }
            _onMessage(args);
        };

        core.Navigate(PageUrl());
    }
}
