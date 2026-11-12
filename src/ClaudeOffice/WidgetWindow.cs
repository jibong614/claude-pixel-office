// 하위 호환 및 편의용 별칭
global using MiniWidgetWindow = ClaudeOffice.WidgetWindow;

using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using ClaudeOffice.Monitoring;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Forms = System.Windows.Forms;

namespace ClaudeOffice;

/// <summary>
/// 바탕화면 미니위젯(MiniWidget) 창: 대표 세션의 직원이 바탕화면 오른쪽 아래 구석 책상에 앉아 일하는 모습을 보여 주고,
/// 바로 지시를 던지거나 승인·거절·대표 세션 전환을 할 수 있는 작은 테두리 없는 항상-위 창.
/// 본창(MainWindow)을 ✕ 로 숨겨 두어도 바탕화면에 그대로 남아 있다.
/// </summary>
internal sealed class WidgetWindow : Window
{
    public const double WidthLarge = 360;
    public const double WidthMedium = 300;
    public const double WidthSmall = 260;

    public const double GraphicLarge = 230;
    public const double GraphicMedium = 190;
    public const double GraphicSmall = 160;

    public const double DefaultLarge = 510;
    public const double DefaultMedium = 450;
    public const double DefaultSmall = 400;

    public const double DefaultOfficeLarge = 280;
    public const double DefaultOfficeMedium = 260;
    public const double DefaultOfficeSmall = 240;

    public const double DefaultWidth = WidthLarge;
    public const double DefaultHeight = DefaultLarge;
    public const double DefaultOfficeHeight = DefaultOfficeLarge;
    public const double GraphicHeight = GraphicLarge;
    public const double MinWidgetHeight = 180;
    public const double MinBasicHeight = 380;
    public const double MinOfficeHeight = 180;
    public const double MaxWidgetHeight = 840;
    public const double MiniBarWidth = 540;
    public const double MiniBarHeight = 44;
    public const double WalkWidthLarge = 580;
    public const double WalkWidthMedium = 500;
    public const double WalkWidthSmall = 420;
    public const double WalkHeightLarge = 135;
    public const double WalkHeightMedium = 120;
    public const double WalkHeightSmall = 105;
    private const double MinPetHeight = 40, MaxPetHeight = 840;

    private readonly WebView2 _web = new();
    private readonly string _virtualHost;
    private readonly string _webRoot;
    private readonly Action<CoreWebView2WebMessageReceivedEventArgs> _onMessage;
    private readonly SubclassProc _webSubclass;
    private bool _ready;
    private bool _allowClose;
    private bool _subclassed;
    private double _dragStartTop;
    private double _dragStartHeight;
    private string _currentAppMode = App.Settings.AppMode ?? "basic";
    private string _currentWidgetSize = App.Settings.WidgetSize ?? "large";
    private Forms.ContextMenuStrip? _modeMenu;
    private Forms.ToolStripMenuItem? _itemNormal;
    private Forms.ToolStripMenuItem? _itemIsland;
    private Forms.ToolStripMenuItem? _itemWalk;
    private Action? _onNormal;
    private Action? _onIsland;
    private Action? _onWalk;

    public WidgetWindow(string virtualHost, string webRoot, Action<CoreWebView2WebMessageReceivedEventArgs> onMessage, Action onUserClose)
    {
        _virtualHost = virtualHost;
        _webRoot = webRoot;
        _onMessage = onMessage;
        _webSubclass = OnWebChildWndProc;

        Title = "Claude Office — MiniWidget";
        Width = App.Settings.PetIsland ? MiniBarWidth
              : App.Settings.PetWalk ? GetWalkWidthForSize()
              : GetWidthForSize();
        Height = App.Settings.PetIsland ? MiniBarHeight
              : App.Settings.PetWalk ? GetWalkHeightForSize()
              : GetSavedWidgetHeight();
        MinWidth = 240;
        MaxWidth = 900;
        MinHeight = MinPetHeight;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Topmost = App.Settings.PetTopmost;
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
                if (App.Settings.PetTopmost) Topmost = true;
            }
        };
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

            // WS_EX_TOOLWINDOW: Alt+Tab 제외 및 Shell 전체 최소화(Win+D) 대상에서 분리
            try
            {
                var exStyle = (long)GetWindowLongPtr(hwnd, -20 /* GWL_EXSTYLE */);
                SetWindowLongPtr(hwnd, -20, new IntPtr(exStyle | 0x00000080 /* WS_EX_TOOLWINDOW */));
            }
            catch { }
            ApplyBackdrop(App.Settings.PetNoBg);
        };
        Loaded += OnLoaded;
        Closing += (_, e) =>
        {
            if (_modeMenu != null && !_modeMenu.IsDisposed)
            {
                _modeMenu.Close();
                _modeMenu.Dispose();
                _modeMenu = null;
            }
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

        // Windows 11 외곽 회색 테두리 선 완전 제거:
        // HTML/CSS가 자체적으로 둥근 모서리와 카드 테두리를 렌더링하므로, OS 창 테두리(DWMWA_BORDER_COLOR)를 완전 비활성화(DWMWA_COLOR_NONE)한다.
        int ncrp = 1 /* DWMNCRP_DISABLED */;
        DwmSetWindowAttribute(hwnd, 2 /* DWMWA_NCRENDERING_POLICY */, ref ncrp, sizeof(int));

        int corner = 1 /* DWMWCP_DONOTROUND */;
        DwmSetWindowAttribute(hwnd, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref corner, sizeof(int));

        int border = unchecked((int)0xFFFFFFFE) /* DWMWA_COLOR_NONE */;
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
        if (_modeMenu != null && !_modeMenu.IsDisposed)
        {
            _modeMenu.Close();
            _modeMenu.Dispose();
            _modeMenu = null;
        }
        SavePlacement();
        Close();
    }

    private string PageUrl() => $"https://{_virtualHost}/index.html?widget_window=1&pet=1"
        + (App.Settings.PetIsland ? "&island=1" : "")
        + (App.Settings.PetWalk ? "&walk=1" : "")
        + (L.En ? "&lang=en" : "");

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _web.DefaultBackgroundColor = System.Drawing.Color.Transparent;
            if (!_subclassed && _web.Handle != IntPtr.Zero)
            {
                _subclassed = SetWindowSubclass(_web.Handle, _webSubclass, 1, IntPtr.Zero);
            }
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
        if (msg == 0x0112 /* WM_SYSCOMMAND */)
        {
            int sc = (int)wParam & 0xFFF0;
            if (sc == 0xF020 /* SC_MINIMIZE */)
            {
                // Win + D 나 시스템 전체 최소화 명령 무시 (바탕화면 위젯이므로 숨거나 최소화되지 않음)
                handled = true;
                return IntPtr.Zero;
            }
        }
        if (msg == 0x0046 /* WM_WINDOWPOSCHANGING */ && lParam != IntPtr.Zero)
        {
            try
            {
                var wp = Marshal.PtrToStructure<WindowPosStruct>(lParam);
                if ((wp.flags & 0x0080 /* SWP_HIDEWINDOW */) != 0)
                {
                    wp.flags &= ~0x0080u;
                    Marshal.StructureToPtr(wp, lParam, false);
                }
            }
            catch { }
        }
        if (msg == 0x0018 /* WM_SHOWWINDOW */)
        {
            // Win + D 로 인해 Shell이 창을 강제 숨김 처리하려는 경우(wParam == 0, lParam != 0) 방어
            if (wParam == IntPtr.Zero && lParam != IntPtr.Zero)
            {
                handled = true;
                return IntPtr.Zero;
            }
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

    public static double GetWidthForSize(string? size = null)
    {
        size ??= App.Settings.WidgetSize;
        return size switch
        {
            "small" => WidthSmall,
            "medium" => WidthMedium,
            _ => WidthLarge
        };
    }

    public static double GetGraphicHeightForSize(string? size = null)
    {
        size ??= App.Settings.WidgetSize;
        return size switch
        {
            "small" => GraphicSmall,
            "medium" => GraphicMedium,
            _ => GraphicLarge
        };
    }

    /// <summary>산책 모드(시메지) 전용 가상 와이드 너비: 넓은 공간을 자유롭게 활보할 수 있도록 확장</summary>
    public static double GetWalkWidthForSize(string? size = null)
    {
        size ??= App.Settings.WidgetSize;
        return size switch
        {
            "small" => WalkWidthSmall,
            "medium" => WalkWidthMedium,
            _ => WalkWidthLarge
        };
    }

    /// <summary>산책 모드(시메지) 전용 높이: 캐릭터 캔버스 및 상단 툴바 여백을 포함한 컴팩트 높이</summary>
    public static double GetWalkHeightForSize(string? size = null)
    {
        size ??= App.Settings.WidgetSize;
        return size switch
        {
            "small" => WalkHeightSmall,
            "medium" => WalkHeightMedium,
            _ => WalkHeightLarge
        };
    }

    public static double GetDefaultHeightForMode(string? mode = null, string? size = null)
    {
        mode ??= App.Settings.AppMode;
        size ??= App.Settings.WidgetSize;
        if (mode == "office")
        {
            return size switch
            {
                "small" => DefaultOfficeSmall,
                "medium" => DefaultOfficeMedium,
                _ => DefaultOfficeLarge
            };
        }
        return size switch
        {
            "small" => DefaultSmall,
            "medium" => DefaultMedium,
            _ => DefaultLarge
        };
    }

    public static double GetMinHeightForCurrent(string? mode = null, string? size = null)
    {
        mode ??= App.Settings.AppMode;
        size ??= App.Settings.WidgetSize;
        if (mode == "office") return MinOfficeHeight;
        return size switch
        {
            "small" => 310,
            "medium" => 340,
            _ => MinBasicHeight
        };
    }

    public static double GetSavedWidgetHeight(string? mode = null, string? size = null)
    {
        var h = App.Settings.MiniWidgetPlacement?.Height;
        if (h >= MinWidgetHeight && h <= MaxWidgetHeight) return h.Value;
        return GetDefaultHeightForMode(mode, size);
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
        double wWidth = GetWidthForSize(_currentWidgetSize);
        if (App.Settings.PetIsland)
        {
            Width = MiniBarWidth;
            Height = MiniBarHeight;
            if (App.Settings.MiniBarPlacement is { } bar && OnScreen(bar, MiniBarWidth, MiniBarHeight))
            {
                Left = bar.Left;
                Top = bar.Top;
            }
            else if ((App.Settings.MiniWidgetPlacement ?? App.Settings.MiniWidgetWindow) is { } w && OnScreen(w, wWidth, DefaultHeight))
            {
                double wH = (w.Height >= MinWidgetHeight && w.Height <= MaxWidgetHeight) ? w.Height : GetDefaultHeightForMode();
                double curBottom = w.Top + wH;
                double curRight = w.Left + wWidth;
                Left = Math.Clamp(curRight - MiniBarWidth, wa.Left + 8, wa.Right - MiniBarWidth - 8);
                Top = Math.Clamp(curBottom - MiniBarHeight, wa.Top + 8, wa.Bottom - MiniBarHeight - 8);
            }
            else
            {
                Left = wa.Right - MiniBarWidth - 20;
                Top = wa.Bottom - MiniBarHeight - 20;
            }
        }
        else if (App.Settings.PetWalk)
        {
            double walkW = GetWalkWidthForSize(_currentWidgetSize);
            double walkH = GetWalkHeightForSize(_currentWidgetSize);
            Width = walkW;
            Height = walkH;
            if ((App.Settings.MiniWidgetPlacement ?? App.Settings.MiniWidgetWindow) is { } w && OnScreen(w, wWidth, walkH))
            {
                double savedH = (w.Height >= MinWidgetHeight && w.Height <= MaxWidgetHeight) ? w.Height : GetDefaultHeightForMode();
                double curBottom = w.Top + savedH;
                double curRight = w.Left + wWidth;
                Left = Math.Clamp(curRight - walkW, wa.Left + 8, wa.Right - walkW - 8);
                Top = Math.Clamp(curBottom - walkH, wa.Top + 8, wa.Bottom - walkH - 8);
            }
            else
            {
                Left = wa.Right - walkW - 20;
                Top = wa.Bottom - walkH - 20;
            }
        }
        else
        {
            Width = wWidth;
            Height = GetSavedWidgetHeight();
            if ((App.Settings.MiniWidgetPlacement ?? App.Settings.MiniWidgetWindow) is { } w && OnScreen(w, wWidth, Height))
            {
                Left = w.Left;
                Top = w.Top;
            }
            else
            {
                Left = wa.Right - Width - 20;
                Top = wa.Bottom - Height - 20;
            }
        }
    }

    /// <summary>미니위젯 내용 높이에 창 높이를 맞추되, 아래쪽 모서리를 고정해 바탕화면 오른쪽 아래 자리에서 위로만 늘어나고 줄어들게 한다.</summary>
    public void FitHeight(double contentHeight)
    {
        if (WindowState != WindowState.Normal) return;
        if (App.Settings.PetIsland) return; // 미니바는 높이 고정(44px)
        double curH = ActualHeight > 0 ? ActualHeight : Height;
        double bottom = Top + curH;
        if (App.Settings.PetWalk)
        {
            double targetH = GetWalkHeightForSize(_currentWidgetSize);
            if (Math.Abs(Height - targetH) < 1) return;
            Height = targetH;
            if (!App.Offscreen && bottom > 0)
            {
                var wa = SystemParameters.WorkArea;
                Top = Math.Clamp(bottom - targetH, wa.Top + 8, wa.Bottom - targetH - 8);
            }
            return;
        }
        double chrome = ActualHeight > 0 && _web.ActualHeight > 0 ? Math.Max(0, ActualHeight - _web.ActualHeight) : 0;
        double target = Math.Clamp(Math.Ceiling(contentHeight + chrome), MinWidgetHeight, MaxWidgetHeight);
        if (Math.Abs(Height - target) < 1) return;
        Height = target;
        if (!App.Offscreen && bottom > 0)
        {
            var wa = SystemParameters.WorkArea;
            Top = Math.Clamp(bottom - target, wa.Top + 8, wa.Bottom - target - 8);
        }
        SaveWidgetPlacement();
    }

    /// <summary>미니위젯 내용 너비에 창 너비를 맞춘다.</summary>
    public void FitWidth(double contentWidth)
    {
        if (WindowState != WindowState.Normal) return;
        if (App.Settings.PetIsland)
        {
            Width = MiniBarWidth;
            return;
        }
        if (App.Settings.PetWalk)
        {
            double targetW = GetWalkWidthForSize(_currentWidgetSize);
            double curRight = Left + Width;
            Width = targetW;
            Left = curRight - targetW;
            return;
        }
        Width = DefaultWidth;
    }

    public void SetTopmost(bool on)
    {
        App.Settings.PetTopmost = on;
        Topmost = on;
        SavePlacement();
    }

    public void OnResizeStart(string edge)
    {
        if (App.Settings.PetIsland || App.Settings.PetWalk) return; // 미니바 및 산책모드는 리사이즈 불가 (높이 고정)
        _dragStartTop = Top;
        _dragStartHeight = ActualHeight > 0 ? ActualHeight : Height;
    }

    public void OnResizeMove(string edge, double deltaY)
    {
        if (WindowState != WindowState.Normal) return;
        if (App.Settings.PetIsland || App.Settings.PetWalk) return;
        var wa = SystemParameters.WorkArea;
        double maxH = (wa.Height > 0) ? Math.Min(MaxWidgetHeight, wa.Height - 40) : MaxWidgetHeight;
        double minH = GetMinHeightForCurrent(_currentAppMode, _currentWidgetSize);

        if (edge == "bottom")
        {
            double targetH = Math.Clamp(_dragStartHeight + deltaY, minH, maxH);
            if (!App.Offscreen && wa.Bottom > 0 && Top + targetH > wa.Bottom)
                targetH = Math.Max(minH, wa.Bottom - Top);
            Height = targetH;
        }
        else if (edge == "top")
        {
            double fixedBottom = _dragStartTop + _dragStartHeight;
            double targetH = Math.Clamp(_dragStartHeight - deltaY, minH, maxH);
            double targetTop = fixedBottom - targetH;
            if (!App.Offscreen && wa.Top >= 0 && targetTop < wa.Top)
            {
                targetTop = wa.Top;
                targetH = Math.Max(minH, fixedBottom - targetTop);
            }
            Top = targetTop;
            Height = targetH;
        }
    }

    public void OnResizeEnd(string edge, double deltaY)
    {
        if (App.Settings.PetIsland || App.Settings.PetWalk) return;
        OnResizeMove(edge, deltaY);
        SaveWidgetPlacement();
    }

    /// <summary>
    /// 💼 오피스 모드 ↔ 기본 모드 전환 시 위젯 창 높이 조정:
    /// - 기본 모드로 전환: 그래픽 요소 영역(+GraphicHeight)을 더해 도트 그래픽이 본래 크기로 출력되게 하고, 바탕화면 하단 모서리를 고정
    /// - 오피스 모드로 전환: 그래픽 요소 영역(-GraphicHeight)을 빼서 텍스트 영역 비율을 유지한 채 컴팩트하게 정돈
    /// - 미니바(초슬림 아일랜드) 상태에서는 위젯 저장 배치 정보만 갱신
    /// </summary>
    public void SetAppMode(string newMode, bool initial = false)
    {
        if (newMode != "basic" && newMode != "office") newMode = "basic";
        if (initial)
        {
            _currentAppMode = newMode;
            return;
        }
        if (_currentAppMode == newMode) return;

        string prevMode = _currentAppMode;
        _currentAppMode = newMode;

        double currentGraphicH = GetGraphicHeightForSize(_currentWidgetSize);

        if (App.Settings.PetIsland)
        {
            if (App.Settings.MiniWidgetPlacement is { } p)
            {
                double delta = (newMode == "basic") ? currentGraphicH : -currentGraphicH;
                double savedH = (p.Height >= MinWidgetHeight && p.Height <= MaxWidgetHeight)
                    ? p.Height
                    : (newMode == "basic" ? GetDefaultHeightForMode("office", _currentWidgetSize) : GetDefaultHeightForMode("basic", _currentWidgetSize));
                double newSavedH = Math.Clamp(savedH + delta, MinWidgetHeight, MaxWidgetHeight);
                p.Top = p.Top - (newSavedH - savedH);
                p.Height = newSavedH;
                try { App.Settings.Save(); } catch { }
            }
            return;
        }

        double hDelta = (newMode == "basic") ? currentGraphicH : -currentGraphicH;
        double curH = ActualHeight > 0 ? ActualHeight : Height;
        var wa = SystemParameters.WorkArea;
        double maxH = (wa.Height > 0) ? Math.Min(MaxWidgetHeight, wa.Height - 40) : MaxWidgetHeight;
        double minH = GetMinHeightForCurrent(newMode, _currentWidgetSize);
        double targetH = Math.Clamp(curH + hDelta, minH, maxH);

        double fixedBottom = Top + curH;
        double targetTop = fixedBottom - targetH;
        if (!App.Offscreen && wa.Height > 0 && wa.Width > 0)
        {
            if (targetTop < wa.Top)
            {
                targetTop = wa.Top;
                targetH = Math.Max(minH, fixedBottom - targetTop);
            }
            targetTop = Math.Clamp(targetTop, wa.Top + 8, wa.Bottom - targetH - 8);
        }

        Top = targetTop;
        Height = targetH;
        SaveWidgetPlacement();
    }

    /// <summary>
    /// 📐 위젯 크기 옵션 변경 (large: 크게 360px · 기본 / medium: 보통 300px / small: 작게 260px)
    /// - 픽셀 그래픽 규모와 창 너비가 함께 비례 조정되며 우측/하단 모서리를 고정
    /// </summary>
    public void SetWidgetSize(string newSize)
    {
        if (newSize != "large" && newSize != "medium" && newSize != "small") newSize = "large";
        string prevSize = _currentWidgetSize;
        _currentWidgetSize = newSize;
        App.Settings.WidgetSize = newSize;

        double targetW = GetWidthForSize(newSize);
        double prevGraphicH = GetGraphicHeightForSize(prevSize);
        double newGraphicH = GetGraphicHeightForSize(newSize);
        double graphicDelta = newGraphicH - prevGraphicH;

        if (App.Settings.PetWalk)
        {
            double targetWalkW = GetWalkWidthForSize(newSize);
            double targetWalkH = GetWalkHeightForSize(newSize);
            double curH2 = ActualHeight > 0 ? ActualHeight : Height;
            double curW2 = ActualWidth > 0 ? ActualWidth : Width;
            double fixedB = Top + curH2;
            double fixedR = Left + curW2;
            double targetT = fixedB - targetWalkH;
            double targetL = fixedR - targetWalkW;
            var waWalk = SystemParameters.WorkArea;
            if (!App.Offscreen && waWalk.Height > 0 && waWalk.Width > 0)
            {
                targetT = Math.Clamp(targetT, waWalk.Top + 8, waWalk.Bottom - targetWalkH - 8);
                targetL = Math.Clamp(targetL, waWalk.Left + 8, waWalk.Right - targetWalkW - 8);
            }
            Top = targetT;
            Left = targetL;
            Width = targetWalkW;
            Height = targetWalkH;
            return;
        }

        if (App.Settings.PetIsland)
        {
            if (App.Settings.MiniWidgetPlacement is { } p)
            {
                double savedH = (p.Height >= MinWidgetHeight && p.Height <= MaxWidgetHeight)
                    ? p.Height
                    : GetDefaultHeightForMode(App.Settings.AppMode, prevSize);
                double newSavedH = (_currentAppMode == "basic")
                    ? Math.Clamp(savedH + graphicDelta, MinWidgetHeight, MaxWidgetHeight)
                    : savedH;
                double hDiff = newSavedH - savedH;
                p.Top = p.Top - hDiff;
                p.Left = p.Left + (p.Width - targetW);
                p.Width = targetW;
                p.Height = newSavedH;
                try { App.Settings.Save(); } catch { }
            }
            return;
        }

        double curH = ActualHeight > 0 ? ActualHeight : Height;
        double curW = ActualWidth > 0 ? ActualWidth : Width;
        var wa = SystemParameters.WorkArea;
        double maxH = (wa.Height > 0) ? Math.Min(MaxWidgetHeight, wa.Height - 40) : MaxWidgetHeight;
        double minH = GetMinHeightForCurrent(_currentAppMode, newSize);

        double targetH = (_currentAppMode == "basic")
            ? Math.Clamp(curH + graphicDelta, minH, maxH)
            : Math.Clamp(curH, MinOfficeHeight, maxH);

        double fixedBottom = Top + curH;
        double fixedRight = Left + curW;

        double targetTop = fixedBottom - targetH;
        double targetLeft = fixedRight - targetW;

        if (!App.Offscreen && wa.Height > 0 && wa.Width > 0)
        {
            if (targetTop < wa.Top)
            {
                targetTop = wa.Top;
                targetH = Math.Max(minH, fixedBottom - targetTop);
            }
            targetTop = Math.Clamp(targetTop, wa.Top + 8, wa.Bottom - targetH - 8);
            targetLeft = Math.Clamp(targetLeft, wa.Left + 8, wa.Right - targetW - 8);
        }

        Top = targetTop;
        Left = targetLeft;
        Width = targetW;
        Height = targetH;

        SaveWidgetPlacement();
    }

    public void RestoreSavedHeight()
    {
        if (App.Settings.PetIsland) return;
        Width = GetWidthForSize(_currentWidgetSize);
        Height = GetSavedWidgetHeight();
    }

    public void SetPetMode(bool island, bool walk = false)
    {
        var wa = SystemParameters.WorkArea;
        double curBottom = Top + (ActualHeight > 0 ? ActualHeight : Height);
        double curRight = Left + (ActualWidth > 0 ? ActualWidth : Width);

        if (island)
        {
            // 위젯/산책 -> 미니바: 하단(Bottom) 및 우측(Right) 기준 정렬
            double targetW = MiniBarWidth;
            double targetH = MiniBarHeight;
            double newTop = curBottom - targetH;
            double newLeft = curRight - targetW;

            if (!App.Offscreen && wa.Width > 0 && wa.Height > 0)
            {
                newTop = Math.Clamp(newTop, wa.Top + 8, wa.Bottom - targetH - 8);
                newLeft = Math.Clamp(newLeft, wa.Left + 8, wa.Right - targetW - 8);
            }

            Width = targetW;
            Height = targetH;
            Top = newTop;
            Left = newLeft;

            SaveMiniBarPlacement();
        }
        else if (walk)
        {
            // 미니바/위젯 -> 산책 모드: 하단(Bottom) 및 우측(Right) 기준 정렬, 캐릭터 캔버스 높이 및 산책 전용 가상 와이드 너비 적용
            double targetW = GetWalkWidthForSize(_currentWidgetSize);
            double targetH = GetWalkHeightForSize(_currentWidgetSize);
            double newTop = curBottom - targetH;
            double newLeft = curRight - targetW;

            if (!App.Offscreen && wa.Width > 0 && wa.Height > 0)
            {
                newTop = Math.Clamp(newTop, wa.Top + 8, wa.Bottom - targetH - 8);
                newLeft = Math.Clamp(newLeft, wa.Left + 8, wa.Right - targetW - 8);
            }

            Width = targetW;
            Height = targetH;
            Top = newTop;
            Left = newLeft;

            ApplyBackdrop(true); // 산책 모드는 바탕화면 위를 자유롭게 걸어야 하므로 항상 완전 투명
        }
        else
        {
            // 미니바/산책 -> 위젯: 하단(Bottom) 및 우측(Right) 기준 정렬, 위젯 커스텀 높이 복원
            double targetW = GetWidthForSize(_currentWidgetSize);
            double targetH = GetSavedWidgetHeight();
            double newTop = curBottom - targetH;
            double newLeft = curRight - targetW;

            if (!App.Offscreen && wa.Width > 0 && wa.Height > 0)
            {
                newTop = Math.Clamp(newTop, wa.Top + 8, wa.Bottom - targetH - 8);
                newLeft = Math.Clamp(newLeft, wa.Left + 8, wa.Right - targetW - 8);
            }

            Width = targetW;
            Height = targetH;
            Top = newTop;
            Left = newLeft;

            SaveWidgetPlacement();
            ApplyBackdrop(App.Settings.PetNoBg);
        }
    }

    private void EnsureModeMenu(Action onNormal, Action onIsland, Action onWalk)
    {
        _onNormal = onNormal;
        _onIsland = onIsland;
        _onWalk = onWalk;

        if (_modeMenu != null && !_modeMenu.IsDisposed) return;

        var menu = new Forms.ContextMenuStrip();
        if (System.Drawing.SystemFonts.MenuFont is { } mf)
            menu.Font = mf;

        _itemNormal = new Forms.ToolStripMenuItem(
            L.T("🪟 바탕화면 미니위젯", "🪟 Desktop MiniWidget"),
            null,
            (_, _) => { if (_onNormal != null) Dispatcher.InvokeAsync(_onNormal); });

        _itemIsland = new Forms.ToolStripMenuItem(
            L.T("🏝️ 미니바 (초슬림)", "🏝️ Mini-bar (Ultra-slim)"),
            null,
            (_, _) => { if (_onIsland != null) Dispatcher.InvokeAsync(_onIsland); });

        _itemWalk = new Forms.ToolStripMenuItem(
            L.T("🐾 산책 모드", "🐾 Walk Mode"),
            null,
            (_, _) => { if (_onWalk != null) Dispatcher.InvokeAsync(_onWalk); });

        menu.Items.Add(_itemNormal);
        menu.Items.Add(_itemIsland);
        menu.Items.Add(_itemWalk);

        _modeMenu = menu;
    }

    /// <summary>
    /// 화면 모드 변경(미니위젯 · 미니바 · 산책 모드) 컨텍스트 메뉴를 띄운다.
    /// 미니바(높이 44px)나 화면 하단 근처에서는 메뉴가 잘리지 않도록 위쪽 방향(AboveLeft)으로 띄운다.
    /// </summary>
    public void ShowPetModeMenu(
        double clientLeft,
        double clientTop,
        double clientRight,
        double clientBottom,
        bool isIsland,
        Action onNormal,
        Action onIsland,
        Action onWalk)
    {
        if (_web.Handle == IntPtr.Zero || !IsVisible) return;

        if (_modeMenu != null && !_modeMenu.IsDisposed && _modeMenu.Visible)
        {
            _modeMenu.Close();
            return;
        }

        EnsureModeMenu(onNormal, onIsland, onWalk);
        if (_modeMenu == null || _modeMenu.IsDisposed) return;

        if (_itemNormal != null)
        {
            _itemNormal.Text = L.T("🪟 바탕화면 미니위젯", "🪟 Desktop MiniWidget");
            _itemNormal.Checked = !App.Settings.PetIsland && !App.Settings.PetWalk;
        }
        if (_itemIsland != null)
        {
            _itemIsland.Text = L.T("🏝️ 미니바 (초슬림)", "🏝️ Mini-bar (Ultra-slim)");
            _itemIsland.Checked = App.Settings.PetIsland;
        }
        if (_itemWalk != null)
        {
            _itemWalk.Text = L.T("🐾 산책 모드", "🐾 Walk Mode");
            _itemWalk.Checked = App.Settings.PetWalk;
            _itemWalk.Enabled = App.Settings.AppMode != "office";
            _itemWalk.ToolTipText = _itemWalk.Enabled ? "" : L.T("오피스 모드에서는 비활성화됨", "Disabled in office mode");
        }

        System.Windows.Point screenBottomRight;
        System.Windows.Point screenTopLeft;
        try
        {
            screenBottomRight = _web.PointToScreen(new System.Windows.Point(clientRight, clientBottom));
            screenTopLeft = _web.PointToScreen(new System.Windows.Point(clientLeft, clientTop));
        }
        catch
        {
            screenBottomRight = new System.Windows.Point(Left + clientRight, Top + clientBottom);
            screenTopLeft = new System.Windows.Point(Left + clientLeft, Top + clientTop);
        }

        var ptBottomRight = new System.Drawing.Point((int)Math.Round(screenBottomRight.X), (int)Math.Round(screenBottomRight.Y));
        var ptTopRight = new System.Drawing.Point((int)Math.Round(screenBottomRight.X), (int)Math.Round(screenTopLeft.Y));

        var screen = Forms.Screen.FromPoint(ptTopRight);
        var wa = screen.WorkingArea;

        // 미니바(island) 모드이거나 화면 하단 근처에 있으면 위쪽 방향으로 열기
        bool openUp = isIsland || (ptBottomRight.Y + 120 > wa.Bottom);

        if (openUp)
        {
            _modeMenu.Show(ptTopRight, Forms.ToolStripDropDownDirection.AboveLeft);
        }
        else
        {
            _modeMenu.Show(ptBottomRight, Forms.ToolStripDropDownDirection.BelowLeft);
        }
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
        if (App.Settings.PetIsland)
            SaveMiniBarPlacement();
        else
            SaveWidgetPlacement();
    }

    public void SaveWidgetPlacement()
    {
        if (App.Offscreen || App.DemoMode || !IsLoaded || Left <= -10000) return;
        if (App.Settings.PetWalk)
        {
            // 산책 모드일 때는 창 위치(Left, Top)만 저장하고 일반 위젯 고유의 높이는 유지
            App.Settings.MiniWidgetPlacement = new AppSettings.WindowPlacement
            {
                Left = Left,
                Top = Top,
                Width = GetWidthForSize(_currentWidgetSize),
                Height = GetSavedWidgetHeight(),
            };
            try { App.Settings.Save(); } catch { }
            return;
        }
        double h = ActualHeight > 0 ? ActualHeight : Height;
        double savedH = (h >= MinWidgetHeight && h <= MaxWidgetHeight) ? h : GetSavedWidgetHeight();
        App.Settings.MiniWidgetPlacement = new AppSettings.WindowPlacement
        {
            Left = Left,
            Top = Top,
            Width = GetWidthForSize(_currentWidgetSize),
            Height = savedH,
        };
        try { App.Settings.Save(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public void SaveMiniBarPlacement()
    {
        if (App.Offscreen || App.DemoMode || !IsLoaded || Left <= -10000) return;
        App.Settings.MiniBarPlacement = new AppSettings.WindowPlacement
        {
            Left = Left,
            Top = Top,
            Width = MiniBarWidth,
            Height = MiniBarHeight,
        };
        try { App.Settings.Save(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static bool OnScreen(AppSettings.WindowPlacement w, double width, double height)
    {
        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                              SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        return screen.IntersectsWith(new Rect(w.Left + 20, w.Top + 10, Math.Max(1, width - 40), Math.Max(1, height - 20)));
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

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPosStruct
    {
        public IntPtr hwnd;
        public IntPtr hwndInsertAfter;
        public int x;
        public int y;
        public int cx;
        public int cy;
        public uint flags;
    }
}
