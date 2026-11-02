using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using ClaudeOffice.Monitoring;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace ClaudeOffice;

/// <summary>
/// 바탕화면 미니펫(MiniPet) 창: 대표 세션의 직원이 바탕화면 오른쪽 아래 구석 책상에 앉아 일하는 모습을 보여 주고,
/// 바로 지시를 던지거나 승인·거절·대표 세션 전환을 할 수 있는 작은 테두리 없는 항상-위 창.
/// 본창(MainWindow)을 ✕ 로 숨겨 두어도 바탕화면에 그대로 남아 있다.
/// </summary>
internal sealed class PetWindow : Window
{
    public const double DefaultWidth = 300;
    private const double MinPetHeight = 44, MaxPetHeight = 640;

    private readonly WebView2 _web = new();
    private readonly string _virtualHost;
    private readonly string _webRoot;
    private readonly Action<CoreWebView2WebMessageReceivedEventArgs> _onMessage;
    private readonly SubclassProc _webSubclass;
    private bool _ready;
    private bool _allowClose;
    private bool _subclassed;

    public PetWindow(string virtualHost, string webRoot, Action<CoreWebView2WebMessageReceivedEventArgs> onMessage, Action onUserClose)
    {
        _virtualHost = virtualHost;
        _webRoot = webRoot;
        _onMessage = onMessage;
        _webSubclass = OnWebChildWndProc;

        Title = "Claude Office — MiniPet";
        Width = DefaultWidth;
        Height = 260;
        MinWidth = DefaultWidth;
        MaxWidth = DefaultWidth;
        MinHeight = MinPetHeight;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = false;
        Background = Brushes.Transparent;
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 0,
            ResizeBorderThickness = new Thickness(0),
            GlassFrameThickness = new Thickness(-1),
            UseAeroCaptionButtons = false,
        });

        Content = _web;
        RestorePlacement();

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(hwnd)?.AddHook(OnParentWndProc);
            ApplyBackdrop(App.Settings.PetNoBg);
        };
        Loaded += OnLoaded;
        Closing += (_, e) =>
        {
            SavePlacement();
            if (_allowClose)
            {
                if (_subclassed && _web.Handle != IntPtr.Zero)
                {
                    RemoveWindowSubclass(_web.Handle, _webSubclass, 1);
                    _subclassed = false;
                }
                return;
            }
            e.Cancel = true;
            onUserClose();
        };
    }

    public bool IsPageReady => _ready;

    /// <summary>배경 숨김(투명) 여부에 따라 DWM 창 테두리·그림자·둥근 모서리 속성을 갱신한다.</summary>
    public void ApplyBackdrop(bool noBg)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        DwmExtendFrameIntoClientArea(hwnd, ref margins);

        // 배경 숨기기(투명)일 때는 Windows 11 사각형 창 테두리와 그림자를 끄고, 구석 방 카드일 때는 둥근 모서리와 그림자를 켠다
        int ncrp = noBg ? 1 /* DWMNCRP_DISABLED */ : 2 /* DWMNCRP_ENABLED */;
        DwmSetWindowAttribute(hwnd, 2 /* DWMWA_NCRENDERING_POLICY */, ref ncrp, sizeof(int));

        int corner = noBg ? 1 /* DWMWCP_DONOTROUND */ : 2 /* DWMWCP_ROUND */;
        DwmSetWindowAttribute(hwnd, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref corner, sizeof(int));

        int border = noBg ? unchecked((int)0xFFFFFFFE) /* DWMWA_COLOR_NONE */ : unchecked((int)0xFFFFFFFF) /* DWMWA_COLOR_DEFAULT */;
        DwmSetWindowAttribute(hwnd, 34 /* DWMWA_BORDER_COLOR */, ref border, sizeof(int));

        InvalidateRect(hwnd, IntPtr.Zero, true);
        if (_web.Handle != IntPtr.Zero) InvalidateRect(_web.Handle, IntPtr.Zero, true);
    }

    public void Post(string json)
    {
        if (_ready && IsVisible) _web.CoreWebView2?.PostWebMessageAsJson(json);
    }

    public void ReloadLanguage()
    {
        _ready = false;
        _web.CoreWebView2?.Navigate(PageUrl());
    }

    public void CloseForce()
    {
        _allowClose = true;
        SavePlacement();
        Close();
    }

    private string PageUrl() => $"https://{_virtualHost}/index.html?pet=1" + (L.En ? "&lang=en" : "");

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _web.DefaultBackgroundColor = System.Drawing.Color.Transparent;
            if (!_subclassed && _web.Handle != IntPtr.Zero)
            {
                _subclassed = SetWindowSubclass(_web.Handle, _webSubclass, 1, IntPtr.Zero);
            }
            var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(App.DataDir, "WebView2"));
            await _web.EnsureCoreWebView2Async(env);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            return;
        }

        if (!_subclassed && _web.Handle != IntPtr.Zero)
        {
            _subclassed = SetWindowSubclass(_web.Handle, _webSubclass, 1, IntPtr.Zero);
        }

        var core = _web.CoreWebView2;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
#if !DEBUG
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
#endif
        core.SetVirtualHostNameToFolderMapping(_virtualHost, _webRoot, CoreWebView2HostResourceAccessKind.Allow);
        core.NavigationStarting += (_, _) => _ready = false;
        core.WebMessageReceived += (_, args) =>
        {
            if (args.WebMessageAsJson.Contains("\"ready\"", StringComparison.Ordinal)) _ready = true;
            _onMessage(args);
        };
        core.Navigate(PageUrl());
    }

    // DWM 유리 영역(MARGINS -1)에서 GDI BLACK_BRUSH(0x00000000)는 알파=0(완전 투명)으로 합성된다.
    // WebView2 의 호스트 "static" 자식 창이 기본 회색(COLOR_BTNFACE)으로 배경을 칠하지 못하게 가로챈다.
    private static IntPtr OnParentWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x0014 /* WM_ERASEBKGND */)
        {
            if (GetClientRect(hwnd, out var rc))
                FillRect(wParam, ref rc, GetStockObject(4 /* BLACK_BRUSH */));
            handled = true;
            return new IntPtr(1);
        }
        return IntPtr.Zero;
    }

    private static IntPtr OnWebChildWndProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, nuint uIdSubclass, IntPtr dwRefData)
    {
        if (uMsg == 0x0014 /* WM_ERASEBKGND */)
        {
            if (GetClientRect(hWnd, out var rc))
                FillRect(wParam, ref rc, GetStockObject(4 /* BLACK_BRUSH */));
            return new IntPtr(1);
        }
        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    /// <summary>기본 위치는 바탕화면 오른쪽 아래(작업표시줄 위 여백 20px). 이전에 옮긴 자리가 화면 안에 있으면 그곳으로.</summary>
    public void RestorePlacement()
    {
        WindowStartupLocation = WindowStartupLocation.Manual;
        if (App.Offscreen)
        {
            Left = -20000;
            Top = -20000;
            ShowActivated = false;
            return;
        }
        var wa = SystemParameters.WorkArea;
        if (App.Settings.PetWindow is { } w && OnScreen(w))
        {
            if (w.Height >= MinPetHeight && w.Height <= MaxPetHeight) Height = w.Height;
            Left = w.Left;
            Top = w.Top;
        }
        else
        {
            Left = wa.Right - Width - 20;
            Top = wa.Bottom - Height - 20;
        }
    }

    /// <summary>미니펫 내용 높이에 창 높이를 맞추되, 아래쪽 모서리를 고정해 바탕화면 오른쪽 아래 자리에서 위로만 늘어나고 줄어들게 한다.</summary>
    public void FitHeight(double contentHeight)
    {
        if (WindowState != WindowState.Normal) return;
        double chrome = ActualHeight > 0 && _web.ActualHeight > 0 ? Math.Max(0, ActualHeight - _web.ActualHeight) : 0;
        double target = Math.Clamp(Math.Ceiling(contentHeight + chrome) + 2, MinPetHeight, MaxPetHeight);
        if (Math.Abs(Height - target) < 1) return;
        double curH = ActualHeight > 0 ? ActualHeight : Height;
        double bottom = Top + curH;
        Height = target;
        if (!App.Offscreen && bottom > 0)
        {
            var wa = SystemParameters.WorkArea;
            Top = Math.Clamp(bottom - target, wa.Top + 8, wa.Bottom - target - 8);
        }
        SavePlacement();
    }

    /// <summary>WebView2 안에서 마우스로 끌어 창을 옮긴다.</summary>
    public void StartDrag()
    {
        if (App.Offscreen) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        ReleaseCapture();
        SendMessage(hwnd, 0x00A1 /* WM_NCLBUTTONDOWN */, new IntPtr(2 /* HTCAPTION */), IntPtr.Zero);
        SavePlacement();
    }

    public void SavePlacement()
    {
        if (App.Offscreen || App.DemoMode || !IsLoaded || Left <= -10000) return;
        App.Settings.PetWindow = new AppSettings.WindowPlacement
        {
            Left = Left,
            Top = Top,
            Width = Width,
            Height = ActualHeight > 0 ? ActualHeight : Height,
        };
        try { App.Settings.Save(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static bool OnScreen(AppSettings.WindowPlacement w)
    {
        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                              SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        return screen.IntersectsWith(new Rect(w.Left + 40, w.Top + 20, Math.Max(1, DefaultWidth - 80), 40));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins { public int Left, Right, Top, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinRect { public int Left, Top, Right, Bottom; }

    private delegate IntPtr SubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, nuint uIdSubclass, IntPtr dwRefData);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out WinRect lpRect);

    [DllImport("user32.dll")]
    private static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);

    [DllImport("user32.dll")]
    private static extern int FillRect(IntPtr hDC, ref WinRect lprc, IntPtr hbr);

    [DllImport("gdi32.dll")]
    private static extern IntPtr GetStockObject(int fnObject);

    [DllImport("comctl32.dll")]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, nuint uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll")]
    private static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, nuint uIdSubclass);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hWnd, ref Margins pMarInset);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
}
