using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClaudeOffice.Monitoring;

namespace ClaudeOffice.Hooks;

/// <summary>
/// Claude Code 전역 설정(~/.claude/settings.json)에 Claude Office 훅을 넣고 뺀다.
/// 전역 설정이라 화면의 🔔 버튼 → 확인 창을 거쳐서만 부른다.
///
/// 훅을 "type: http" 대신 명령으로 거는 이유: http 훅은 이 프로그램이 꺼져 있으면 연결 실패를
/// 도구를 쓸 때마다 "hook error" 알림으로 대화창에 남긴다. 명령 끝에 exit 0 을 붙이면 꺼져 있어도 조용히 지나간다.
/// Claude Code는 Windows에서 훅 명령을 Git Bash로, Git Bash가 없으면 PowerShell로 돌린다 → 넣을 때 Git Bash가 있으면 curl 명령,
/// 없으면 PowerShell 명령(+ "shell": "powershell"). PowerShell 에서 curl 은 다른 명령의 별칭이라 bash 용 명령이 동작하지 않는다.
/// </summary>
internal static class HookSetup
{
    /// <summary>우리가 넣은 훅을 알아보는 표시. 명령 끝의 주석(bash·PowerShell 모두 #)이라 실행에는 영향이 없다.</summary>
    private const string Marker = "# ClaudeOffice";

    // 승인 창·알림은 Claude가 기다릴 이유가 없으니 백그라운드(async)로 보낸다.
    // 세션 종료는 Claude Code가 곧 끝나 버리므로 보내기를 마칠 때까지 기다리게 한다(curl -m 1 이라 1초 안에 끝남).
    private static readonly (string Event, bool Async)[] Events =
    {
        ("PermissionRequest", true),   // 승인 창이 뜨기 직전
        ("Notification", true),        // 승인·입력 요청 알림(permission_prompt, elicitation_dialog ...)
        ("SessionEnd", false),         // 세션을 닫음 → 바로 퇴근
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,   // 한글·따옴표를 \uXXXX 로 바꾸지 않게
    };

    private static readonly object CacheLock = new();
    private static DateTime _cachedWriteUtc;
    private static bool _cachedInstalled;

    public static string SettingsPath => Path.Combine(TranscriptWatcher.ConfigDir(), "settings.json");

    public static string Command =>
        $"curl -s -o /dev/null -m 1 -H \"Content-Type: application/json\" --data-binary @- http://127.0.0.1:{HookServer.Port}/hook 2>/dev/null; exit 0 {Marker}";

    /// <summary>
    /// Git Bash 가 없는 PC용. 표준 입력을 바이트 그대로 보낸다(Windows PowerShell 은 표준 입력을 시스템 코드 페이지로 읽어 한글이 깨지므로 글자로 읽지 않는다).
    /// 실패해도 늘 exit 0 — 승인 요청 훅이 실패하면 Claude Code 가 도구 사용을 막을 수 있다.
    /// </summary>
    public static string PowerShellCommand =>
        "try { $m = New-Object IO.MemoryStream; [Console]::OpenStandardInput().CopyTo($m); " +
        $"Invoke-WebRequest -UseBasicParsing -Method Post -Uri http://127.0.0.1:{HookServer.Port}/hook -ContentType 'application/json' -Body $m.ToArray() -TimeoutSec 2 | Out-Null " +
        $"}} catch {{}}; exit 0 {Marker}";

    /// <summary>
    /// Claude Code 가 훅 명령에 쓸 Git Bash(bash.exe). 없으면 null → PowerShell 명령을 넣는다.
    /// Claude Code 와 같은 순서로 찾는다: CLAUDE_CODE_GIT_BASH_PATH → PATH 의 git.exe 옆 → 흔한 설치 위치.
    /// </summary>
    public static string? FindGitBash()
    {
        if (Environment.GetEnvironmentVariable("CLAUDE_CODE_GIT_BASH_PATH") is { Length: > 0 } env && File.Exists(env)) return env;

        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                if (!File.Exists(Path.Combine(dir, "git.exe"))) continue;
                // ...\Git\cmd\git.exe 또는 ...\Git\mingw64\bin\git.exe → ...\Git\bin\bash.exe
                for (var d = new DirectoryInfo(dir); d != null; d = d.Parent)
                {
                    var bash = Path.Combine(d.FullName, "bin", "bash.exe");
                    if (File.Exists(bash)) return bash;
                }
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        }

        foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + @"\Programs" })
        {
            var bash = Path.Combine(root, "Git", "bin", "bash.exe");
            if (File.Exists(bash)) return bash;
        }
        return null;
    }

    /// <summary>확인 창에 보일 설명: 어떤 셸로 무엇을 써서 보내는지.</summary>
    public static string ShellDescription => FindGitBash() != null
        ? L.T("Git Bash의 curl 사용", "uses curl in Git Bash")
        : L.T("Git Bash가 없어 PowerShell 사용", "no Git Bash, uses PowerShell");

    /// <summary>훅이 모두 들어 있는지. 감시 루프가 몇 초마다 부르므로 파일이 바뀌었을 때만 다시 읽는다.</summary>
    public static bool IsInstalled()
    {
        var fi = new FileInfo(SettingsPath);
        lock (CacheLock)
        {
            if (!fi.Exists) return _cachedInstalled = false;
            if (fi.LastWriteTimeUtc == _cachedWriteUtc) return _cachedInstalled;
            try
            {
                var (root, _) = Load();
                _cachedInstalled = root["hooks"] is JsonObject hooks
                                   && Events.All(e => hooks[e.Event] is JsonArray groups && groups.Any(HasOurs));
                _cachedWriteUtc = fi.LastWriteTimeUtc;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {
                _cachedInstalled = false;   // 쓰는 중이거나 깨진 파일 — 다음에 다시
            }
            return _cachedInstalled;
        }
    }

    public static void Install()
    {
        var (root, fmt) = Load();
        if (root["hooks"] is not JsonObject hooks)
        {
            if (root["hooks"] != null) throw new InvalidDataException(L.T("settings.json 의 hooks 항목 형식을 알 수 없습니다.", "The hooks entry in settings.json has an unknown format."));
            hooks = new JsonObject();
            root["hooks"] = hooks;
        }
        Remove(hooks);   // 전에 넣은 것이 있으면 빼고 다시 넣는다(중복 방지)

        bool bash = FindGitBash() != null;
        foreach (var (ev, isAsync) in Events)
        {
            if (hooks[ev] is not JsonArray groups)
            {
                if (hooks[ev] != null) throw new InvalidDataException(L.T($"settings.json 의 hooks.{ev} 형식을 알 수 없습니다.", $"hooks.{ev} in settings.json has an unknown format."));
                groups = new JsonArray();
                hooks[ev] = groups;
            }
            var handler = bash
                ? new JsonObject { ["type"] = "command", ["command"] = Command }
                : new JsonObject { ["type"] = "command", ["command"] = PowerShellCommand, ["shell"] = "powershell" };
            if (isAsync) handler["async"] = true;
            groups.Add(new JsonObject { ["hooks"] = new JsonArray(handler) });   // matcher 생략 = 전부
        }
        Save(root, fmt);
    }

    public static void Uninstall()
    {
        var (root, fmt) = Load();
        if (root["hooks"] is not JsonObject hooks || !Remove(hooks)) return;
        if (hooks.Count == 0) root.Remove("hooks");
        Save(root, fmt);
    }

    /// <summary>우리 표시가 붙은 처리기만 빼고, 그 때문에 비게 된 묶음·이벤트도 지운다. 다른 훅은 건드리지 않는다.</summary>
    private static bool Remove(JsonObject hooks)
    {
        bool changed = false;
        foreach (var (ev, node) in hooks.ToList())
        {
            if (node is not JsonArray groups) continue;
            bool removedHere = false;
            for (int i = groups.Count - 1; i >= 0; i--)
            {
                if (groups[i] is not JsonObject g || g["hooks"] is not JsonArray handlers) continue;
                int before = handlers.Count;
                for (int j = handlers.Count - 1; j >= 0; j--)
                    if (IsOurs(handlers[j])) handlers.RemoveAt(j);
                if (handlers.Count == before) continue;
                removedHere = true;
                if (handlers.Count == 0) groups.RemoveAt(i);
            }
            if (removedHere && groups.Count == 0) hooks.Remove(ev);
            changed |= removedHere;
        }
        return changed;
    }

    private static bool HasOurs(JsonNode? group) =>
        group is JsonObject g && g["hooks"] is JsonArray handlers && handlers.Any(IsOurs);

    private static bool IsOurs(JsonNode? handler) =>
        handler is JsonObject h && h["command"] is JsonValue v && v.TryGetValue<string>(out var cmd) && cmd.Contains(Marker, StringComparison.Ordinal);

    private readonly record struct TextFormat(bool Crlf, bool TrailingNewline);

    private static (JsonObject Root, TextFormat Format) Load()
    {
        var path = SettingsPath;
        if (!File.Exists(path)) return (new JsonObject(), new TextFormat(false, true));
        var text = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(text)) return (new JsonObject(), new TextFormat(false, true));

        // 주석이 있는 파일은 다시 쓰면 주석이 사라지므로 엄격하게 읽고, 안 되면 손대지 않는다
        if (JsonNode.Parse(text) is not JsonObject obj)
            throw new InvalidDataException(L.T("settings.json 이 JSON 객체가 아닙니다.", "settings.json is not a JSON object."));
        return (obj, new TextFormat(text.Contains("\r\n"), text.EndsWith('\n')));
    }

    private static void Save(JsonObject root, TextFormat fmt)
    {
        var path = SettingsPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path)) File.Copy(path, path + ".claudeoffice.bak", overwrite: true);   // 고치기 직전 원본

        var text = root.ToJsonString(WriteOptions).Replace("\r\n", "\n");   // 원래 줄바꿈 방식을 따른다
        if (fmt.TrailingNewline) text += "\n";
        if (fmt.Crlf) text = text.Replace("\n", "\r\n");

        // Claude Code가 읽는 도중 반쯤 쓴 파일을 보지 않게 임시 파일에 쓴 뒤 바꿔 끼운다
        var tmp = path + ".claudeoffice.tmp";
        File.WriteAllText(tmp, text, new UTF8Encoding(false));
        File.Move(tmp, path, overwrite: true);
    }
}
