using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ClaudeOffice.Monitoring;

/// <summary>
/// Claude Code 도구 이름을 직원 행동과 말풍선 문구로 바꾼다.
/// 말풍선은 되도록 한글로(사용자 요청): Claude가 영어로 쓴 설명·검색어는 명령 종류에 맞는 한글 문구로 바꾸고,
/// 원래 설명은 최근 작업 기록(<see cref="Detail"/>)에만 덧붙인다. 파일 이름·식별자는 그대로 둔다.
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

    /// <summary>머리 위 말풍선 문구(한글). 같은 입력이면 늘 같은 문구 — 훅의 승인 요청과 기록의 도구 호출을 짝지을 때도 쓴다.</summary>
    public static string Describe(string tool, JsonElement input)
    {
        switch (tool)
        {
            case "Read": return $"{FileOf(input, "file_path")} 읽는 중";
            case "Edit":
            case "MultiEdit": return $"{FileOf(input, "file_path")} 수정 중";
            case "Write": return $"{FileOf(input, "file_path")} 작성 중";
            case "NotebookEdit": return $"{FileOf(input, "notebook_path")} 수정 중";
            case "Grep":
            {
                // 식별자 하나('ProcessOrder')는 그대로, 영어 문장·긴 정규식은 한글 문구로
                var pat = Str(input, "pattern");
                return pat != null && (HasKorean(pat) || (!pat.Contains(' ') && pat.Length <= 24)) ? $"'{Clip(pat, 24)}' 찾는 중" : "코드에서 찾는 중";
            }
            case "Glob":
            {
                var name = FileOf(input, "pattern");
                return name == "파일" ? "파일 찾는 중" : $"'{Clip(name, 24)}' 파일 찾는 중";
            }
            case "ToolSearch": return "쓸 도구 찾는 중";
            case "Bash":
            case "PowerShell":
            {
                var desc = Str(input, "description");
                return Clip(HasKorean(desc) ? desc : CommandPhrase(Str(input, "command")), 40);
            }
            case "Monitor":
            {
                var desc = Str(input, "description");
                return Clip(HasKorean(desc) ? desc : "실행 지켜보는 중", 40);
            }
            case "WebSearch":
            {
                var q = Str(input, "query");
                return HasKorean(q) ? $"'{Clip(q, 24)}' 검색 중" : "웹 검색 중";
            }
            case "WebFetch": return "웹페이지 읽는 중";
            case "Agent":
            case "Task":
            {
                var desc = Str(input, "description");
                return HasKorean(desc) ? $"인턴에게: {Clip(desc, 30)}" : $"{AgentName(Str(input, "subagent_type"))}에게 일 맡기는 중";
            }
            case "Workflow": return "여러 인턴과 작업 중";
            case "SendMessage": return "인턴에게 메시지 보내는 중";
            case "AskUserQuestion": return "질문에 답해주세요!";
            case "ExitPlanMode": return "계획을 확인해주세요!";
            case "EnterPlanMode": return "계획 세우는 중";
            case "Skill": return $"{Clip(Str(input, "skill") ?? "스킬", 24)} 스킬 여는 중";
            case "TodoWrite":
            case "TaskCreate":
            case "TaskUpdate":
            case "TaskList":
            case "TaskGet": return "할 일 정리 중";
            case "TaskOutput":
            case "BashOutput": return "실행 결과 보는 중";
            case "TaskStop":
            case "KillShell": return "실행 멈추는 중";
            case "Artifact": return "결과 페이지 만드는 중";
            case "ScheduleWakeup":
            case "CronCreate": return "다음 일 예약하는 중";
        }

        if (tool.StartsWith("mcp__", StringComparison.Ordinal))
        {
            // mcp__claude_ai_Figma__get_design → "Figma 쓰는 중"
            var parts = tool.Split("__");
            var server = parts.Length >= 2 ? parts[1].Replace("claude_ai_", "").Replace('_', ' ').Trim() : "";
            return server.Length > 0 ? $"{Clip(server, 20)} 쓰는 중" : "외부 도구 쓰는 중";
        }
        return "도구 쓰는 중";
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
        null or "" => "인턴",
        "general-purpose" or "claude" => "일반 인턴",
        "Explore" => "탐색 인턴",
        "Plan" => "기획 인턴",
        "claude-code-guide" => "안내 인턴",
        "statusline-setup" => "설정 인턴",
        "fork" => "분신 인턴",
        _ => $"{type} 인턴",
    };

    private const string UnknownCommand = "명령 실행 중";

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
            if (phrase is "출력 중" or "기다리는 중" or "시각 확인 중") { minor ??= phrase; continue; }
            return phrase;
        }
        return minor ?? UnknownCommand;
    }

    private static string Phrase(string first, string second, string seg) => first switch
    {
        "dotnet" => second switch
        {
            "build" or "msbuild" => "빌드하는 중",
            "run" => "프로그램 실행 중",
            "test" => "테스트 돌리는 중",
            "publish" => "배포 파일 만드는 중",
            _ => "닷넷 작업 중",
        },
        "msbuild" => "빌드하는 중",
        "git" => second switch
        {
            "status" or "diff" or "log" or "show" or "blame" => "변경 내역 보는 중",
            "commit" => "커밋하는 중",
            "push" => "올리는 중(푸시)",
            "pull" or "fetch" or "clone" => "받아오는 중",
            "checkout" or "switch" or "branch" or "merge" or "rebase" => "브랜치 다루는 중",
            "add" or "restore" or "reset" or "stash" => "변경 정리 중",
            _ => "Git 작업 중",
        },
        "gh" => "GitHub 작업 중",
        "npm" or "npx" or "node" or "yarn" or "pnpm" =>
            seg.Contains("deploy", StringComparison.OrdinalIgnoreCase) ? "배포하는 중"
            : seg.Contains("install", StringComparison.OrdinalIgnoreCase) ? "패키지 설치 중"
            : "Node 작업 중",
        "python" or "python3" or "py" => "파이썬 실행 중",
        "pip" or "pip3" or "winget" or "choco" => "패키지 설치 중",
        "curl" or "wget" or "invoke-webrequest" or "invoke-restmethod" or "iwr" or "irm" => "인터넷에서 받아오는 중",
        "ls" or "dir" or "get-childitem" or "gci" or "find" or "tree" or "du" or "wc" or "test-path" => "파일 목록 보는 중",
        "cat" or "echo" or "printf" when seg.Contains('>') => "파일 작성 중",   // cat > 파일 <<EOF, echo … > 파일
        "cat" or "type" or "get-content" or "gc" or "head" or "tail" or "less" or "more" or "sed" or "od" or "file" or "get-item" => "파일 내용 보는 중",
        "grep" or "rg" or "select-string" or "sls" or "findstr" => "찾는 중",
        "cp" or "copy" or "copy-item" or "mv" or "move" or "move-item" or "mkdir" or "md" or "new-item" or "rm" or "del"
            or "remove-item" or "rmdir" or "touch" or "ren" or "rename-item" or "set-content" or "out-file" => "파일 정리 중",
        "powershell" or "pwsh" or "bash" or "sh" or "cmd" => "스크립트 실행 중",
        "get-process" or "tasklist" or "stop-process" or "taskkill" or "get-ciminstance" or "get-wmiobject" or "netstat"
            or "get-nettcpconnection" or "ps" or "kill" => "프로세스 확인 중",
        "claude" => "Claude 실행 중",
        "docker" => "도커 작업 중",
        "echo" or "write-output" or "write-host" or "printf" => "출력 중",
        "sleep" or "start-sleep" or "timeout" => "기다리는 중",
        "date" or "get-date" => "시각 확인 중",
        _ => UnknownCommand,
    };

    public static string? Str(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.String)
            return null;
        var s = v.GetString();
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }

    public static string Clip(string? s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = s.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return s.Length <= max ? s : s[..(max - 1)] + "…";
    }

    private static string FileOf(JsonElement input, string name)
    {
        var p = Str(input, name);
        if (p is null) return "파일";
        var file = Path.GetFileName(p.Replace('/', '\\').TrimEnd('\\'));
        return string.IsNullOrEmpty(file) ? "파일" : file;
    }

    private static string HostOf(string? url) =>
        url != null && Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : "웹페이지";

    private static string? FirstLine(string? s)
    {
        if (s is null) return null;
        var line = s.Split('\n')[0].Trim();
        return line.Length == 0 ? null : line;
    }
}
