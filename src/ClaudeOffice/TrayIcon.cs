using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using ClaudeOffice.Monitoring;
using Forms = System.Windows.Forms;

namespace ClaudeOffice;

/// <summary>
/// 작업표시줄 오른쪽 알림 영역의 🧑‍💻 아이콘. 창을 숨겨도(✕·시작 시 숨기기·자동 실행) 프로그램이 돌고 있음을 보여 주고,
/// 누르면 창을 열고 오른쪽 클릭 메뉴로 빠른 이동·완전 종료를 지원한다.
/// 승인을 기다리는 직원이 있으면 아이콘 오른쪽 위에 주황색 알림 점을 표시한다.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Icon _normalIcon;
    private readonly Icon _waitingIcon;
    private readonly Forms.ToolStripItem _openItem, _reportItem, _exitItem;
    private readonly Forms.ToolStripMenuItem _waitItem, _petItem, _widgetItem;
    private string? _waitingSid;
    private int _waitingCount;
    private string? _waitingName;
    private bool _hintShown;

    public TrayIcon(Action open, Action<string> openSession, Action togglePet, Action toggleWidget, Action exit)
    {
        _normalIcon = LoadIcon();
        _waitingIcon = MakeWaitingIcon(_normalIcon);

        var menu = new Forms.ContextMenuStrip();
        _openItem = menu.Items.Add("", null, (_, _) => open());
        _openItem.Font = new Font(_openItem.Font, System.Drawing.FontStyle.Bold);   // 기본 동작
        _waitItem = new Forms.ToolStripMenuItem("", null, (_, _) =>
        {
            if (_waitingSid != null) openSession(_waitingSid);
            else open();
        }) { Visible = false };
        menu.Items.Add(_waitItem);
        _reportItem = menu.Items.Add("", null, (_, _) => openSession("@today"));
        _petItem = new Forms.ToolStripMenuItem("", null, (_, _) => togglePet());
        menu.Items.Add(_petItem);
        _widgetItem = new Forms.ToolStripMenuItem("", null, (_, _) => toggleWidget());
        menu.Items.Add(_widgetItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        _exitItem = menu.Items.Add("", null, (_, _) => exit());
        ApplyLanguage();

        _icon = new Forms.NotifyIcon
        {
            Icon = _normalIcon,
            Text = "Claude Office",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) open(); };
        _icon.BalloonTipClicked += (_, _) => (_onClick ?? open)();
    }

    /// <summary>메뉴 글을 지금 화면 언어로(언어를 바꾸면 다시 부른다).</summary>
    public void ApplyLanguage()
    {
        _openItem.Text = L.T("열기", "Open");
        _reportItem.Text = L.T("📊 오늘의 근무 일지", "📊 Today's work log");
        _petItem.Text = L.T("🪟 바탕화면 미니위젯", "🪟 Desktop MiniWidget");
        _widgetItem.Text = L.T("📋 작게 보기(목록)", "📋 Compact view (list)");
        _exitItem.Text = L.T("끝내기", "Exit");
        RefreshWaitingLabel();
    }

    public void SetPetChecked(bool on) => _petItem.Checked = on;

    public void SetWidgetChecked(bool on) => _widgetItem.Checked = on;

    /// <summary>승인·질문 대기 상태에 따라 트레이 아이콘의 주황색 점과 빠른 열기 메뉴를 갱신한다.</summary>
    public void SetWaiting(int count, string? firstId, string? firstName)
    {
        bool hadDot = _waitingCount > 0;
        bool hasDot = count > 0;
        _waitingCount = count;
        _waitingSid = firstId;
        _waitingName = firstName;
        if (hadDot != hasDot)
            _icon.Icon = hasDot ? _waitingIcon : _normalIcon;
        RefreshWaitingLabel();
    }

    private void RefreshWaitingLabel()
    {
        if (_waitingCount <= 0 || string.IsNullOrEmpty(_waitingSid))
        {
            _waitItem.Visible = false;
            return;
        }
        var label = string.IsNullOrWhiteSpace(_waitingName) ? L.T("직원", "Employee") : _waitingName;
        _waitItem.Text = _waitingCount > 1
            ? L.T($"✋ {label} 외 {_waitingCount - 1}명 확인 대기", $"✋ {label} +{_waitingCount - 1} waiting")
            : L.T($"✋ {label} 확인 대기 열기", $"✋ Open {label} (waiting)");
        _waitItem.Visible = true;
    }

    /// <summary>마우스를 올렸을 때 보일 글(창 제목과 같게 — "(✋2) 우리 팀 사무실 v1.0.6"). 63자 제한.</summary>
    public void SetText(string text) => _icon.Text = text.Length <= 63 ? text : text[..62] + "…";

    /// <summary>처음 숨길 때 한 번만: 꺼진 게 아니라 여기서 돌고 있다고 알려 준다.</summary>
    public void HintOnce()
    {
        if (_hintShown) return;
        _hintShown = true;
        _onClick = null;
        _icon.ShowBalloonTip(4000, "Claude Office", L.T("알림 영역에서 계속 실행 중입니다(휴대폰 연결도 계속). 끝내려면 🧑‍💻 아이콘 오른쪽 클릭 → 끝내기",
                                                        "Still running in the notification area (phone connection too). To quit, right-click the 🧑‍💻 icon → Exit"), Forms.ToolTipIcon.Info);
    }

    private Action? _onClick;

    /// <summary>
    /// 알림(Windows 10·11 에서는 알림 센터 알림으로 보인다). 알림을 누르면 onClick.
    /// silent 면 소리 없이 — WinForms 가 소리 끄기(NIIF_NOSOUND)를 내주지 않아 아이콘의 창 핸들·ID 로 Shell_NotifyIcon 을 직접 부른다.
    /// 그 둘을 찾지 못하면(WinForms 내부가 바뀐 경우) 소리 있는 보통 알림으로.
    /// </summary>
    public void Notify(string title, string text, bool silent, Action? onClick)
    {
        _onClick = onClick;
        title = title.Length <= 63 ? title : title[..62] + "…";
        text = text.Length <= 255 ? text : text[..254] + "…";
        if (silent && ShowSilent(title, text)) return;
        _icon.ShowBalloonTip(6000, title, text, Forms.ToolTipIcon.Info);
    }

    private bool ShowSilent(string title, string text)
    {
        const BindingFlags Fields = BindingFlags.NonPublic | BindingFlags.Instance;
        var fields = typeof(Forms.NotifyIcon).GetFields(Fields);
        var window = fields.FirstOrDefault(f => typeof(Forms.NativeWindow).IsAssignableFrom(f.FieldType))?.GetValue(_icon) as Forms.NativeWindow;
        var id = fields.FirstOrDefault(f => f.Name.TrimStart('_').Equals("id", StringComparison.OrdinalIgnoreCase))?.GetValue(_icon);   // .NET 8: uint _id
        uint? uid = id switch { uint u => u, int i => (uint)i, _ => null };
        if (window is null || window.Handle == IntPtr.Zero || uid is null) return false;

        var data = new NotifyIconData
        {
            cbSize = Marshal.SizeOf<NotifyIconData>(),
            hWnd = window.Handle,
            uID = uid.Value,
            uFlags = NIF_INFO,
            szTip = "",
            szInfo = text,
            uTimeoutOrVersion = 6000,
            szInfoTitle = title,
            dwInfoFlags = NIIF_INFO | NIIF_NOSOUND,
        };
        return Shell_NotifyIconW(NIM_MODIFY, ref data);
    }

    private const uint NIM_MODIFY = 1, NIF_INFO = 0x10, NIIF_INFO = 1, NIIF_NOSOUND = 0x10;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIconW(uint message, ref NotifyIconData data);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public void Dispose()
    {
        _icon.Visible = false;   // 끝난 뒤에도 아이콘이 남아 보이지 않게
        _icon.Dispose();
        _normalIcon.Dispose();
        _waitingIcon.Dispose();
    }

    private static Icon LoadIcon()
    {
        var res = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/office.ico"));
        if (res is null) return (Icon)SystemIcons.Application.Clone();
        using var s = res.Stream;
        return new Icon(s, Forms.SystemInformation.SmallIconSize);   // 화면 배율에 맞는 작은 아이콘
    }

    /// <summary>기본 아이콘 우측 상단에 주황색 알림 점(#ffb347)을 그린 아이콘을 만든다.</summary>
    private static Icon MakeWaitingIcon(Icon baseIcon)
    {
        try
        {
            using var bmp = baseIcon.ToBitmap();
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float d = Math.Max(6f, bmp.Width * 0.38f);
                float x = bmp.Width - d - 0.5f;
                float y = 0.5f;
                using var border = new SolidBrush(Color.FromArgb(240, 24, 20, 38));
                using var fill = new SolidBrush(Color.FromArgb(255, 255, 179, 71));
                g.FillEllipse(border, x - 1f, y - 1f, d + 2f, d + 2f);
                g.FillEllipse(fill, x, y, d, d);
            }
            var hIcon = bmp.GetHicon();
            try
            {
                using var tmp = Icon.FromHandle(hIcon);
                return (Icon)tmp.Clone();
            }
            finally
            {
                DestroyIcon(hIcon);
            }
        }
        catch
        {
            return (Icon)baseIcon.Clone();
        }
    }
}
