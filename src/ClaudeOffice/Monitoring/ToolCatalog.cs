using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ClaudeOffice.Monitoring;

/// <summary>
/// Claude Code 도구 이름을 직원 행동과 말풍선 문구로 바꾼다.
/// 말풍선은 되도록 한글로(사용자 요청): Claude가 영어로 쓴 설명·검색어는 명령 종류에 맞는 한글 문구로 바꾸고,
/// 원래 설명은 최근 작업 기록(<see cref="Detail"/>)에만 덧붙인다. 파일 이름·식별자는 그대로 둔다.
/// 영어 화면(<see cref="L.En"/>)이면 문구도 영어로, Claude가 영어로 쓴 설명은 그대로 쓴다.
/// </summary>
internal static partial class ToolCatalog
{
    [GeneratedRegex("[가-힣]")]
    private static partial Regex HangulChar();

    // 명령을 이어 붙이는 기호: && || ; | 줄바꿈
    [GeneratedRegex(@"&&|\|\||;|\||\r?\n")]
    private static partial Regex CommandSeparators();

    public static bool HasKorean(string? s) => s != null && HangulChar().IsMatch(s);
    public static (Activity Activity, string Icon) Classify(string tool)
    {
        if (tool.StartsWith("mcp__", StringComparison.Ordinal))
            return (Activity.Working, "🔌");

        return tool switch
        {
            "Read" or "NotebookRead" => (Activity.Reading, "📄"),
            "Skill" => (Activity.Reading, "📚"),
            "Grep" or "Glob" or "LS" or "ToolSearch" => (Activity.Searching, "🔍"),
            "Edit" or "MultiEdit" or "Write" or "NotebookEdit" => (Activity.Writing, "✏️"),
            "Bash" or "PowerShell" or "Monitor" or "BashOutput" or "KillShell" or "TaskOutput" or "TaskStop" => (Activity.Running, "⌨️"),
            "WebFetch" or "WebSearch" => (Activity.Browsing, "🌐"),
            "Agent" or "Task" or "Workflow" or "SendMessage" => (Activity.Delegating, "👥"),
            "TodoWrite" or "TaskCreate" or "TaskUpdate" or "TaskList" or "TaskGet" or "EnterPlanMode" => (Activity.Planning, "📋"),
            "AskUserQuestion" or "ExitPlanMode" => (Activity.Asking, "✋"),
            _ => (Activity.Working, "🛠️"),
        };
    }

    /// <summary>
    /// 파일을 고치는 도구. 승인이 필요 없으면 1초 안에 끝나므로,
    /// 결과가 오래 안 오면 권한 승인 창이 떠 있는 것으로 추정한다(기록 파일에는 승인 대기가 남지 않는다).
    /// </summary>
    public static bool IsEditTool(string tool) => tool is "Edit" or "MultiEdit" or "Write" or "NotebookEdit";

    /// <summary>
    /// Claude가 쓴 설명·검색어가 화면 언어로 쓰였는지 — 그렇다면 그대로 말풍선에 쓴다.
    /// 한국어 화면은 한글이 있으면, 영어 화면은 한글이 없으면(영어로 쓴 설명은 그대로가 더 자세하다).
    /// </summary>
    private static bool InUiLanguage(string? s) => s != null && (L.En ? !HasKorean(s) : HasKorean(s));

    /// <summary>머리 위 말풍선 문구(화면 언어). 같은 입력이면 늘 같은 문구 — 훅의 승인 요청과 기록의 도구 호출을 짝지을 때도 쓴다.</summary>
    public static string Describe(string tool, JsonElement input)
    {
        switch (tool)
        {
            case "Read": return L.T($"{FileOf(input, "file_path")} 읽는 중", $"Reading {FileOf(input, "file_path")}");
            case "Edit":
            case "MultiEdit": return L.T($"{FileOf(input, "file_path")} 수정 중", $"Editing {FileOf(input, "file_path")}");
            case "Write": return L.T($"{FileOf(input, "file_path")} 작성 중", $"Writing {FileOf(input, "file_path")}");
            case "NotebookEdit": return L.T($"{FileOf(input, "notebook_path")} 수정 중", $"Editing {FileOf(input, "notebook_path")}");
            case "Grep":
            {
                // 식별자 하나('ProcessOrder')는 그대로, 영어 문장·긴 정규식은 화면 언어 문구로
                var pat = Str(input, "pattern");
                return pat != null && (HasKorean(pat) || (!pat.Contains(' ') && pat.Length <= 24))
                    ? L.T($"'{Clip(pat, 24)}' 찾는 중", $"Finding '{Clip(pat, 24)}'")
                    : L.T("코드에서 찾는 중", "Searching code");
            }
            case "Glob":
            {
                var name = FileOf(input, "pattern");
                return name == FileWord ? L.T("파일 찾는 중", "Finding files") : L.T($"'{Clip(name, 24)}' 파일 찾는 중", $"Finding '{Clip(name, 24)}' files");
            }
            case "ToolSearch": return L.T("쓸 도구 찾는 중", "Looking for a tool");
            case "Bash":
            case "PowerShell":
            {
                var desc = Str(input, "description");
                return Clip(InUiLanguage(desc) ? desc : CommandPhrase(Str(input, "command")), 40);
            }
            case "Monitor":
            {
                var desc = Str(input, "description");
                return Clip(InUiLanguage(desc) ? desc : L.T("실행 지켜보는 중", "Watching a run"), 40);
            }
            case "WebSearch":
            {
                var q = Str(input, "query");
                return InUiLanguage(q) ? L.T($"'{Clip(q, 24)}' 검색 중", $"Searching '{Clip(q, 24)}'") : L.T("웹 검색 중", "Searching the web");
            }
            case "WebFetch": return L.T("웹페이지 읽는 중", "Reading a web page");
            case "Agent":
            case "Task":
            {
                var desc = Str(input, "description");
                return InUiLanguage(desc) ? L.T($"인턴에게: {Clip(desc, 30)}", $"To intern: {Clip(desc, 30)}")
                    : L.T($"{AgentName(Str(input, "subagent_type"))}에게 일 맡기는 중", $"Briefing {AgentName(Str(input, "subagent_type"))}");
            }
            case "Workflow": return L.T("여러 인턴과 작업 중", "Working with interns");
            case "SendMessage": return L.T("인턴에게 메시지 보내는 중", "Messaging an intern");
            case "AskUserQuestion": return L.T("질문에 답해주세요!", "Got a question!");
            case "ExitPlanMode": return L.T("계획을 확인해주세요!", "Please review the plan!");
            case "EnterPlanMode": return L.T("계획 세우는 중", "Making a plan");
            case "Skill":
            {
                var skill = Str(input, "skill");
                return L.T($"{Clip(skill ?? "스킬", 24)} 스킬 여는 중", skill != null ? $"Opening {Clip(skill, 24)} skill" : "Opening a skill");
            }
            case "TodoWrite":
            case "TaskCreate":
            case "TaskUpdate":
            case "TaskList":
            case "TaskGet": return L.T("할 일 정리 중", "Sorting to-dos");
            case "TaskOutput":
            case "BashOutput": return L.T("실행 결과 보는 중", "Checking output");
            case "TaskStop":
            case "KillShell": return L.T("실행 멈추는 중", "Stopping a run");
            case "Artifact": return L.T("결과 페이지 만드는 중", "Making a page");
            case "ScheduleWakeup":
            case "CronCreate": return L.T("다음 일 예약하는 중", "Scheduling next task");
        }

        if (tool.StartsWith("mcp__", StringComparison.Ordinal))
        {
            // mcp__claude_ai_Figma__get_design → "Figma 쓰는 중"
            var parts = tool.Split("__");
            var server = parts.Length >= 2 ? parts[1].Replace("claude_ai_", "").Replace('_', ' ').Trim() : "";
            return server.Length > 0 ? L.T($"{Clip(server, 20)} 쓰는 중", $"Using {Clip(server, 20)}") : L.T("외부 도구 쓰는 중", "Using an external tool");
        }
        return L.T("도구 쓰는 중", "Using a tool");
    }

    /// <summary>최근 작업 기록용: 말풍선 문구 + (한글로 바꾸느라 빠진) 원래 설명·검색어·주소.</summary>
    public static string Detail(string tool, JsonElement input)
    {
        var bubble = Describe(tool, input);
        var extra = tool switch
        {
            "Bash" or "PowerShell" or "Monitor" => Str(input, "description") ?? FirstLine(Str(input, "command")),
            "WebSearch" => Str(input, "query"),
            "WebFetch" => HostOf(Str(input, "url")),
            "Agent" or "Task" => Str(input, "description"),
            "Grep" or "Glob" => Str(input, "pattern"),
            _ when tool.StartsWith("mcp__", StringComparison.Ordinal) => tool.Split("__").LastOrDefault(),
            _ => null,
        };
        return extra != null && !bubble.Contains(extra, StringComparison.Ordinal) ? $"{bubble} — {Clip(extra, 60)}" : bubble;
    }

    /// <summary>서브에이전트 종류 → 인턴 이름(모르는 종류는 그 이름 + 인턴).</summary>
    public static string AgentName(string? type) => type switch
    {
        null or "" => L.T("인턴", "Intern"),
        "general-purpose" or "claude" => L.T("일반 인턴", "General intern"),
        "Explore" => L.T("탐색 인턴", "Explore intern"),
        "Plan" => L.T("기획 인턴", "Plan intern"),
        "claude-code-guide" => L.T("안내 인턴", "Guide intern"),
        "statusline-setup" => L.T("설정 인턴", "Setup intern"),
        "fork" => L.T("분신 인턴", "Fork intern"),
        _ => L.T($"{type} 인턴", $"{type} intern"),
    };

    private static string UnknownCommand => L.T("명령 실행 중", "Running a command");

    // 다른 명령이 없을 때만 쓰는 사소한 문구(출력·대기·시각 확인)
    private static string PrintingPhrase => L.T("출력 중", "Printing");
    private static string WaitingPhrase => L.T("기다리는 중", "Waiting");
    private static string TimePhrase => L.T("시각 확인 중", "Checking the time");

    /// <summary>
    /// 명령줄을 한글 문구로: "cd x &amp;&amp; dotnet build …" → "빌드하는 중". 이어 붙인 명령 중 <b>알아본 첫 명령</b>을 쓴다
    /// (cd·변수 대입·문자열·if 등은 건너뛰고, 출력·대기처럼 사소한 것은 다른 게 없을 때만). 모르면 "명령 실행 중".
    /// </summary>
    public static string CommandPhrase(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return UnknownCommand;
        string? minor = null;
        foreach (var seg in CommandSeparators().Split(command))
        {
            var words = seg.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int i = 0;
            bool called = false;   // & "C:\…\npx.cmd" 처럼 따옴표 친 프로그램을 부르는 경우
            while (i < words.Length)
            {
                var w = words[i];
                if (w == "&") { called = true; i++; continue; }
                if (w is "{" or "}" or "(" or ")" or "time" or "try" or "else" or "finally" or "do"
                    || (w.Length > 1 && w.Contains('=') && !w.StartsWith('-') && !w.StartsWith('"'))) { i++; continue; }   // 블록 기호, VAR=값
                if (i + 1 < words.Length && words[i + 1] == "=") { i += 2; continue; }   // $x = 명령 … → 오른쪽 명령을 본다
                if (w.ToLowerInvariant() is "if" or "elseif" or "while" or "foreach" or "for" or "switch")
                {
                    // if (조건) { 명령 } → 중괄호 안의 명령을 본다
                    int brace = Array.FindIndex(words, i + 1, x => x.StartsWith('{'));
                    i = brace < 0 ? words.Length : words[brace] == "{" ? brace + 1 : brace;
                    continue;
                }
                break;
            }
            if (i >= words.Length) continue;
            if (!called && (words[i].StartsWith('"') || words[i].StartsWith('\''))) continue;   // 문자열을 찍기만 하는 줄
            var raw = words[i].Trim('"', '\'', '(', ')', '{', '}', '`', '@');
            if (raw.Length == 0 || raw.StartsWith('$') || raw.StartsWith('#')) continue;
            var first = Path.GetFileNameWithoutExtension(raw.Replace('/', '\\')).ToLowerInvariant();
            if (first is "cd" or "set-location" or "sl" or "pushd" or "popd" or "export" or "set") continue;
            var second = i + 1 < words.Length ? words[i + 1].ToLowerInvariant() : "";
            var phrase = Phrase(first, second, seg);
            if (phrase == UnknownCommand) continue;
            if (phrase == PrintingPhrase || phrase == WaitingPhrase || phrase == TimePhrase) { minor ??= phrase; continue; }
            return phrase;
        }
        return minor ?? UnknownCommand;
    }

    private static string Phrase(string first, string second, string seg) => first switch
    {
        "dotnet" => second switch
        {
            "build" or "msbuild" => L.T("빌드하는 중", "Building"),
            "run" => L.T("프로그램 실행 중", "Running the app"),
            "test" => L.T("테스트 돌리는 중", "Running tests"),
            "publish" => L.T("배포 파일 만드는 중", "Publishing"),
            _ => L.T("닷넷 작업 중", "Running dotnet"),
        },
        "msbuild" => L.T("빌드하는 중", "Building"),
        "git" => second switch
        {
            "status" or "diff" or "log" or "show" or "blame" => L.T("변경 내역 보는 중", "Checking changes"),
            "commit" => L.T("커밋하는 중", "Committing"),
            "push" => L.T("올리는 중(푸시)", "Pushing"),
            "pull" or "fetch" or "clone" => L.T("받아오는 중", "Fetching"),
            "checkout" or "switch" or "branch" or "merge" or "rebase" => L.T("브랜치 다루는 중", "Switching branches"),
            "add" or "restore" or "reset" or "stash" => L.T("변경 정리 중", "Tidying changes"),
            _ => L.T("Git 작업 중", "Using Git"),
        },
        "gh" => L.T("GitHub 작업 중", "Using GitHub"),
        "npm" or "npx" or "node" or "yarn" or "pnpm" =>
            seg.Contains("deploy", StringComparison.OrdinalIgnoreCase) ? L.T("배포하는 중", "Deploying")
            : seg.Contains("install", StringComparison.OrdinalIgnoreCase) ? L.T("패키지 설치 중", "Installing packages")
            : L.T("Node 작업 중", "Running Node"),
        "python" or "python3" or "py" => L.T("파이썬 실행 중", "Running Python"),
        "pip" or "pip3" or "winget" or "choco" => L.T("패키지 설치 중", "Installing packages"),
        "curl" or "wget" or "invoke-webrequest" or "invoke-restmethod" or "iwr" or "irm" => L.T("인터넷에서 받아오는 중", "Downloading"),
        "ls" or "dir" or "get-childitem" or "gci" or "find" or "tree" or "du" or "wc" or "test-path" => L.T("파일 목록 보는 중", "Listing files"),
        "cat" or "echo" or "printf" when seg.Contains('>') => L.T("파일 작성 중", "Writing a file"),   // cat > 파일 <<EOF, echo … > 파일
        "cat" or "type" or "get-content" or "gc" or "head" or "tail" or "less" or "more" or "sed" or "od" or "file" or "get-item" => L.T("파일 내용 보는 중", "Reading a file"),
        "grep" or "rg" or "select-string" or "sls" or "findstr" => L.T("찾는 중", "Searching"),
        "cp" or "copy" or "copy-item" or "mv" or "move" or "move-item" or "mkdir" or "md" or "new-item" or "rm" or "del"
            or "remove-item" or "rmdir" or "touch" or "ren" or "rename-item" or "set-content" or "out-file" => L.T("파일 정리 중", "Managing files"),
        "powershell" or "pwsh" or "bash" or "sh" or "cmd" => L.T("스크립트 실행 중", "Running a script"),
        "get-process" or "tasklist" or "stop-process" or "taskkill" or "get-ciminstance" or "get-wmiobject" or "netstat"
            or "get-nettcpconnection" or "ps" or "kill" => L.T("프로세스 확인 중", "Checking processes"),
        "claude" => L.T("Claude 실행 중", "Running Claude"),
        "docker" => L.T("도커 작업 중", "Using Docker"),
        "echo" or "write-output" or "write-host" or "printf" => PrintingPhrase,
        "sleep" or "start-sleep" or "timeout" => WaitingPhrase,
        "date" or "get-date" => TimePhrase,
        _ => UnknownCommand,
    };

    public static string? Str(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.String)
            return null;
        var s = v.GetString();
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }

    public static bool Bool(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    public static long Num(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : 0;

    public static string Clip(string? s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = s.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return s.Length <= max ? s : s[..(max - 1)] + "…";
    }

    /// <summary>파일 이름을 모를 때 대신 쓰는 말.</summary>
    private static string FileWord => L.T("파일", "file");

    private static string FileOf(JsonElement input, string name)
    {
        var p = Str(input, name);
        if (p is null) return FileWord;
        var file = Path.GetFileName(p.Replace('/', '\\').TrimEnd('\\'));
        return string.IsNullOrEmpty(file) ? FileWord : file;
    }

    private static string HostOf(string? url) =>
        url != null && Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : L.T("웹페이지", "web page");

    private static string? FirstLine(string? s)
    {
        if (s is null) return null;
        var line = s.Split('\n')[0].Trim();
        return line.Length == 0 ? null : line;
    }
}
