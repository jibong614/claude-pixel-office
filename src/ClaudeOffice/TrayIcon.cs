using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using ClaudeOffice.Monitoring;
using Forms = System.Windows.Forms;

namespace ClaudeOffice;

/// <summary>
/// 작업표시줄 오른쪽 알림 영역의 🏢 아이콘. 창을 숨겨도(✕·시작 시 숨기기·자동 실행) 프로그램이 돌고 있음을 보여 주고,
/// 누르면 창을 열고 오른쪽 클릭 메뉴로 완전히 끝낸다.
/// WPF 에는 알림 영역 아이콘이 없어 WinForms NotifyIcon 을 쓴다(WPF 디스패처가 메시지를 돌려 주므로 그대로 동작).
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ToolStripItem _openItem, _exitItem;
    private readonly Forms.ToolStripMenuItem _awayItem;
    private readonly Forms.ToolStripSeparator _awaySep;
    private bool _hintShown;

    /// <param name="setAway">자리 비움 원격 켜기·끄기(원격 훅이 있을 때만 메뉴에 보인다).</param>
    public TrayIcon(Action open, Action exit, Action<bool> setAway)
    {
        var menu = new Forms.ContextMenuStrip();
        _openItem = menu.Items.Add("", null, (_, _) => open());
        _openItem.Font = new System.Drawing.Font(_openItem.Font, System.Drawing.FontStyle.Bold);   // 기본 동작
        _awayItem = new Forms.ToolStripMenuItem("", null, (_, _) => setAway(!_awayItem!.Checked)) { Visible = false };
        _awaySep = new Forms.ToolStripSeparator { Visible = false };
        menu.Items.Add(_awaySep);
        menu.Items.Add(_awayItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        _exitItem = menu.Items.Add("", null, (_, _) => exit());
        ApplyLanguage();

        _icon = new Forms.NotifyIcon
        {
            Icon = LoadIcon(),
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
        _exitItem.Text = L.T("끝내기", "Exit");
        _awayItem.Text = L.T("🏃 자리 비움 (휴대폰 원격)", "🏃 Away (remote from the phone)");
    }

    /// <summary>자리 비움 메뉴: 원격 훅이 있을 때만 보이고, 켜져 있으면 체크.</summary>
    public void SetAway(bool away, bool available)
    {
        _awayItem.Visible = _awaySep.Visible = available;
        _awayItem.Checked = away && available;
    }

    /// <summary>마우스를 올렸을 때 보일 글(창 제목과 같게 — "(✋2) 우리 팀 사무실 v1.0.6"). 63자 제한.</summary>
    public void SetText(string text) => _icon.Text = text.Length <= 63 ? text : text[..62] + "…";

    /// <summary>처음 숨길 때 한 번만: 꺼진 게 아니라 여기서 돌고 있다고 알려 준다.</summary>
    public void HintOnce()
    {
        if (_hintShown) return;
        _hintShown = true;
        _onClick = null;
        _icon.ShowBalloonTip(4000, "Claude Office", L.T("알림 영역에서 계속 실행 중입니다(휴대폰 보기도 계속). 끝내려면 🏢 아이콘 오른쪽 클릭 → 끝내기",
                                                        "Still running in the notification area (phone view too). To quit, right-click the 🏢 icon → Exit"), Forms.ToolTipIcon.Info);
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

    public void Dispose()
    {
        _icon.Visible = false;   // 끝난 뒤에도 아이콘이 남아 보이지 않게
        _icon.Dispose();
    }

    private static System.Drawing.Icon LoadIcon()
    {
        var res = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/office.ico"));
        if (res is null) return System.Drawing.SystemIcons.Application;
        using var s = res.Stream;
        return new System.Drawing.Icon(s, Forms.SystemInformation.SmallIconSize);   // 화면 배율에 맞는 작은 아이콘
    }
}
