namespace ClaudeOffice.Monitoring;

/// <summary>결과(tool_result)를 아직 못 받은 도구 호출 = 지금 하고 있는 일.</summary>
internal sealed record PendingTool(string Id, string Name, string Text, Activity Activity, string Icon, DateTime StartUtc);

/// <summary>직원 상세 패널에 보여 줄 최근 작업 한 줄.</summary>
internal sealed record LogItem(DateTime Utc, string Icon, string Text);

internal enum WaitKind
{
    Permission,   // 도구 사용 승인 창
    Input,        // MCP 서버 등의 입력 요청 창
}

/// <summary>
/// 훅이 알려 준 "사람을 기다리는 중". 기록 파일에는 남지 않는 정보다.
/// 승인 대기는 기록 파일의 도구 호출(<see cref="ToolUseId"/>)과 짝지어 두고, 그 호출이 끝나면(승인·거절·중단) 내린다.
/// </summary>
internal sealed class HookWait(WaitKind kind, string? toolName, string? toolText, string text, DateTime utc)
{
    public WaitKind Kind { get; } = kind;
    public string? ToolName { get; } = toolName;   // null = 어떤 도구인지 모름(알림만 받음)
    public string? ToolText { get; } = toolText;   // 기록 파일의 도구 호출 말풍선과 같은 문구 → 짝 맞추기에 쓴다
    public string Text { get; } = text;            // 머리 위 말풍선
    public DateTime Utc { get; } = utc;
    public string? ToolUseId { get; set; }
    public string? Command { get; init; }        // 명령 실행 승인 요청이면 그 명령(ApprovalProbe)
    public DateTime ProbedUtc { get; set; }      // 마지막으로 프로세스를 살펴본 시각
}

/// <summary>
/// jsonl 기록 파일 하나(세션 또는 인턴=서브에이전트)를 읽어 쌓아 둔 상태.
/// 감시 스레드 하나에서만 만지므로 잠금을 두지 않는다.
/// </summary>
internal sealed class TranscriptState
{
    public const int RecentMax = 40;

    public TranscriptState(string filePath, string id, string? parentId)
    {
        FilePath = filePath;
        Id = id;
        ParentId = parentId;
    }

    public string FilePath { get; }
    public string Id { get; }

    /// <summary>인턴(서브에이전트)이면 소속 세션 ID, 일반 세션이면 null.</summary>
    public string? ParentId { get; }

    // 읽기 위치
    public long Offset;
    public bool Initialized;

    /// <summary>대화 메시지를 한 번이라도 읽었는지. 메타데이터만 있는 파일에는 책상을 만들지 않는다.</summary>
    public bool Seen;

    public DateTime StartedUtc;
    public DateTime FileWriteUtc;
    public DateTime LastEventUtc;

    // 기록에서 뽑은 정보
    public string? CustomTitle;   // /rename 으로 붙인 세션명
    public string? AiTitle;       // 자동 요약 제목
    public string? AgentName;
    public string? LastPrompt;
    public bool LastPromptFromPhone;   // 마지막 지시가 휴대폰 원격 지시(뒤따르는 last-prompt 줄은 PC 에서 친 옛 지시라 덮어쓰지 않는다)
    public string? Cwd;
    public string? Entrypoint;    // claude-vscode, cli ...
    public string? GitBranch;
    public string? Model;

    // 인턴 메타(agent-*.meta.json)
    public string? AgentType;
    public string? Description;

    // 도구를 쓰지 않을 때의 기본 상태
    public Activity BaseActivity = Activity.Idle;
    public string BaseIcon = "☕";
    public string BaseText = L.T("대기 중", "Standing by");
    public DateTime BaseSinceUtc;
    public string? LastCompletedMessageId;

    public readonly Dictionary<string, PendingTool> Pending = new(StringComparer.Ordinal);
    public readonly List<LogItem> Recent = new();

    /// <summary>도구 이름 → 마지막으로 결과를 받은 시각. 승인 요청이 왔는데 짝인 호출이 이미 끝났는지 가리는 데 쓴다.</summary>
    public readonly Dictionary<string, DateTime> ResolvedUtc = new(StringComparer.Ordinal);

    // 훅으로 알게 된 것
    public HookWait? Wait;

    /// <summary>오늘의 근무 일지(일한 시간·지시·도구·토큰).</summary>
    public readonly DayStats Today = new();
    public DateTime EndedUtc;     // SessionEnd 를 받은 시각. 그 뒤로 대화가 이어지면(resume) 무시된다

    /// <summary>마지막으로 일한 시각. 대화 기록의 timestamp 기준(파일 수정 시각은 메타데이터 줄 때문에 부정확).</summary>
    public DateTime LastActivityUtc => LastEventUtc != default ? LastEventUtc : FileWriteUtc;

    public void SetBase(Activity activity, string icon, string text, DateTime ts)
    {
        if (activity != BaseActivity || BaseSinceUtc == default)
            BaseSinceUtc = ts;
        BaseActivity = activity;
        BaseIcon = icon;
        BaseText = text;
    }

    public void Log(DateTime ts, string icon, string text)
    {
        Recent.Add(new LogItem(ts, icon, text));
        if (Recent.Count > RecentMax)
            Recent.RemoveRange(0, Recent.Count - RecentMax);
    }
}
