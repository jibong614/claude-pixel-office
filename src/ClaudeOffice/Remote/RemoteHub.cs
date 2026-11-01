using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using ClaudeOffice.Hooks;
using ClaudeOffice.Monitoring;

namespace ClaudeOffice.Remote;

/// <summary>
/// 자리 비움 원격 및 바탕화면 미니펫(MiniPet): 휴대폰이나 PC 미니펫에서 보낸 지시·승인을 Claude Code 세션에 전한다.
///
/// 길은 Claude Code 훅 둘(HookSetup.InstallRemote 가 넣는다)
///   listen  : Stop·SessionStart 의 asyncRewake 훅. 뒤에서 이 프로그램에 붙어 기다리다가 지시가 오면 그 글을 돌려받고
///             종료 코드 2 로 끝난다 → Claude Code 가 쉬고 있던 세션을 깨워 그 글을 사용자 메시지로 처리한다.
///             세션마다 하나만 붙잡아 둔다(새로 오면 옛것은 빈 답으로 놓아준다).
///   approve : PermissionRequest 의 동기 훅. 자리 비움일 때(또는 미니펫이 켜져 그 세션을 보고 있을 때) 승인·거절을 기다렸다가 결정(JSON)을 돌려준다.
///             해당하지 않거나, 돌아왔거나, 오래 답이 없으면 빈 답 → 평소처럼 PC 에 승인 창이 뜬다.
/// 원격 훅을 빼면 두 훅 모두 바로 아무 일 없이 끝난다. 이 프로그램이 꺼져 있으면 approve 는 바로 끝나고(평소 승인 창),
/// listen 은 켜질 때까지 15초마다 다시 붙어 본다(세션마다 가장 새 것 하나만 — HookSetup.RemoteListenBash).
/// </summary>
internal sealed class RemoteHub
{
    /// <summary>휴대폰·미니펫 승인을 기다리는 최대 시간(훅 시간 제한 3600초 안). 지나면 PC 승인 창으로.</summary>
    public static readonly TimeSpan ApprovalHold = TimeSpan.FromMinutes(55);

    /// <summary>Claude 에게 건네는 지시 맨 앞 표시. 기록 파일에서 휴대폰·미니펫 지시를 알아보는 데도 쓴다(TranscriptParser).</summary>
    public const string TagKo = "[Claude Office 원격]", TagEn = "[Claude Office remote]";

    public const int MaxText = 1000;   // 지시·거절 이유 글자 수(데이터베이스 규칙의 암호문 크기 안)
    private const int MaxReply = 3000;
    private const long LogMaxBytes = 1024 * 1024;
    private static readonly TimeSpan MsgMaxAge = TimeSpan.FromMinutes(10);   // 이보다 오래된(또는 시계가 이만큼 앞선) 지시는 버린다

    private static readonly JsonSerializerOptions JsonOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly object _lock = new();
    private readonly string? _logPath;
    private readonly Dictionary<string, Listener> _listeners = new(StringComparer.Ordinal);    // 세션 → 기다리는 listen 훅
    private readonly Dictionary<string, Approval> _approvals = new(StringComparer.Ordinal);    // 요청 번호 → 답을 기다리는 승인
    private readonly Dictionary<string, List<QueuedMsg>> _queued = new(StringComparer.Ordinal); // 세션 → 아직 못 건넨 지시(세션이 다시 붙으면 바로)
    private readonly Dictionary<string, Said> _said = new(StringComparer.Ordinal);             // 세션 → 마지막 지시와 그 처리 상태
    private readonly Dictionary<string, Said> _replies = new(StringComparer.Ordinal);          // 세션 → Claude 의 마지막 답(Stop 훅이 알려 줌)
    private readonly Dictionary<string, DateTime> _nonces = new(StringComparer.Ordinal);       // 이미 처리한 휴대폰 메시지(같은 것을 두 번 받지 않게)
    private bool _away;
    private DateTime _awaySinceUtc;
    private bool _pcIdle;      // PC 를 아무도 안 쓰는 중(입력 없음·화면 잠금) — 이때만 휴대폰이 자리 비움을 켤 수 있다
    private int _autoMinutes;  // 입력이 없으면 몇 분 뒤 저절로 자리 비움(0 = 끔) — 휴대폰 안내 글용
    private bool _petOpen;     // 바탕화면 미니펫이 켜져 있는지(켜져 있으면 자리 비움이 아니어도 대표 세션의 승인을 미니펫에서 처리 가능)
    private string? _petSid;   // 미니펫 대표 세션 ID(비어 있으면 자동 선택 — 어느 세션이든 승인 대기 가능)

    public RemoteHub(string? logPath)
    {
        _logPath = logPath;
        RestoreRecentPcQueues();
    }

    /// <summary>원격 상태가 바뀔 때마다(아무 스레드에서나).</summary>
    public event Action? Changed;

    /// <summary>휴대폰에서 세션 긴급 작업 중단(Ctrl+C)을 요청했을 때 발생한다.</summary>
    public event Action<string>? StopRequested;

    /// <summary>원격 훅이 Claude Code 설정에 들어 있는지 — 없으면 원격은 꺼진 것.</summary>
    public static bool Installed => HookSetup.IsRemoteInstalled();

    public bool Away { get { lock (_lock) return _away; } }

    private sealed record Listener(string Sid, TaskCompletionSource<string?> Tcs, DateTime SinceUtc);

    private sealed record Approval(string Id, string Sid, string Kind, string Text, string? Detail, string[]? Options, DateTime SinceUtc, TaskCompletionSource<string?> Tcs);

    private sealed record QueuedMsg(string Text, bool FromPc);

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
                now = string.Join("\n\n", waiting.Select(x => x.Text));
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
        Approval me;
        lock (_lock)
        {
            if (!_away && !_petOpen) return [];
            var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(5)).ToLowerInvariant();
            var kind = h.Tool == "AskUserQuestion" ? "question" : "tool";
            me = new Approval(id, h.Sid, kind, h.ToolText ?? h.Tool, h.Detail, h.Options, DateTime.UtcNow,
                new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously));
            _approvals[id] = me;
            Log(kind == "question" ? "question" : "ask", h.Sid, me.Detail ?? me.Text);
        }
        Notify();

        string? decision;
        try { decision = await me.Tcs.Task.WaitAsync(ApprovalHold, gone); }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException) { decision = null; }

        lock (_lock) _approvals.Remove(me.Id);
        Notify();
        return decision is null ? [] : Encoding.ASCII.GetBytes(decision);
    }

    // ───────────────────────── 휴대폰·PC 미니펫·본창 상세 패널 쪽 ─────────────────────────

    /// <summary>바탕화면 미니펫 표시 여부와 대표 세션 ID 를 갱신한다. 미니펫이 켜져 있으면 모든 세션의 승인·질문을 미니펫/본창에서 처리할 수 있고, 미니펫을 끄면(자리 비움이 아닐 때) 대기 중이던 요청을 터미널로 돌려준다.</summary>
    public void SetPet(bool open, string? sid)
    {
        bool changed = false;
        lock (_lock)
        {
            var norm = string.IsNullOrWhiteSpace(sid) ? null : sid.Trim();
            if (_petOpen == open && _petSid == norm) return;
            _petOpen = open;
            _petSid = norm;
            changed = true;
            if (!_away && !_petOpen)
            {
                foreach (var (id, a) in _approvals.ToList())
                {
                    _approvals.Remove(id);
                    a.Tcs.TrySetResult(null);
                }
            }
        }
        if (changed) Notify();
    }

    /// <summary>아직 세션에 전달되지 않고 대기열(_queued)에 있는 지시를 취소한다.</summary>
    public void CancelQueued(string sid)
    {
        if (string.IsNullOrEmpty(sid)) return;
        bool changed = false;
        lock (_lock)
        {
            if (_queued.Remove(sid)) changed = true;
            if (_said.TryGetValue(sid, out var s) && s.State == "queued")
            {
                _said.Remove(sid);
                changed = true;
            }
            if (changed) Log("cancel-queue", sid, "-");
        }
        if (changed) Notify();
    }

    /// <summary>PC 바탕화면 미니펫에서 보낸 지시 — 사용자가 직접 PC 에서 입력한 것이므로 자리 비움 여부와 상관없이 바로 전달·대기열에 넣는다.</summary>
    public void SayFromPc(string sid, string text, int max = MaxText)
    {
        text = text.Trim();
        if (string.IsNullOrEmpty(sid) || string.IsNullOrEmpty(text)) return;
        if (text.Length > max) text = text[..max];
        lock (_lock)
        {
            if (_listeners.Remove(sid, out var l))
            {
                l.Tcs.TrySetResult(Instruction(text));
                _said[sid] = new Said(text, DateTime.UtcNow, "delivered");
                Log("pc-deliver", sid, text);
            }
            else
            {
                if (!_queued.TryGetValue(sid, out var list)) _queued[sid] = list = [];
                if (list.Count < 5) list.Add(new QueuedMsg(text, FromPc: true));
                _said[sid] = new Said(string.Join("\n\n", list.Select(x => x.Text)), DateTime.UtcNow, "queued");
                Log("pc-queue", sid, text);
            }
        }
        Notify();
    }

    /// <summary>PC 미니펫에서 승인(true)·거절(false)·터미널로 넘기기(null)를 눌렀을 때.</summary>
    public void DecideFromPc(string id, bool? allow, string? text)
    {
        if (allow is null)
        {
            lock (_lock)
            {
                if (!_approvals.Remove(id, out var a)) return;
                a.Tcs.TrySetResult(null);
                Log("pc-pass", a.Sid, a.Detail ?? a.Text);
            }
            Notify();
            return;
        }
        DecideCore(id, allow.Value, text, fromPc: true);
    }

    /// <summary>휴대폰이 보낸 것(해독해 둔 것)을 처리한다. 올리기 스레드에서 불린다.</summary>
    public void FromPhone(IEnumerable<RemoteMsg> msgs)
    {
        foreach (var m in msgs)
        {
            if (!Fresh(m)) continue;
            switch (m.Kind)
            {
                case "say" when m.Sid != null && !string.IsNullOrWhiteSpace(m.Text): Say(m.Sid, m.Text); break;
                case "cancel" when m.Sid != null: CancelQueued(m.Sid); break;
                case "stop" when m.Sid != null:
                    CancelQueued(m.Sid);
                    StopRequested?.Invoke(m.Sid);
                    Log("stop-phone", m.Sid, "-");
                    break;
                case "ok" when m.Req != null: DecideCore(m.Req, allow: true, m.Text, fromPc: false); break;
                case "no" when m.Req != null: DecideCore(m.Req, allow: false, m.Text, fromPc: false); break;
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
                if (list.Count < 5) list.Add(new QueuedMsg(text, FromPc: false));
                _said[sid] = new Said(string.Join("\n\n", list.Select(x => x.Text)), DateTime.UtcNow, "queued");
                Log("queue", sid, text);
            }
        }
        Notify();
    }

    /// <summary>
    /// 승인·거절·질문 응답. 거절하며 "대신 할 일"을 썼거나 객관식 질문(AskUserQuestion)에 답을 골랐으면
    /// 그 글은 도구 결과에 싣지 않고 지시와 같은 길(listen 깨우기 훅)로 따로 보낸다 —
    /// Claude 는 도구 결과 속 지시를 사용자의 말로 믿지 않는다(프롬프트 주입 방어). 깨우는 훅의 글은 사용자 메시지로 받는다.
    /// </summary>
    private void DecideCore(string id, bool allow, string? text, bool fromPc)
    {
        string? follow = null, sid;
        lock (_lock)
        {
            if (!_approvals.Remove(id, out var a)) return;   // 이미 끝났거나 PC 에서 처리함
            text = string.IsNullOrWhiteSpace(text) ? null : text.Trim() is { Length: > MaxText } t ? t[..MaxText] : text.Trim();
            sid = a.Sid;
            if (a.Kind == "question" && allow && text != null)
            {
                a.Tcs.TrySetResult(QuestionAnswerJson());
                Log("answer", a.Sid, (a.Detail ?? a.Text) + " → " + text);
                var q = ToolCatalog.Clip(a.Detail ?? a.Text, 200);
                follow = L.T($"방금 질문({q})에 대한 답입니다:\n{text}",
                             $"Here is my answer to your question ({q}):\n{text}");
            }
            else
            {
                a.Tcs.TrySetResult(allow ? AllowJson() : DenyJson(followUp: text != null));
                Log(allow ? "allow" : "deny", a.Sid, (a.Detail ?? a.Text) + (text != null ? " — " + text : ""));
                if (!allow && text != null)
                {
                    var what = ToolCatalog.Clip(a.Detail ?? a.Text, 200);
                    follow = L.T($"방금 거절한 요청({what}) 대신 이렇게 해 주세요:\n{text}",
                                 $"Instead of the request I just denied ({what}), do this:\n{text}");
                }
            }
        }
        Notify();
        if (follow != null)
        {
            if (fromPc) SayFromPc(sid, follow, MaxText + 300);
            else Say(sid, follow, MaxText + 300);
        }
    }

    /// <summary>
    /// 자리 비움 켜기·끄기(PC 쪽: 화면 잠금·입력 없음·돌아옴, 원격 해제). 휴대폰은 AwayFromPhone·Say 로 PC 가 비어 있을 때만 켠다.
    /// 끄면 휴대폰을 기다리던 승인은 모두 PC 승인 창으로(단 미니펫이 켜져 있으면 유지), 못 건넨 지시는 버린다.
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
                if (!_petOpen)
                {
                    foreach (var (id, a) in _approvals.ToList())
                    {
                        _approvals.Remove(id);
                        a.Tcs.TrySetResult(null);
                    }
                }
                if (why == "hooks-off")
                {
                    foreach (var (sid, list) in _queued)
                        _said[sid] = new Said(string.Join("\n\n", list.Select(x => x.Text)), DateTime.UtcNow, "dropped");
                    _queued.Clear();
                }
                else
                {
                    // PC로 돌아왔을 때(back·unlock)는 휴대폰에서 보낸 대기열만 취소하고, PC(미니펫·본창)에서 직접 보낸 대기 지시는 그대로 유지한다
                    foreach (var (sid, list) in _queued.ToList())
                    {
                        var dropped = list.Where(x => !x.FromPc).ToList();
                        list.RemoveAll(x => !x.FromPc);
                        if (list.Count == 0)
                        {
                            _queued.Remove(sid);
                            if (dropped.Count > 0)
                                _said[sid] = new Said(string.Join("\n\n", dropped.Select(x => x.Text)), DateTime.UtcNow, "dropped");
                        }
                        else if (dropped.Count > 0)
                        {
                            _said[sid] = new Said(string.Join("\n\n", list.Select(x => x.Text)), DateTime.UtcNow, "queued");
                        }
                    }
                }
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
                        .Select(a => new { id = a.Id, kind = a.Kind, text = a.Text, detail = a.Detail, options = a.Options, t = SnapshotBuilder.Ms(a.SinceUtc) }).ToList();
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
        L.T($"{TagKo} 사용자가 보내온 지시입니다. 사용자의 메시지로 받아 처리해 주세요.\n\n{text}",
            $"{TagEn} The user sent this instruction. Treat it as the user's message.\n\n{text}");

    /// <summary>기록 파일의 메시지에서 휴대폰·미니펫 지시 글만 뽑는다(없으면 null).</summary>
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
            ? L.T("사용자가 Claude Office 에서 이 요청을 거절했습니다.", "The user denied this in Claude Office.")
            : L.T("사용자가 Claude Office 에서 이 요청을 거절했습니다. 대신 할 일은 사용자가 따로 보냅니다 — 다른 방법을 시도하지 말고 이번 응답을 마친 뒤 그 메시지를 기다려 주세요.",
                  "The user denied this in Claude Office. They will send what to do instead as a separate message — don't try another way; end this response and wait for it.");
        return JsonSerializer.Serialize(new
        {
            hookSpecificOutput = new { hookEventName = "PermissionRequest", decision = new { behavior = "deny", message, interrupt = false } },
        });   // 기본 인코더는 ASCII 밖의 글자를 \uXXXX 로 쓴다
    }

    private static string QuestionAnswerJson()
    {
        var message = L.T(
            "사용자가 Claude Office 에서 질문에 답을 골라 보냈습니다. 답은 따로 사용자 메시지로 바로 이어서 전달됩니다 — 다시 질문 창을 띄우지 말고 이번 응답을 마친 뒤 그 메시지를 기다려 주세요.",
            "The user answered this question in Claude Office. Their answer will arrive right away as the next user message — end this response and wait for it.");
        return JsonSerializer.Serialize(new
        {
            hookSpecificOutput = new { hookEventName = "PermissionRequest", decision = new { behavior = "deny", message, interrupt = false } },
        });
    }

    // ───────────────────────── 그 밖 ─────────────────────────

    private sealed record HookInput(string Event, string Sid, string? Tool, string? ToolText, string? Detail, string[]? Options, string? LastMessage);

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
            string? detail;
            string[]? options = null;
            if (tool == "AskUserQuestion")
            {
                (detail, options) = ParseQuestion(input);
            }
            else
            {
                // 무엇을 허락하는지 알 수 있게: 명령은 그대로, 파일 도구는 경로
                detail = tool is null ? null
                    : ToolCatalog.Str(input, "command") ?? ToolCatalog.Str(input, "file_path") ?? ToolCatalog.Str(input, "url") ?? ToolCatalog.Str(input, "path");
            }
            return new HookInput(ev, sid, tool, tool != null ? ToolCatalog.Describe(tool, input) : null,
                detail != null ? ToolCatalog.Clip(detail, 600) : null, options, ToolCatalog.Str(r, "last_assistant_message"));
        }
        catch (JsonException) { return null; }
    }

    /// <summary>AskUserQuestion 도구의 입력(questions 배열 또는 단일 question/options)에서 질문 본문과 선택지 목록을 뽑는다.</summary>
    private static (string? Detail, string[]? Options) ParseQuestion(JsonElement input)
    {
        if (input.ValueKind != JsonValueKind.Object) return (null, null);
        var questions = new List<string>();
        var opts = new List<string>();

        static void CollectOptions(JsonElement container, List<string> target)
        {
            if (!container.TryGetProperty("options", out var arr) || arr.ValueKind != JsonValueKind.Array) return;
            foreach (var item in arr.EnumerateArray())
            {
                if (target.Count >= 8) break;
                var label = item.ValueKind == JsonValueKind.String ? item.GetString()
                    : item.ValueKind == JsonValueKind.Object
                        ? (ToolCatalog.Str(item, "label") ?? ToolCatalog.Str(item, "title") ?? ToolCatalog.Str(item, "value"))
                        : null;
                if (!string.IsNullOrWhiteSpace(label))
                    target.Add(ToolCatalog.Clip(label.Trim(), 80));
            }
        }

        if (input.TryGetProperty("questions", out var qs) && qs.ValueKind == JsonValueKind.Array)
        {
            foreach (var q in qs.EnumerateArray())
            {
                if (q.ValueKind != JsonValueKind.Object) continue;
                var txt = ToolCatalog.Str(q, "question") ?? ToolCatalog.Str(q, "header") ?? ToolCatalog.Str(q, "title");
                if (!string.IsNullOrWhiteSpace(txt)) questions.Add(txt.Trim());
                if (opts.Count == 0) CollectOptions(q, opts);
            }
        }
        else
        {
            var txt = ToolCatalog.Str(input, "question") ?? ToolCatalog.Str(input, "prompt") ?? ToolCatalog.Str(input, "text");
            if (!string.IsNullOrWhiteSpace(txt)) questions.Add(txt.Trim());
            CollectOptions(input, opts);
        }

        var detail = questions.Count > 0 ? string.Join("\n", questions) : null;
        return (detail, opts.Count > 0 ? opts.ToArray() : null);
    }

    private void Notify() => Changed?.Invoke();

    /// <summary>앱을 껐다 켰을 때도 최근 1시간 안에 PC(미니펫·본창)에서 보내 아직 전달되지 않은 대기열(pc-queue)은 유지한다.</summary>
    private void RestoreRecentPcQueues()
    {
        if (_logPath is null || !File.Exists(_logPath)) return;
        try
        {
            var cutoff = DateTime.Now.AddHours(-1);
            foreach (var line in File.ReadLines(_logPath).TakeLast(200))
            {
                var parts = line.Split('\t', 4);
                if (parts.Length < 4 || !DateTime.TryParse(parts[0], out var dt) || dt < cutoff) continue;
                var what = parts[1];
                var sid = parts[2];
                var text = parts[3];
                if (what == "pc-queue" && sid.Length > 1 && !string.IsNullOrWhiteSpace(text))
                {
                    if (!_queued.TryGetValue(sid, out var list)) _queued[sid] = list = [];
                    if (list.Count < 5) list.Add(new QueuedMsg(text, FromPc: true));
                    _said[sid] = new Said(string.Join("\n\n", list.Select(x => x.Text)), dt.ToUniversalTime(), "queued");
                }
                else if (what is "deliver" or "pc-deliver" && sid.Length > 1)
                {
                    _queued.Remove(sid);
                    _said[sid] = new Said(text, dt.ToUniversalTime(), "delivered");
                }
                else if (what == "cancel-queue" && sid.Length > 1)
                {
                    _queued.Remove(sid);
                    if (_said.TryGetValue(sid, out var s) && s.State == "queued") _said.Remove(sid);
                }
                else if (what == "away-off" && text == "hooks-off")
                {
                    _queued.Clear();
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

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
