using System.Windows;
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
    private bool _hintShown;

    public TrayIcon(Action open, Action exit)
    {
        var menu = new Forms.ContextMenuStrip();
        var openItem = menu.Items.Add("열기", null, (_, _) => open());
        openItem.Font = new System.Drawing.Font(openItem.Font, System.Drawing.FontStyle.Bold);   // 기본 동작
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("끝내기", null, (_, _) => exit());

        _icon = new Forms.NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "Claude Office",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) open(); };
    }

    /// <summary>마우스를 올렸을 때 보일 글(창 제목과 같게 — "(✋2) 우리 팀 사무실 v1.0.6"). 63자 제한.</summary>
    public void SetText(string text) => _icon.Text = text.Length <= 63 ? text : text[..62] + "…";

    /// <summary>처음 숨길 때 한 번만: 꺼진 게 아니라 여기서 돌고 있다고 알려 준다.</summary>
    public void HintOnce()
    {
        if (_hintShown) return;
        _hintShown = true;
        _icon.ShowBalloonTip(4000, "Claude Office", "알림 영역에서 계속 실행 중입니다(휴대폰 보기도 계속). 끝내려면 🏢 아이콘 오른쪽 클릭 → 끝내기", Forms.ToolTipIcon.Info);
    }

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
