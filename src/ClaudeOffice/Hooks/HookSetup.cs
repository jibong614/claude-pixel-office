using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClaudeOffice.Monitoring;

namespace ClaudeOffice.Hooks;

/// <summary>
/// Claude Code 전역 설정(~/.claude/settings.json)에 Claude Office 훅을 넣고 뺀다.
/// 전역 설정이라 화면의 🔔 버튼 → 확인 창을 거쳐서만 부른다(이미 연결한 원격 훅을 새 버전 명령으로 바꾸는 UpgradeRemote 만 묻지 않는다).
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

    /// <summary>자리 비움 원격 훅 표시(Marker 로 시작하므로 "우리 훅" 전체를 지울 때 함께 지워진다).</summary>
    private const string RemoteMarker = "# ClaudeOffice-remote";

    // 자리 비움 원격(RemoteHub): 승인은 휴대폰 답을 기다리는 동기 훅(최대 1시간), 지시는 뒤에서 기다리다 세션을 깨우는 asyncRewake 훅(최대 하루)
    private const int ApproveTimeout = 3600, ListenTimeout = 86400;
    private static readonly (string Event, string Kind)[] RemoteEvents =
    {
        ("PermissionRequest", "approve"),   // 자리 비움이면 휴대폰에서 승인·거절
        ("Stop", "listen"),                 // 답을 마치면 휴대폰 지시를 기다린다
        ("SessionStart", "listen"),         // 막 연 세션도(아직 한 번도 답하지 않았어도) 지시를 받게
        ("PreToolUse", "pretool"),          // 도구 실행 전 긴급 중단 플래그 검사 (중단 시 exit 2로 중지)
    };

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
    private static (bool Monitor, bool Remote) _cached;
    private static string? _token;
    private static string? _tokenDir;

    public static string SettingsPath => Path.Combine(TranscriptWatcher.ConfigDir(), "settings.json");

    /// <summary>
    /// 같은 PC의 다른 프로그램이 훅 포트(127.0.0.1)로 가짜 이벤트나 지시 대기를 보내지 못하게 하는 로컬 인증 토큰.
    /// Claude 설정 폴더(.claude/.claudeoffice-token)에 두고 훅 명령 헤더(X-ClaudeOffice-Token)와 수신기(HookServer)가 대조한다.
    /// </summary>
    public static string Token
    {
        get
        {
            var dir = TranscriptWatcher.ConfigDir();
            lock (CacheLock)
            {
                if (_token != null && _tokenDir == dir) return _token;
                var path = Path.Combine(dir, ".claudeoffice-token");
                try
                {
                    if (File.Exists(path))
                    {
                        var saved = File.ReadAllText(path).Trim();
                        if (saved.Length == 32 && saved.All(Uri.IsHexDigit))
                        {
                            _tokenDir = dir;
                            return _token = saved;
                        }
                    }
                    Directory.CreateDirectory(dir);
                    var created = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
                    File.WriteAllText(path, created, new UTF8Encoding(false));
                    _tokenDir = dir;
                    return _token = created;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _tokenDir = dir;
                    return _token ??= Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
                }
            }
        }
    }

    public static bool ValidToken(string? candidate)
    {
        if (string.IsNullOrEmpty(candidate)) return false;
        var expected = Encoding.ASCII.GetBytes(Token);
        var actual = Encoding.UTF8.GetBytes(candidate);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    public static string Command =>
        $"curl -s -o /dev/null -m 1 -H \"Content-Type: application/json\" -H \"X-ClaudeOffice-Token: {Token}\" --data-binary @- http://127.0.0.1:{HookServer.Port}/hook 2>/dev/null; exit 0 {Marker}";

    /// <summary>
    /// Git Bash 가 없는 PC용. 표준 입력을 바이트 그대로 보낸다(Windows PowerShell 은 표준 입력을 시스템 코드 페이지로 읽어 한글이 깨지므로 글자로 읽지 않는다).
    /// 실패해도 늘 exit 0 — 승인 요청 훅이 실패하면 Claude Code 가 도구 사용을 막을 수 있다.
    /// </summary>
    public static string PowerShellCommand =>
        "try { $m = New-Object IO.MemoryStream; [Console]::OpenStandardInput().CopyTo($m); " +
        $"Invoke-WebRequest -UseBasicParsing -Method Post -Uri http://127.0.0.1:{HookServer.Port}/hook -ContentType 'application/json' -Headers @{{'X-ClaudeOffice-Token'='{Token}'}} -Body $m.ToArray() -TimeoutSec 2 | Out-Null " +
        $"}} catch {{}}; exit 0 {Marker}";

    /// <summary>원격 승인(bash): 응답(결정 JSON 이나 빈 것)을 그대로 표준 출력으로. 꺼져 있으면 아무것도 안 나와 평소 승인 창.</summary>
    private static string RemoteApproveBash =>
        $"curl -s -m {ApproveTimeout - 10} -H \"Content-Type: application/json\" -H \"X-ClaudeOffice-Token: {Token}\" --data-binary @- http://127.0.0.1:{HookServer.Port}/remote/approve 2>/dev/null; exit 0 {RemoteMarker}";

    /// <summary>
    /// 원격 지시 대기(bash): 지시가 오면 stderr 로 내보내고 종료 코드 2(asyncRewake → 세션이 깨어 그 글을 받음). 빈 답이면 조용히 끝.
    /// 이 프로그램이 꺼져 있거나 기다리던 중 끊기면(업데이트로 껐다 켬 등) 15초마다 다시 붙는다 — 쉬는 세션은 다음 답까지 새 훅이 없으니
    /// 여기서 포기하면 휴대폰 지시를 영영 못 받는다. 입력은 한 번만 오니 먼저 담아 둔다.
    /// 꺼진 동안 답할 때마다 기다리는 프로세스가 쌓이지 않게, 세션마다 임시 폴더에 가장 새 훅의 표시를 두고 밀려난 훅은 끝낸다.
    /// </summary>
    private static string RemoteListenBash =>
        "in=$(cat); sid=$(printf '%s' \"$in\" | grep -o '\"session_id\" *: *\"[0-9A-Za-z_-]*\"' | head -n 1 | tr -d '\" ' | cut -d: -f2); [ -n \"$sid\" ] || sid=x$$; " +
        "f=\"${TMPDIR:-/tmp}/claudeoffice-listen-$sid\"; me=\"$$.$RANDOM\"; printf '%s' \"$me\" > \"$f\" 2>/dev/null; " +
        "trap '[ \"$(cat \"$f\" 2>/dev/null)\" = \"$me\" ] && rm -f \"$f\"' EXIT; while :; do " +
        $"out=$(printf '%s' \"$in\" | curl -sf -m {ListenTimeout - 400} -H \"Content-Type: application/json\" -H \"X-ClaudeOffice-Token: {Token}\" --data-binary @- http://127.0.0.1:{HookServer.Port}/remote/listen 2>/dev/null); " +
        "if [ $? -eq 0 ]; then [ -n \"$out\" ] && { printf '%s' \"$out\" >&2; exit 2; }; sleep 3; [ \"$(cat \"$f\" 2>/dev/null)\" = \"$me\" ] || exit 0; continue; fi; " +
        "sleep 15; [ \"$(cat \"$f\" 2>/dev/null)\" = \"$me\" ] || exit 0; done; " +
        $"exit 0 {RemoteMarker}";

    /// <summary>원격 승인(PowerShell). 결정 JSON 은 ASCII 라 콘솔 코드 페이지와 상관없다.</summary>
    private static string RemoteApprovePowerShell =>
        "try { $m = New-Object IO.MemoryStream; [Console]::OpenStandardInput().CopyTo($m); " +
        $"$r = Invoke-WebRequest -UseBasicParsing -Method Post -Uri http://127.0.0.1:{HookServer.Port}/remote/approve -ContentType 'application/json' -Headers @{{'X-ClaudeOffice-Token'='{Token}'}} -Body $m.ToArray() -TimeoutSec {ApproveTimeout - 10}; " +
        $"if ($r.Content) {{ [Console]::Out.Write($r.Content) }} }} catch {{}}; exit 0 {RemoteMarker}";

    /// <summary>
    /// 원격 지시 대기(PowerShell). 지시 글은 받은 바이트(UTF-8) 그대로 stderr 에 쓴다(글자로 바꾸면 한글이 깨진다).
    /// 다시 붙기·밀려나면 끝내기는 bash 와 같다.
    /// </summary>
    private static string RemoteListenPowerShell =>
        "$m = New-Object IO.MemoryStream; [Console]::OpenStandardInput().CopyTo($m); $b = $m.ToArray(); " +
        "$sid = if ([Text.Encoding]::UTF8.GetString($b) -match '\"session_id\"\\s*:\\s*\"([0-9A-Za-z_-]+)\"') { $Matches[1] } else { \"x$PID\" }; " +
        "$f = Join-Path $env:TEMP \"claudeoffice-listen-$sid\"; $me = \"$PID.$(Get-Random)\"; try { [IO.File]::WriteAllText($f, $me) } catch {}; " +
        "$mine = { try { [IO.File]::ReadAllText($f) -eq $me } catch { $false } }; try { while ($true) { try { " +
        $"$r = Invoke-WebRequest -UseBasicParsing -Method Post -Uri http://127.0.0.1:{HookServer.Port}/remote/listen -ContentType 'application/json' -Headers @{{'X-ClaudeOffice-Token'='{Token}'}} -Body $b -TimeoutSec {ListenTimeout - 400}; " +
        "$o = $r.RawContentStream.ToArray(); if ($o.Length -gt 0) { $e = [Console]::OpenStandardError(); $e.Write($o, 0, $o.Length); $e.Flush(); exit 2 }; Start-Sleep -Seconds 3; if (-not (& $mine)) { exit 0 } " +
        "} catch { Start-Sleep -Seconds 15; if (-not (& $mine)) { exit 0 } } } } " +
        "finally { if (& $mine) { Remove-Item -LiteralPath $f -ErrorAction SilentlyContinue } }; " +
        $"exit 0 {RemoteMarker}";

    /// <summary>
    /// 도구 실행 전 확인(bash): 긴급 중단 플래그가 서 있으면 중단 안내를 stderr 로 내보내고 종료 코드 2(도구 실행 즉시 차단).
    /// 꺼져 있거나 중단 플래그가 없으면 exit 0 으로 조용히 진행.
    /// </summary>
    private static string RemotePreToolBash =>
        $"out=$(curl -sf -m 2 -H \"Content-Type: application/json\" -H \"X-ClaudeOffice-Token: {Token}\" --data-binary @- http://127.0.0.1:{HookServer.Port}/remote/pretool 2>/dev/null); " +
        "if [ $? -eq 0 ] && [ -n \"$out\" ]; then printf '%s' \"$out\" >&2; exit 2; fi; " +
        $"exit 0 {RemoteMarker}";

    /// <summary>
    /// 도구 실행 전 확인(PowerShell). 한글 안내 바이트(UTF-8) 그대로 stderr 에 쓰고 exit 2.
    /// </summary>
    private static string RemotePreToolPowerShell =>
        "try { $m = New-Object IO.MemoryStream; [Console]::OpenStandardInput().CopyTo($m); $b = $m.ToArray(); " +
        $"$r = Invoke-WebRequest -UseBasicParsing -Method Post -Uri http://127.0.0.1:{HookServer.Port}/remote/pretool -ContentType 'application/json' -Headers @{{'X-ClaudeOffice-Token'='{Token}'}} -Body $b -TimeoutSec 2; " +
        "$o = $r.RawContentStream.ToArray(); if ($o.Length -gt 0) { $e = [Console]::OpenStandardError(); $e.Write($o, 0, $o.Length); $e.Flush(); exit 2 } " +
        $"}} catch {{}}; exit 0 {RemoteMarker}";

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

    /// <summary>훅이 모두 들어 있는지. 감시 루프가 몇 초마다 부르므로 파일이 바뀌었을 때만 다시 읽는다.</summary>
    public static bool IsInstalled() => Status().Monitor;

    /// <summary>자리 비움 원격 훅이 모두 들어 있는지.</summary>
    public static bool IsRemoteInstalled() => Status().Remote;

    private static (bool Monitor, bool Remote) Status()
    {
        var fi = new FileInfo(SettingsPath);
        lock (CacheLock)
        {
            if (!fi.Exists) return _cached = (false, false);
            if (fi.LastWriteTimeUtc == _cachedWriteUtc) return _cached;
            try
            {
                var (root, _) = Load();
                var hooks = root["hooks"] as JsonObject;
                bool Has(string ev, Func<JsonNode?, bool> kind) =>
                    hooks?[ev] is JsonArray groups && groups.Any(g => g is JsonObject o && o["hooks"] is JsonArray hs && hs.Any(kind));
                _cached = (Events.All(e => Has(e.Event, IsMonitor)), RemoteEvents.All(e => Has(e.Event, IsRemote)));
                _cachedWriteUtc = fi.LastWriteTimeUtc;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {
                _cached = (false, false);   // 쓰는 중이거나 깨진 파일 — 다음에 다시
            }
            return _cached;
        }
    }

    /// <summary>알림 훅을 넣는다(전에 넣은 것은 빼고 다시 — 중복 방지). 원격 훅은 그대로 둔다.</summary>
    public static void Install()
    {
        var (root, fmt) = Load();
        InstallMonitorCore(HooksOf(root));
        Save(root, fmt);
    }

    private static void InstallMonitorCore(JsonObject hooks)
    {
        Remove(hooks, IsMonitor);
        bool bash = FindGitBash() != null;
        foreach (var (ev, isAsync) in Events)
        {
            var handler = bash
                ? new JsonObject { ["type"] = "command", ["command"] = Command }
                : new JsonObject { ["type"] = "command", ["command"] = PowerShellCommand, ["shell"] = "powershell" };
            if (isAsync) handler["async"] = true;
            Add(hooks, ev, handler);
        }
    }

    /// <summary>자리 비움 원격 훅을 넣는다. 세션 종료로 정리하는 데 알림 훅도 쓰므로 함께 넣는다(한 번에 저장해 원본 .bak 보존).</summary>
    public static void InstallRemote()
    {
        var (root, fmt) = Load();
        var hooks = HooksOf(root);
        InstallMonitorCore(hooks);
        Remove(hooks, IsRemote);
        bool bash = FindGitBash() != null;
        foreach (var (ev, kind) in RemoteEvents)
        {
            bool listen = kind == "listen";
            bool pretool = kind == "pretool";
            var handler = new JsonObject
            {
                ["type"] = "command",
                ["command"] = (bash, kind) switch
                {
                    (true, "listen") => RemoteListenBash,
                    (true, "approve") => RemoteApproveBash,
                    (true, "pretool") => RemotePreToolBash,
                    (false, "listen") => RemoteListenPowerShell,
                    (false, "approve") => RemoteApprovePowerShell,
                    (false, "pretool") => RemotePreToolPowerShell,
                    _ => RemoteApprovePowerShell,
                },
            };
            if (!bash) handler["shell"] = "powershell";
            if (listen) handler["asyncRewake"] = true;
            handler["timeout"] = listen ? ListenTimeout : (pretool ? 5 : ApproveTimeout);
            Add(hooks, ev, handler);
        }
        Save(root, fmt);
    }

    /// <summary>
    /// 예전 버전이 넣은 훅 명령(원격·알림)을 지금 것으로 바꾼다(앱을 켤 때). 사용자가 이미 연결해 둔 우리 줄만 고치므로 따로 묻지 않는다.
    /// 이 포트를 쓰는 훅일 때만 — 점검용 포트(CLAUDEOFFICE_HOOK_PORT)로 켠 시험 앱이 실제 연결을 바꾸지 않게. 바꿨으면 true.
    /// </summary>
    public static bool UpgradeRemote()
    {
        var (mon, rem) = Status();
        var (root, _) = Load();
        if (root["hooks"] is not JsonObject hooks) return false;
        var commands = hooks.Select(kv => kv.Value).OfType<JsonArray>()
            .SelectMany(groups => groups.OfType<JsonObject>()).Select(g => g["hooks"]).OfType<JsonArray>()
            .SelectMany(handlers => handlers).Where(IsOurs).Select(CommandOf).ToList();
        bool hasAnyRemote = hooks.Select(kv => kv.Value).OfType<JsonArray>()
            .SelectMany(groups => groups.OfType<JsonObject>()).Select(g => g["hooks"]).OfType<JsonArray>()
            .SelectMany(handlers => handlers).Any(IsRemote);
        if (!mon && !rem && !hasAnyRemote) return false;
        var current = new[] { Command, PowerShellCommand, RemoteListenBash, RemoteApproveBash, RemoteListenPowerShell, RemoteApprovePowerShell, RemotePreToolBash, RemotePreToolPowerShell };
        var port = $"127.0.0.1:{HookServer.Port}/";
        if (rem && commands.All(c => current.Contains(c)) && commands.All(c => c?.Contains(port, StringComparison.Ordinal) == true)) return false;
        if (!hasAnyRemote && mon && commands.All(c => current.Contains(c)) && commands.All(c => c?.Contains(port, StringComparison.Ordinal) == true)) return false;
        if (hasAnyRemote || rem) InstallRemote(); else Install();
        return true;
    }

    /// <summary>우리가 넣은 훅을 모두(알림·원격) 뺀다.</summary>
    public static void Uninstall() => RemoveWhere(IsOurs);

    /// <summary>원격 훅만 뺀다(알림 훅은 그대로).</summary>
    public static void UninstallRemote() => RemoveWhere(IsRemote);

    private static void RemoveWhere(Func<JsonNode?, bool> which)
    {
        var (root, fmt) = Load();
        if (root["hooks"] is not JsonObject hooks || !Remove(hooks, which)) return;
        if (hooks.Count == 0) root.Remove("hooks");
        Save(root, fmt);
    }

    private static JsonObject HooksOf(JsonObject root)
    {
        if (root["hooks"] is JsonObject hooks) return hooks;
        if (root["hooks"] != null) throw new InvalidDataException(L.T("settings.json 의 hooks 항목 형식을 알 수 없습니다.", "The hooks entry in settings.json has an unknown format."));
        hooks = new JsonObject();
        root["hooks"] = hooks;
        return hooks;
    }

    private static void Add(JsonObject hooks, string ev, JsonObject handler)
    {
        if (hooks[ev] is not JsonArray groups)
        {
            if (hooks[ev] != null) throw new InvalidDataException(L.T($"settings.json 의 hooks.{ev} 형식을 알 수 없습니다.", $"hooks.{ev} in settings.json has an unknown format."));
            groups = new JsonArray();
            hooks[ev] = groups;
        }
        groups.Add(new JsonObject { ["hooks"] = new JsonArray(handler) });   // matcher 생략 = 전부
    }

    /// <summary>고른 처리기만 빼고, 그 때문에 비게 된 묶음·이벤트도 지운다. 다른 훅은 건드리지 않는다.</summary>
    private static bool Remove(JsonObject hooks, Func<JsonNode?, bool> which)
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
                    if (which(handlers[j])) handlers.RemoveAt(j);
                if (handlers.Count == before) continue;
                removedHere = true;
                if (handlers.Count == 0) groups.RemoveAt(i);
            }
            if (removedHere && groups.Count == 0) hooks.Remove(ev);
            changed |= removedHere;
        }
        return changed;
    }

    private static string? CommandOf(JsonNode? handler) =>
        handler is JsonObject h && h["command"] is JsonValue v && v.TryGetValue<string>(out var cmd) ? cmd : null;

    private static bool IsOurs(JsonNode? handler) => CommandOf(handler)?.Contains(Marker, StringComparison.Ordinal) == true;
    private static bool IsRemote(JsonNode? handler) => CommandOf(handler)?.Contains(RemoteMarker, StringComparison.Ordinal) == true;
    private static bool IsMonitor(JsonNode? handler) => IsOurs(handler) && !IsRemote(handler);

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
