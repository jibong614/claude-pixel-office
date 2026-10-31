using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using ClaudeOffice.Hooks;
using ClaudeOffice.Monitoring;

namespace ClaudeOffice.Remote;

/// <summary>
/// 자리 비움 원격: 휴대폰에서 보낸 지시·승인을 Claude Code 세션에 전한다. Claude 로그인은 PC 에만 있으면 된다.
///
/// 길은 Claude Code 훅 둘(HookSetup.InstallRemote 가 넣는다)
///   listen  : Stop·SessionStart 의 asyncRewake 훅. 뒤에서 이 프로그램에 붙어 기다리다가 지시가 오면 그 글을 돌려받고
///             종료 코드 2 로 끝난다 → Claude Code 가 쉬고 있던 세션을 깨워 그 글을 사용자 메시지로 처리한다.
///             세션마다 하나만 붙잡아 둔다(새로 오면 옛것은 빈 답으로 놓아준다).
///   approve : PermissionRequest 의 동기 훅. 자리 비움일 때만 휴대폰의 승인·거절을 기다렸다가 결정(JSON)을 돌려준다.
///             자리 비움이 아니거나, 돌아왔거나, 오래 답이 없으면 빈 답 → 평소처럼 PC 에 승인 창이 뜬다.
/// 원격 훅을 빼면 두 훅 모두 바로 아무 일 없이 끝난다. 이 프로그램이 꺼져 있으면 approve 는 바로 끝나고(평소 승인 창),
/// listen 은 켜질 때까지 15초마다 다시 붙어 본다(세션마다 가장 새 것 하나만 — HookSetup.RemoteListenBash).
/// </summary>
internal sealed class RemoteHub
{
    /// <summary>휴대폰 승인을 기다리는 최대 시간(훅 시간 제한 3600초 안). 지나면 PC 승인 창으로.</summary>
    public static readonly TimeSpan ApprovalHold = TimeSpan.FromMinutes(55);

    /// <summary>Claude 에게 건네는 지시 맨 앞 표시. 기록 파일에서 휴대폰 지시를 알아보는 데도 쓴다(TranscriptParser).</summary>
    public const string TagKo = "[Claude Office 원격]", TagEn = "[Claude Office remote]";

    public const int MaxText = 1000;   // 지시·거절 이유 글자 수(데이터베이스 규칙의 암호문 크기 안)
    private const int MaxReply = 3000;
    private const long LogMaxBytes = 1024 * 1024;
    private static readonly TimeSpan MsgMaxAge = TimeSpan.FromMinutes(10);   // 이보다 오래된(또는 시계가 이만큼 앞선) 지시는 버린다

    private static readonly JsonSerializerOptions JsonOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly object _lock = new();
    private readonly string? _logPath;
    private readonly Dictionary<string, Listener> _listeners = new(StringComparer.Ordinal);    // 세션 → 기다리는 listen 훅
    private readonly Dictionary<string, Approval> _approvals = new(StringComparer.Ordinal);    // 요청 번호 → 휴대폰 답을 기다리는 승인
    private readonly Dictionary<string, List<string>> _queued = new(StringComparer.Ordinal);   // 세션 → 아직 못 건넨 지시(세션이 다시 붙으면 바로)
    private readonly Dictionary<string, Said> _said = new(StringComparer.Ordinal);             // 세션 → 마지막 지시와 그 처리 상태
    private readonly Dictionary<string, Said> _replies = new(StringComparer.Ordinal);          // 세션 → Claude 의 마지막 답(Stop 훅이 알려 줌)
    private readonly Dictionary<string, DateTime> _nonces = new(StringComparer.Ordinal);       // 이미 처리한 휴대폰 메시지(같은 것을 두 번 받지 않게)
    private bool _away;
    private DateTime _awaySinceUtc;
    private bool _pcIdle;      // PC 를 아무도 안 쓰는 중(입력 없음·화면 잠금) — 이때만 휴대폰이 자리 비움을 켤 수 있다
    private int _autoMinutes;  // 입력이 없으면 몇 분 뒤 저절로 자리 비움(0 = 끔) — 휴대폰 안내 글용

    public RemoteHub(string? logPath) => _logPath = logPath;

    /// <summary>원격 상태가 바뀔 때마다(아무 스레드에서나).</summary>
    public event Action? Changed;

    /// <summary>원격 훅이 Claude Code 설정에 들어 있는지 — 없으면 원격은 꺼진 것.</summary>
    public static bool Installed => HookSetup.IsRemoteInstalled();

    public bool Away { get { lock (_lock) return _away; } }

    private sealed record Listener(string Sid, TaskCompletionSource<string?> Tcs, DateTime SinceUtc);

    private sealed record Approval(string Id, string Sid, string Text, string? Detail, DateTime SinceUtc, TaskCompletionSource<string?> Tcs);

    private sealed record Said(string Text, DateTime Utc, string State);   // State: queued | delivered | dropped

    // ───────────────────────── 훅 쪽 ─────────────────────────

    /// <summary>훅 수신기가 /remote/&lt;kind&gt; 요청을 넘긴다. 돌려준 바이트가 응답 본문(빈 것이면 "할 일 없음"). gone: 훅 쪽 연결이 끊김.</summary>
    public Task<byte[]> HandleAsync(string kind, byte[] body, CancellationToken gone) => kind switch
    {
        "listen" => ListenAsync(body, gone),
        "approve" => ApproveAsync(body, gone),
        _ => Task.FromResult(Array.Empty<byte>()),
    };

    private async Task<byte[]> ListenAsync(byte[] body, CancellationToken gone)
    {
        if (!Installed || ParseHook(body) is not { } h) return [];
        Listener me;
        string? now = null;
        lock (_lock)
        {
            if (h.Event == "Stop" && !string.IsNullOrWhiteSpace(h.LastMessage))
                _replies[h.Sid] = new Said(ToolCatalog.Clip(h.LastMessage.Trim(), MaxReply), DateTime.UtcNow, "reply");

            // 이 세션에 못 건넨 지시가 있으면 기다리지 않고 바로 — 일하는 중에 보낸 것은 일을 마치는 순간 이어서 받는다
            if (_queued.Remove(h.Sid, out var waiting) && waiting.Count > 0)
            {
                now = string.Join("\n\n", waiting);
                _said[h.Sid] = new Said(now, DateTime.UtcNow, "delivered");
                Log("deliver", h.Sid, now);
            }

            me = new Listener(h.Sid, new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously), DateTime.UtcNow);
            if (_listeners.Remove(h.Sid, out var old)) old.Tcs.TrySetResult(null);   // 같은 세션의 옛 훅은 놓아준다
            if (now is null) _listeners[h.Sid] = me;
        }
        Notify();
        if (now != null) return Encoding.UTF8.GetBytes(Instruction(now));

        string? result;
        try { result = await me.Tcs.Task.WaitAsync(gone); }
        catch (OperationCanceledException) { result = null; }

        bool changed = false;
        lock (_lock)
        {
            if (_listeners.TryGetValue(h.Sid, out var cur) && cur == me) { _listeners.Remove(h.Sid); changed = true; }
        }
        if (changed) Notify();
        return result is null ? [] : Encoding.UTF8.GetBytes(result);
    }

    private async Task<byte[]> ApproveAsync(byte[] body, CancellationToken gone)
    {
        if (!Installed || ParseHook(body) is not { } h || h.Tool is null) return [];
        if (h.Tool == "AskUserQuestion") return [];   // 질문은 답을 골라야 해서 휴대폰으로 넘기지 않는다
        Approval me;
        lock (_lock)
        {
            if (!_away) return [];
            var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(5)).ToLowerInvariant();
            me = new Approval(id, h.Sid, h.ToolText ?? h.Tool, h.Detail, DateTime.UtcNow,
                new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously));
            _approvals[id] = me;
            Log("ask", h.Sid, me.Detail ?? me.Text);
        }
        Notify();

        string? decision;
        try { decision = await me.Tcs.Task.WaitAsync(ApprovalHold, gone); }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException) { decision = null; }

        lock (_lock) _approvals.Remove(me.Id);
        Notify();
        return decision is null ? [] : Encoding.ASCII.GetBytes(decision);
    }

    // ───────────────────────── 휴대폰·PC 화면 쪽 ─────────────────────────

    /// <summary>휴대폰이 보낸 것(해독해 둔 것)을 처리한다. 올리기 스레드에서 불린다.</summary>
    public void FromPhone(IEnumerable<RemoteMsg> msgs)
    {
        foreach (var m in msgs)
        {
            if (!Fresh(m)) continue;
            switch (m.Kind)
            {
                case "say" when m.Sid != null && !string.IsNullOrWhiteSpace(m.Text): Say(m.Sid, m.Text); break;
                case "ok" when m.Req != null: Decide(m.Req, allow: true, null); break;
                case "no" when m.Req != null: Decide(m.Req, allow: false, m.Text); break;
                case "away": AwayFromPhone(); break;
            }
        }
    }

    /// <summary>
    /// PC 가 알려 주는 자리 상태(MainWindow 가 몇 초마다): idle = 아무도 안 쓰는 중(입력 없음·화면 잠금), autoMinutes = 저절로 자리 비움까지.
    /// 휴대폰은 PC 가 idle 일 때만 자리 비움을 켤 수 있다 — 누가 PC 를 쓰는 동안에는 휴대폰 지시가 끼어들지 않게.
    /// </summary>
    public void SetPresence(bool idle, int autoMinutes)
    {
        lock (_lock)
        {
            if (_pcIdle == idle && _autoMinutes == autoMinutes) return;
            _pcIdle = idle;
            _autoMinutes = autoMinutes;
        }
        Notify();
    }

    /// <summary>휴대폰이 화면을 보고 있다며 자리 비움을 부탁함(휴대폰 화면이 열려 있고 PC 가 비어 있을 때 저절로 보냄) — PC 를 아무도 안 쓰고 있을 때만.</summary>
    private void AwayFromPhone()
    {
        lock (_lock)
        {
            if (_away) return;
            if (!_pcIdle) { Log("away-refused", "-", "phone (PC in use)"); return; }
            TurnAwayOn("phone");
        }
        Notify();
    }

    private void TurnAwayOn(string why)   // _lock 안에서
    {
        _away = true;
        _awaySinceUtc = DateTime.UtcNow;
        Log("away-on", "-", why);
    }

    /// <summary>오래됐거나 이미 받은 메시지는 버린다(휴대폰 시계가 조금 틀려도 되게 앞뒤 10분).</summary>
    private bool Fresh(RemoteMsg m)
    {
        var now = DateTime.UtcNow;
        var t = DateTimeOffset.FromUnixTimeMilliseconds(Math.Clamp(m.T, 0, 253402300799999)).UtcDateTime;
        if (string.IsNullOrEmpty(m.N) || (now - t).Duration() > MsgMaxAge) return false;
        lock (_lock)
        {
            foreach (var old in _nonces.Where(kv => now - kv.Value > MsgMaxAge * 3).Select(kv => kv.Key).ToList()) _nonces.Remove(old);
            return _nonces.TryAdd(m.N, now);
        }
    }

    private void Say(string sid, string text, int max = MaxText)
    {
        text = text.Trim();
        if (text.Length > max) text = text[..max];
        lock (_lock)
        {
            // 자리 비움이 아니어도 PC 를 아무도 안 쓰고 있으면 보내는 순간 켠다(PC 에서 따로 켜지 않아도 되게)
            if (!_away && _pcIdle) TurnAwayOn("phone-say");
            if (!_away)
            {
                _said[sid] = new Said(text, DateTime.UtcNow, "dropped");
                Log("drop", sid, text);
            }
            else if (_listeners.Remove(sid, out var l))
            {
                l.Tcs.TrySetResult(Instruction(text));
                _said[sid] = new Said(text, DateTime.UtcNow, "delivered");
                Log("deliver", sid, text);
            }
            else
            {
                // 지금 일하는 중이거나 아직 훅이 붙지 않은 세션 — 일을 마치고 다시 붙는 순간 건넨다
                if (!_queued.TryGetValue(sid, out var list)) _queued[sid] = list = [];
                if (list.Count < 5) list.Add(text);
                _said[sid] = new Said(string.Join("\n\n", list), DateTime.UtcNow, "queued");
                Log("queue", sid, text);
            }
        }
        Notify();
    }

    /// <summary>
    /// 승인·거절. 거절하며 "대신 할 일"을 썼으면 그 글은 거절 응답(도구 결과)에 싣지 않고 지시와 같은 길로 따로 보낸다 —
    /// Claude 는 도구 결과 속 지시를 사용자의 말로 믿지 않는다(프롬프트 주입 방어). 깨우는 훅의 글은 사용자 메시지로 받는다.
    /// </summary>
    private void Decide(string id, bool allow, string? text)
    {
        string? follow = null, sid;
        lock (_lock)
        {
            if (!_approvals.Remove(id, out var a)) return;   // 이미 끝났거나 PC 에서 처리함
            text = string.IsNullOrWhiteSpace(text) ? null : text.Trim() is { Length: > MaxText } t ? t[..MaxText] : text.Trim();
            a.Tcs.TrySetResult(allow ? AllowJson() : DenyJson(followUp: text != null));
            Log(allow ? "allow" : "deny", a.Sid, (a.Detail ?? a.Text) + (text != null ? " — " + text : ""));
            sid = a.Sid;
            if (!allow && text != null)
            {
                var what = ToolCatalog.Clip(a.Detail ?? a.Text, 200);
                follow = L.T($"방금 휴대폰에서 거절한 요청({what}) 대신 이렇게 해 주세요:\n{text}",
                             $"Instead of the request I just denied from the phone ({what}), do this:\n{text}");
            }
        }
        Notify();
        if (follow != null) Say(sid, follow, MaxText + 300);
    }

    /// <summary>
    /// 자리 비움 켜기·끄기(PC 쪽: 화면 잠금·입력 없음·돌아옴, 원격 해제). 휴대폰은 AwayFromPhone·Say 로 PC 가 비어 있을 때만 켠다.
    /// 끄면 휴대폰을 기다리던 승인은 모두 PC 승인 창으로, 못 건넨 지시는 버린다.
    /// </summary>
    public void SetAway(bool on, string why)
    {
        lock (_lock)
        {
            if (_away == on) return;
            if (on) TurnAwayOn(why);
            else
            {
                _away = false;
                _awaySinceUtc = default;
                foreach (var a in _approvals.Values) a.Tcs.TrySetResult(null);
                _approvals.Clear();
                foreach (var (sid, list) in _queued) _said[sid] = new Said(string.Join("\n\n", list), DateTime.UtcNow, "dropped");
                _queued.Clear();
                Log("away-off", "-", why);
            }
        }
        Notify();
    }

    /// <summary>Claude Code 연결을 끊음 — 붙잡아 둔 지시 대기 훅을 모두 빈 답으로 놓아준다(다시 붙지 않고 끝난다).</summary>
    public void ReleaseListeners()
    {
        lock (_lock)
        {
            foreach (var l in _listeners.Values) l.Tcs.TrySetResult(null);
            _listeners.Clear();
        }
        Notify();
    }

    /// <summary>세션을 닫음(SessionEnd 훅) — 붙잡아 둔 훅·못 건넨 지시를 정리한다.</summary>
    public void OnSessionEnd(string sid)
    {
        lock (_lock)
        {
            if (_listeners.Remove(sid, out var l)) l.Tcs.TrySetResult(null);
            _queued.Remove(sid);
            _said.Remove(sid);
            _replies.Remove(sid);
        }
        Notify();
    }

    /// <summary>
    /// 화면·휴대폰에 보낼 상태: {installed, away, since, idle, auto, s:{세션:{listen, asks:[{id,text,detail,t}], said:{text,t,state}, reply:{text,t}}}}.
    /// idle: PC 를 아무도 안 쓰는 중(휴대폰이 자리 비움을 켤 수 있음), auto: 입력이 없으면 몇 분 뒤 저절로 자리 비움(0 = 끔).
    /// 세션 상태는 원격이 켜져 있을 때만 싣는다(Claude 의 답 글이 들어 있어서).
    /// </summary>
    public string StateJson()
    {
        bool installed = Installed;
        lock (_lock)
        {
            var s = new Dictionary<string, object>();
            if (installed)
            {
                var ids = _listeners.Keys.Concat(_approvals.Values.Select(a => a.Sid)).Concat(_said.Keys).Concat(_replies.Keys).ToHashSet(StringComparer.Ordinal);
                foreach (var sid in ids)
                {
                    var asks = _approvals.Values.Where(a => a.Sid == sid).OrderBy(a => a.SinceUtc)
                        .Select(a => new { id = a.Id, text = a.Text, detail = a.Detail, t = SnapshotBuilder.Ms(a.SinceUtc) }).ToList();
                    s[sid] = new
                    {
                        listen = _listeners.ContainsKey(sid),
                        asks,
                        said = _said.TryGetValue(sid, out var said) ? new { text = said.Text, t = SnapshotBuilder.Ms(said.Utc), state = said.State } : null,
                        reply = _replies.TryGetValue(sid, out var r) ? new { text = r.Text, t = SnapshotBuilder.Ms(r.Utc) } : null,
                    };
                }
            }
            return JsonSerializer.Serialize(new { installed, away = _away && installed, since = SnapshotBuilder.Ms(_awaySinceUtc),
                                                  idle = _pcIdle && installed, auto = _autoMinutes, s }, JsonOptions);
        }
    }

    // ───────────────────────── 글 만들기 ─────────────────────────

    /// <summary>listen 훅이 Claude 에게 건네는 글(종료 코드 2 의 stderr → 시스템 알림으로 Claude 에게 감).</summary>
    private static string Instruction(string text) =>
        L.T($"{TagKo} 사용자가 자리를 비운 동안 휴대폰에서 보낸 지시입니다. 사용자의 메시지로 받아 처리해 주세요.\n\n{text}",
            $"{TagEn} The user sent this from their phone while away from the PC. Treat it as the user's message.\n\n{text}");

    /// <summary>기록 파일의 메시지에서 휴대폰 지시 글만 뽑는다(없으면 null).</summary>
    public static string? ExtractInstruction(string raw)
    {
        int at = raw.IndexOf(TagKo, StringComparison.Ordinal);
        if (at < 0) at = raw.IndexOf(TagEn, StringComparison.Ordinal);
        if (at < 0) return null;
        int start = raw.IndexOf("\n\n", at, StringComparison.Ordinal);
        if (start < 0) return null;
        start += 2;
        int end = raw.IndexOf("</system-reminder>", start, StringComparison.Ordinal);
        var text = (end < 0 ? raw[start..] : raw[start..end]).Trim();
        return text.Length > 0 ? text : null;
    }

    // PermissionRequest 훅 응답. 글자는 모두 \uXXXX 로(ASCII) — PowerShell 훅이 콘솔 코드 페이지로 내보내도 깨지지 않게
    private static string AllowJson() =>
        """{"hookSpecificOutput":{"hookEventName":"PermissionRequest","decision":{"behavior":"allow"}}}""";

    private static string DenyJson(bool followUp)
    {
        var message = !followUp
            ? L.T("사용자가 휴대폰(Claude Office 원격)에서 이 요청을 거절했습니다.", "The user denied this from their phone (Claude Office remote).")
            : L.T("사용자가 휴대폰(Claude Office 원격)에서 이 요청을 거절했습니다. 대신 할 일은 사용자가 따로 보냅니다 — 다른 방법을 시도하지 말고 이번 응답을 마친 뒤 그 메시지를 기다려 주세요.",
                  "The user denied this from their phone (Claude Office remote). They will send what to do instead as a separate message — don't try another way; end this response and wait for it.");
        return JsonSerializer.Serialize(new
        {
            hookSpecificOutput = new { hookEventName = "PermissionRequest", decision = new { behavior = "deny", message, interrupt = false } },
        });   // 기본 인코더는 ASCII 밖의 글자를 \uXXXX 로 쓴다
    }

    // ───────────────────────── 그 밖 ─────────────────────────

    private sealed record HookInput(string Event, string Sid, string? Tool, string? ToolText, string? Detail, string? LastMessage);

    private static HookInput? ParseHook(byte[] body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var r = doc.RootElement;
            var ev = ToolCatalog.Str(r, "hook_event_name");
            var sid = ToolCatalog.Str(r, "session_id");
            if (ev is null || sid is null) return null;
            var tool = ToolCatalog.Str(r, "tool_name");
            var input = r.TryGetProperty("tool_input", out var inp) ? inp : default;
            // 휴대폰에서 무엇을 허락하는지 알 수 있게: 명령은 그대로, 파일 도구는 경로
            var detail = tool is null ? null
                : ToolCatalog.Str(input, "command") ?? ToolCatalog.Str(input, "file_path") ?? ToolCatalog.Str(input, "url") ?? ToolCatalog.Str(input, "path");
            return new HookInput(ev, sid, tool, tool != null ? ToolCatalog.Describe(tool, input) : null,
                detail != null ? ToolCatalog.Clip(detail, 600) : null, ToolCatalog.Str(r, "last_assistant_message"));
        }
        catch (JsonException) { return null; }
    }

    private void Notify() => Changed?.Invoke();

    /// <summary>원격 기록(remote.log): 언제 무엇을 받아 어떻게 했는지 — 지시·승인을 나중에 확인할 수 있게.</summary>
    private void Log(string what, string sid, string text)
    {
        if (_logPath is null) return;
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\t{what}\t{sid}\t{text.Replace("\r", " ").Replace("\n", " ")}\r\n";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
            var fi = new FileInfo(_logPath);
            if (fi.Exists && fi.Length > LogMaxBytes) File.Move(_logPath, Path.ChangeExtension(_logPath, ".old.log"), overwrite: true);
            File.AppendAllText(_logPath, line);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}

/// <summary>
/// 휴대폰에서 보낸 원격 메시지 하나(CloudSync 가 해독한 것). Kind: say(지시 — Sid 세션에 Text) | ok(승인) | no(거절, Text 는 대신 할 일) — Req 는 승인 요청 번호.
/// T: 보낸 시각(ms), N: 한 번만 쓰는 무작위 값(같은 메시지를 두 번 처리하지 않게).
/// </summary>
internal sealed record RemoteMsg(string Kind, string? Sid, string? Req, string? Text, long T, string? N);
