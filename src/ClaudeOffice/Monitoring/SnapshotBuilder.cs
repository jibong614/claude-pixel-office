using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaudeOffice.Monitoring;

// 화면(office.js)으로 보내는 모양. 시각은 모두 Unix 밀리초.
internal sealed record SnapshotDto(long Now, HooksDto? Hooks, List<EmployeeDto> Sessions);

/// <summary>훅 연결 상태. State: on(설치됨·수신 중) / off(설치 안 됨) / error(설치됐지만 수신 못 함). Last: 마지막 수신 시각.</summary>
internal sealed record HooksDto(string State, string? Error, long Last);

internal sealed record EmployeeDto(
    string Id,
    string Name,
    string? Title,
    string Team,
    string? Cwd,
    string? Entry,
    string? Branch,
    string? Model,
    string Act,
    string Icon,
    string Text,
    long Since,
    long Last,
    long Started,
    string? Prompt,
    string? Reply,
    long? Ctx,
    List<LogDto> Recent,
    List<InternDto> Interns,
    DayDto? Today,
    DayDto? Yesterday,
    DayDto? Week,
    string? Thinking = null,
    List<TodoDto>? Todos = null,
    bool Secret = false);

internal sealed record TodoDto(string Content, string Status);

/// <summary>오늘의 근무 일지(인턴 몫 포함). Work: 일한 시간(ms), In·Out·CacheRead·CacheWrite: 토큰, Tools: 많이 쓴 도구 [이름, 횟수].</summary>
internal sealed record DayDto(long Work, int Prompts, long In, long Out, long CacheRead, long CacheWrite, List<ToolCountDto> Tools);

internal sealed record ToolCountDto(string N, int C);

internal sealed record InternDto(string Id, string Name, string? Desc, string Act, string Icon, string Text, long Since);

internal sealed record LogDto(long T, string Icon, string Text);

/// <summary>쌓인 상태를 "지금 이 직원이 어떤 모습인가"로 바꿔 JSON으로 만든다.</summary>
internal static class SnapshotBuilder
{
    // 시간이 지나면서 바뀌는 상태(기록 파일에는 남지 않으므로 여기서 계산)
    private static readonly TimeSpan IdleToSleep = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan BusyTooLong = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan ToAway = TimeSpan.FromHours(3);
    private static readonly TimeSpan EditPendingAsWaiting = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan InternLinger = TimeSpan.FromSeconds(20);   // 일 끝낸 인턴이 남아 있는 시간
    private const int RecentToSend = 15;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <param name="window">표시 기간: 마지막 대화가 이보다 오래된 세션은 넣지 않는다.</param>
    /// <param name="hooks">훅 연결 상태. null이면 훅을 쓰지 않는 실행.</param>
    public static string Build(IEnumerable<TranscriptState> states, DateTime now, TimeSpan window, HooksDto? hooks = null)
    {
        var all = states.ToList();
        var interns = all.Where(s => s.ParentId != null && s.Seen)
                         .ToLookup(s => s.ParentId!, StringComparer.OrdinalIgnoreCase);

        // 훅이 연결돼 있으면 승인 대기를 훅이 정확히 알려 주므로 "파일 수정이 오래 걸리면 승인 대기" 추정은 끈다
        bool hooksOn = hooks?.State == "on";

        // 먼저 출근한 순서대로 책상을 준다(자리가 들쭉날쭉하지 않도록).
        // 파일 수정 시각은 최근이어도(세션 목록을 열면 메타데이터 줄이 덧붙는다) 마지막 대화가 오래됐으면 숨긴다.
        var sessions = all.Where(s => s.ParentId == null && s.Seen && now - s.LastActivityUtc <= window)
                          .OrderBy(s => s.StartedUtc).ThenBy(s => s.Id, StringComparer.Ordinal)
                          .Select(s => ToDto(s, interns[s.Id], now, hooksOn))
                          .ToList();

        return JsonSerializer.Serialize(new SnapshotDto(Ms(now), hooks, sessions), JsonOptions);
    }

    private static EmployeeDto ToDto(TranscriptState s, IEnumerable<TranscriptState> interns, DateTime now, bool hooksOn)
    {
        var (act, icon, text, since) = View(s, now, hooksOn);
        var name = NameOf(s);

        var internDtos = new List<InternDto>();
        DateTime? internWaitSince = null;
        foreach (var it in interns.OrderBy(x => x.StartedUtc))
        {
            var (ia, ii, itx, isince) = View(it, now, hooksOn);
            bool finished = ia is Activity.Idle or Activity.Sleeping or Activity.Away;
            if (finished && now - it.LastActivityUtc > InternLinger) continue;
            if (finished) { ia = Activity.Idle; ii = "✅"; itx = L.T("다 했어요!", "All done!"); }
            if (ia is Activity.Waiting && it.Wait != null) internWaitSince ??= isince;
            internDtos.Add(new InternDto(it.Id, ToolCatalog.AgentName(it.AgentType), it.Description,
                Lower(ia), ii, itx, Ms(isince)));
        }

        // 인턴의 승인 창도 사람이 응답할 곳은 이 세션 창이므로 직원이 대신 손을 든다
        if (internWaitSince is { } iws && act is not (Activity.Asking or Activity.Waiting or Activity.Away))
            (act, icon, text, since) = (Activity.Waiting, "✋", L.T("인턴 일에 승인해 주세요", "Intern needs approval"), iws);

        var internList = interns.ToList();
        var today = now.ToLocalTime().Date;

        return new EmployeeDto(
            Id: s.Id,
            Name: name,
            Title: s.AiTitle != null && s.AiTitle != name ? s.AiTitle : null,
            Team: TeamOf(s),
            Cwd: s.Cwd,
            Entry: s.Entrypoint,
            Branch: s.GitBranch,
            Model: s.Model,
            Act: Lower(act),
            Icon: icon,
            Text: text,
            Since: Ms(since),
            Last: Ms(s.LastActivityUtc),
            Started: Ms(s.StartedUtc),
            Prompt: s.LastPrompt != null ? ToolCatalog.Clip(s.LastPrompt, 300) : null,
            Reply: s.LastReply != null ? ToolCatalog.Clip(s.LastReply, 240) : null,
            Ctx: s.Today.LastContextTokens > 0 ? s.Today.LastContextTokens : null,
            Recent: s.Recent.AsEnumerable().Reverse().Take(RecentToSend)
                     .Select(x => new LogDto(Ms(x.Utc), x.Icon, x.Text)).ToList(),
            Interns: internDtos,
            Today: RangeDay(s, internList, today, today),
            Yesterday: RangeDay(s, internList, today.AddDays(-1), today.AddDays(-1)),
            Week: RangeDay(s, internList, today.AddDays(-6), today),
            Thinking: s.LastThinking,
            Todos: s.Todos?.Select(t => new TodoDto(t.Content, t.Status)).ToList(),
            Secret: s.HasSecret);
    }

    /// <summary>지금 보여 줄 행동·아이콘·말풍선 문구·그 상태가 시작된 시각.</summary>
    private static (Activity, string, string, DateTime) View(TranscriptState s, DateTime now, bool hooksOn)
    {
        if (s.EndedUtc != default && s.EndedUtc >= s.LastActivityUtc)
            return (Activity.Away, "🌙", L.T("퇴근", "Gone home"), s.EndedUtc);   // 세션을 닫음(SessionEnd 훅). 다시 이어서 대화하면 풀린다

        var quiet = now - s.LastActivityUtc;
        if (quiet > ToAway)
            return (Activity.Away, "🌙", L.T("퇴근", "Gone home"), s.LastActivityUtc);

        if (s.Wait is { } w)   // 훅이 알려 준 확실한 대기(승인 창·입력 요청)
            return (w.Kind == WaitKind.Input ? Activity.Asking : Activity.Waiting, "✋", w.Text, w.Utc);

        Activity act;
        string icon, text;
        DateTime since;
        bool toolRunning = s.Pending.Count > 0;

        if (toolRunning)
        {
            var p = s.Pending.Values.MaxBy(x => x.StartUtc)!;   // 동시에 여러 개면 가장 최근 것
            act = p.Activity;
            icon = p.Icon;
            text = p.Text;
            since = p.StartUtc;
            if (s.Pending.Count > 1) text += L.T($" 외 {s.Pending.Count - 1}건", $" +{s.Pending.Count - 1} more");

            if (!hooksOn && ToolCatalog.IsEditTool(p.Name) && now - p.StartUtc > EditPendingAsWaiting)
            {
                act = Activity.Waiting;
                icon = "✋";
                text = L.T("승인 기다리는 중?", "Needs approval?");
            }
        }
        else
        {
            act = s.BaseActivity;
            icon = s.BaseIcon;
            text = s.BaseText;
            since = s.BaseSinceUtc != default ? s.BaseSinceUtc : s.LastActivityUtc;
        }

        if (act is Activity.Asking or Activity.Waiting)
            return (act, icon, text, since);   // 사람을 기다리는 중이면 오래돼도 그대로

        if (act == Activity.Idle && quiet > IdleToSleep)
            return (Activity.Sleeping, "💤", "zzz", s.LastActivityUtc + IdleToSleep);

        if (!toolRunning && act is Activity.Thinking or Activity.Talking && quiet > IdleToSleep)
            return (Activity.Sleeping, "💤", L.T("응답 없음", "Not responding"), s.LastActivityUtc);   // 창을 닫았거나 멈춤

        if (toolRunning && quiet > BusyTooLong)
            text += L.T(" · 오래 걸리는 중", " · taking long");

        return (act, icon, text, since);
    }

    /// <summary>세션과 그 인턴들의 지정 기간 기록을 합친다(일한 시간·지시는 세션 것만, 도구·토큰은 인턴 몫까지). 기록이 없으면 null.</summary>
    private static DayDto? RangeDay(TranscriptState s, IReadOnlyList<TranscriptState> interns, DateTime fromLocalDate, DateTime toLocalDate)
    {
        long work = 0;
        int prompts = 0;
        long tin = 0, tout = 0, cr = 0, cw = 0;
        var tools = new Dictionary<string, int>(StringComparer.Ordinal);
        bool any = false;

        foreach (var b in s.Today.GetRange(fromLocalDate, toLocalDate))
        {
            any = true;
            work += b.WorkMs;
            prompts += b.Prompts;
            foreach (var u in b.Messages.Values) { tin += u.In; tout += u.Out; cr += u.CacheRead; cw += u.CacheWrite; }
            foreach (var (name, c) in b.Tools) tools[name] = tools.GetValueOrDefault(name) + c;
        }
        foreach (var it in interns)
        {
            foreach (var b in it.Today.GetRange(fromLocalDate, toLocalDate))
            {
                any = true;
                foreach (var u in b.Messages.Values) { tin += u.In; tout += u.Out; cr += u.CacheRead; cw += u.CacheWrite; }
                foreach (var (name, c) in b.Tools) tools[name] = tools.GetValueOrDefault(name) + c;
            }
        }
        if (!any) return null;
        return new DayDto(work, prompts, tin, tout, cr, cw,
            tools.OrderByDescending(kv => kv.Value).Take(6).Select(kv => new ToolCountDto(kv.Key, kv.Value)).ToList());
    }

    private static string NameOf(TranscriptState s)
    {
        if (s.CustomTitle != null) return s.CustomTitle;
        if (s.AgentName != null) return s.AgentName;
        if (s.AiTitle != null) return ToolCatalog.Clip(s.AiTitle, 18);
        return L.T("세션 ", "Session ") + s.Id[..Math.Min(8, s.Id.Length)];
    }

    /// <summary>
    /// 작업 폴더의 마지막 두 단계를 팀 이름으로 쓴다(예: C:\Projects\acme\web-shop → acme/web-shop).
    /// 구분자를 '/'로 쓰는 이유: 맑은 고딕은 '\'를 '₩'로 그린다.
    /// </summary>
    private static string TeamOf(TranscriptState s)
    {
        if (string.IsNullOrWhiteSpace(s.Cwd))
            return Path.GetFileName(Path.GetDirectoryName(s.FilePath)) ?? L.T("기타", "Other");

        var parts = s.Cwd.Replace('/', '\\').TrimEnd('\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => L.T("기타", "Other"),
            1 => parts[0],
            _ => $"{parts[^2]}/{parts[^1]}",
        };
    }

    private static string Lower(Activity a) => a.ToString().ToLowerInvariant();

    internal static long Ms(DateTime utc) =>
        utc == default ? 0 : new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
}
