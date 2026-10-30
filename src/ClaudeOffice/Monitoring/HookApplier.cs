using ClaudeOffice.Hooks;

namespace ClaudeOffice.Monitoring;

/// <summary>
/// 훅 이벤트를 상태에 반영한다. 기록 파일(jsonl)에는 안 남는 "승인 창이 떠 있다", "세션을 닫았다"를 채운다.
/// 도구의 시작·끝은 여전히 기록 파일이 기준이고, 훅은 거기에 "사람을 기다리는 중"만 얹는다.
///
/// 승인한 순간을 알려 주는 훅은 없다(도구가 다 끝나야 PostToolUse가 온다). 그래서 승인 대기는
/// 기록 파일의 도구 호출과 짝지어 두고 그 호출이 끝날 때 내린다 → 승인 후 오래 도는 명령은 끝날 때까지 손을 들고 있다.
/// </summary>
internal static class HookApplier
{
    /// <summary>승인 요청은 왔는데 기록 파일의 도구 호출 줄이 아직 안 읽힌 경우 기다려 주는 시간.</summary>
    private static readonly TimeSpan BindGrace = TimeSpan.FromSeconds(5);

    /// <summary>도구 호출 줄은 승인 요청보다 먼저 기록되지만, 기록 시각과 받은 시각 사이의 오차를 감안한 여유.</summary>
    private static readonly TimeSpan BindSlack = TimeSpan.FromSeconds(2);

    /// <param name="target">이벤트가 난 곳: 인턴이면 그 인턴, 아니면 세션.</param>
    public static void Apply(TranscriptState session, TranscriptState target, IEnumerable<TranscriptState> interns, HookEvent ev)
    {
        switch (ev.Event)
        {
            case "PermissionRequest":
            {
                var what = ev.ToolText ?? ev.ToolName ?? "도구 사용";
                target.Wait = new HookWait(WaitKind.Permission, ev.ToolName, ev.ToolText, "승인해 주세요 · " + ToolCatalog.Clip(what, 30), ev.Utc);
                target.Log(ev.Utc, "✋", "승인 요청: " + what);
                break;
            }

            case "Notification":
                OnNotification(session, interns, ev);
                break;

            case "SessionEnd":
                session.EndedUtc = ev.Utc;
                session.Wait = null;
                session.Log(ev.Utc, "🚪", "세션 종료" + (ev.Reason != null ? $" ({ReasonText(ev.Reason)})" : ""));
                break;
        }
    }

    private static void OnNotification(TranscriptState session, IEnumerable<TranscriptState> interns, HookEvent ev)
    {
        switch (ev.NotificationType)
        {
            case "permission_prompt":
                // 보통은 PermissionRequest 가 먼저 와 있다. 이 알림은 그게 빠졌을 때의 대비책(어떤 도구인지는 모름).
                if (session.Wait?.Kind == WaitKind.Permission || interns.Any(i => i.Wait?.Kind == WaitKind.Permission)) return;
                session.Wait = new HookWait(WaitKind.Permission, null, null, "승인해 주세요!", ev.Utc);
                break;

            case "elicitation_dialog":
            case "elicitation_url_dialog":
                session.Wait = new HookWait(WaitKind.Input, null, null, "입력해 주세요!", ev.Utc);
                session.Log(ev.Utc, "✋", "입력 요청");
                break;

            case "elicitation_complete":
            case "elicitation_response":
                if (session.Wait?.Kind == WaitKind.Input) session.Wait = null;
                break;
        }
    }

    /// <summary>
    /// 매 틱 기록 파일을 읽은 뒤 부른다. 승인 대기를 기록 파일의 도구 호출과 짝짓고, 끝난 대기는 내린다.
    /// </summary>
    public static void Settle(TranscriptState s, DateTime now)
    {
        var w = s.Wait;
        if (w is null) return;

        if (w.Kind == WaitKind.Input)
        {
            if (s.LastEventUtc > w.Utc) s.Wait = null;   // 그 뒤로 대화가 이어졌으면 입력이 끝난 것
            return;
        }

        if (w.ToolUseId != null)
        {
            if (!s.Pending.ContainsKey(w.ToolUseId)) s.Wait = null;   // 승인·거절·중단으로 그 호출이 끝남
            return;
        }

        // 같은 도구가 여러 개 대기 중이면 말풍선 문구까지 같은 것, 그다음 가장 최근 것
        var match = s.Pending.Values
            .Where(p => (w.ToolName == null || p.Name == w.ToolName) && p.StartUtc <= w.Utc + BindSlack)
            .OrderByDescending(p => w.ToolText != null && p.Text == w.ToolText)
            .ThenByDescending(p => p.StartUtc)
            .FirstOrDefault();
        if (match != null)
        {
            w.ToolUseId = match.Id;
            return;
        }

        // 짝이 없다: 요청 뒤에 같은 도구의 결과가 이미 왔거나(곧바로 승인), 기다려도 호출 줄이 안 보이면 내린다
        bool resolvedAfter = w.ToolName != null
            ? s.ResolvedUtc.TryGetValue(w.ToolName, out var t) && t > w.Utc
            : s.ResolvedUtc.Values.Any(t2 => t2 > w.Utc);
        if (resolvedAfter || now - w.Utc > BindGrace) s.Wait = null;
    }

    private static string ReasonText(string reason) => reason switch
    {
        "clear" => "/clear",
        "resume" => "다른 세션으로 전환",
        "logout" => "로그아웃",
        "prompt_input_exit" => "종료",
        _ => "창 닫음",
    };
}
