using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ClaudeOffice.Monitoring;

/// <summary>
/// Claude Code 대화 기록(jsonl) 한 줄을 읽어 <see cref="TranscriptState"/>에 반영한다.
///
/// 기록에서 쓰는 줄 종류
///   user       : 사람의 지시(text) 또는 도구 결과(tool_result)
///   assistant  : 생각(thinking) / 답변(text) / 도구 호출(tool_use). 블록 하나가 한 줄이고 stop_reason이 함께 붙는다
///   custom-title, ai-title, agent-name, last-prompt : 이름표에 쓸 정보
///   system     : 대화 압축(compact_boundary) 등
/// </summary>
internal static partial class TranscriptParser
{
    // <system-reminder>…</system-reminder>, <command-name>…</command-name> 같은 주입 블록
    [GeneratedRegex(@"<([a-zA-Z][\w-]*)\b[^>]*>.*?</\1>", RegexOptions.Singleline, matchTimeoutMilliseconds: 200)]
    private static partial Regex TagBlock();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    // API 키/토큰/비밀키 누출 감지 패턴 (sk-, AKIA, ghp_, xox-, PRIVATE KEY)
    [GeneratedRegex(@"(?:sk-(?:ant-|proj-)?[a-zA-Z0-9_\-]{20,}|AKIA[0-9A-Z]{16}|gh[pous]_[a-zA-Z0-9]{36}|xox[baprs]-[0-9a-zA-Z]{10,48}|-----BEGIN (?:RSA|EC|DSA|OPENSSH) PRIVATE KEY-----)", RegexOptions.Compiled, matchTimeoutMilliseconds: 200)]
    private static partial Regex SecretPattern();

    private static void CheckSecret(TranscriptState s, string? text)
    {
        if (s.HasSecret || string.IsNullOrEmpty(text)) return;
        try
        {
            if (SecretPattern().IsMatch(text))
                s.HasSecret = true;
        }
        catch (RegexMatchTimeoutException) { }
    }

    public static void Apply(TranscriptState s, ReadOnlyMemory<byte> line)
    {
        using var doc = JsonDocument.Parse(line);
        var r = doc.RootElement;
        if (r.ValueKind != JsonValueKind.Object) return;

        var type = Str(r, "type");
        if (type is null) return;
        var ts = Time(r) ?? s.FileWriteUtc;

        switch (type)
        {
            case "custom-title": s.CustomTitle = Str(r, "customTitle") ?? s.CustomTitle; return;
            case "ai-title": s.AiTitle = Str(r, "aiTitle") ?? s.AiTitle; return;
            case "agent-name": s.AgentName = Str(r, "agentName") ?? s.AgentName; return;
            case "last-prompt": if (!s.LastPromptFromPhone) s.LastPrompt = CleanPrompt(Str(r, "lastPrompt")) ?? s.LastPrompt; return;
            case "user": Common(s, r, ts); s.Today.Add(r, type, ts); OnUser(s, r, ts); return;
            case "assistant": Common(s, r, ts); s.Today.Add(r, type, ts); OnAssistant(s, r, ts); return;
            case "system": OnSystem(s, r, ts); return;
        }
    }

    private static void Common(TranscriptState s, JsonElement r, DateTime ts)
    {
        s.Seen = true;
        if (ts > s.LastEventUtc) s.LastEventUtc = ts;
        s.Cwd = Str(r, "cwd") ?? s.Cwd;
        s.Entrypoint = Str(r, "entrypoint") ?? s.Entrypoint;
        s.GitBranch = Str(r, "gitBranch") ?? s.GitBranch;
    }

    private static void OnUser(TranscriptState s, JsonElement r, DateTime ts)
    {
        if (Bool(r, "isMeta")) return;   // 스킬 본문 등 사람이 쓰지 않은 메시지
        if (Bool(r, "isCompactSummary"))
        {
            s.Log(ts, "📦", L.T("대화 압축 후 이어서 진행", "Continuing after compaction"));
            return;
        }
        if (!r.TryGetProperty("message", out var msg) || msg.ValueKind != JsonValueKind.Object
            || !msg.TryGetProperty("content", out var content)) return;

        if (content.ValueKind == JsonValueKind.String)
        {
            OnPrompt(s, content.GetString(), ts);
            return;
        }
        if (content.ValueKind != JsonValueKind.Array) return;

        bool gotResult = false;
        foreach (var b in content.EnumerateArray())
        {
            switch (Str(b, "type"))
            {
                case "tool_result":
                    gotResult = true;
                    var id = Str(b, "tool_use_id");
                    if (id is null || !s.Pending.Remove(id, out var p)) break;
                    s.ResolvedUtc[p.Name] = ts;
                    if (Bool(b, "is_error"))
                    {
                        var body = TextOf(b, "content") ?? "";
                        var phone = body.Contains("(Claude Office 원격)", StringComparison.Ordinal) || body.Contains("(Claude Office remote)", StringComparison.Ordinal);
                        var rejected = phone
                                       || body.Contains("doesn't want to proceed", StringComparison.OrdinalIgnoreCase)
                                       || body.Contains("rejected", StringComparison.OrdinalIgnoreCase);
                        s.Log(ts, rejected ? "🚫" : "❌", $"{p.Text} — {(phone ? L.T("휴대폰에서 거절", "denied from the phone") : rejected ? L.T("거절됨", "rejected") : L.T("실패", "failed"))}");
                    }
                    break;
                case "text":
                    OnPrompt(s, Str(b, "text"), ts);
                    break;
            }
        }

        // 결과를 받았고 더 기다릴 도구가 없으면 다음 행동을 고민하는 중
        if (gotResult && s.Pending.Count == 0)
            s.SetBase(Activity.Thinking, "💭", L.T("생각 중", "Thinking"), ts);
    }

    private static void OnPrompt(TranscriptState s, string? raw, DateTime ts)
    {
        if (string.IsNullOrWhiteSpace(raw)) return;
        CheckSecret(s, raw);

        if (raw.TrimStart().StartsWith("[Request interrupted", StringComparison.Ordinal))
        {
            s.Pending.Clear();
            s.SetBase(Activity.Idle, "⏹️", L.T("중단됨", "Stopped"), ts);
            s.Log(ts, "⏹️", L.T("작업이 중단됨", "Interrupted"));
            return;
        }

        // 자리 비움 원격: 휴대폰 지시는 훅이 깨운 시스템 알림 안에 들어 있다(태그를 지우면 사라지므로 먼저 꺼낸다)
        if (Remote.RemoteHub.ExtractInstruction(raw) is { } fromPhone)
        {
            s.LastPrompt = "📱 " + ToolCatalog.Clip(Spaces().Replace(fromPhone, " "), 300);
            s.LastPromptFromPhone = true;
            s.SetBase(Activity.Thinking, "💭", L.T("생각 중", "Thinking"), ts);
            s.Log(ts, "📱", L.T("휴대폰 지시: ", "From the phone: ") + ToolCatalog.Clip(fromPhone, 60));
            return;
        }

        var clean = CleanPrompt(raw);
        if (clean is null) return;   // 슬래시 명령·시스템 알림처럼 태그만 있는 메시지

        s.LastPrompt = clean;
        s.LastPromptFromPhone = false;
        s.SetBase(Activity.Thinking, "💭", L.T("생각 중", "Thinking"), ts);
        s.Log(ts, "📨", L.T("지시: ", "Prompt: ") + ToolCatalog.Clip(clean, 60));
    }

    private static void OnAssistant(TranscriptState s, JsonElement r, DateTime ts)
    {
        if (!r.TryGetProperty("message", out var msg) || msg.ValueKind != JsonValueKind.Object) return;

        var model = Str(msg, "model");
        if (model != null && model != "<synthetic>") s.Model = model;

        if (Bool(r, "isApiErrorMessage"))
        {
            s.Pending.Clear();
            s.SetBase(Activity.Idle, "⚠️", L.T("오류로 멈춤", "Stopped by an error"), ts);
            s.Log(ts, "⚠️", L.T("API 오류로 멈춤", "Stopped by an API error"));
            return;
        }

        bool hasToolUse = false;
        if (msg.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            foreach (var b in content.EnumerateArray())
            {
                switch (Str(b, "type"))
                {
                    case "thinking":
                    case "redacted_thinking":
                        s.SetBase(Activity.Thinking, "💭", L.T("생각 중", "Thinking"), ts);
                        var thTxt = Str(b, "thinking") ?? Str(b, "text");
                        if (!string.IsNullOrWhiteSpace(thTxt))
                        {
                            s.LastThinking = ToolCatalog.Clip(thTxt.Trim(), 1200);
                            CheckSecret(s, thTxt);
                        }
                        break;

                    case "text":
                        if (Str(b, "text") is { } ansTxt && !string.IsNullOrWhiteSpace(ansTxt))
                        {
                            s.SetBase(Activity.Talking, "💬", L.T("답변 쓰는 중", "Writing an answer"), ts);
                            s.LastReply = ToolCatalog.Clip(Spaces().Replace(ansTxt.Trim(), " "), 240);
                            CheckSecret(s, ansTxt);
                        }
                        break;

                    case "tool_use":
                    {
                        hasToolUse = true;
                        var name = Str(b, "name") ?? "도구";
                        var id = Str(b, "id") ?? Guid.NewGuid().ToString("N");
                        var input = b.TryGetProperty("input", out var inp) ? inp : default;
                        if (input.ValueKind != JsonValueKind.Undefined)
                            CheckSecret(s, input.GetRawText());
                        if (name is "TodoWrite" or "TaskCreate" or "TaskUpdate" or "TaskList")
                        {
                            if (input.ValueKind == JsonValueKind.Object && input.TryGetProperty("todos", out var todoArr) && todoArr.ValueKind == JsonValueKind.Array)
                            {
                                var list = new List<TodoItem>();
                                foreach (var item in todoArr.EnumerateArray())
                                {
                                    var itemContent = Str(item, "content") ?? Str(item, "text") ?? Str(item, "task") ?? Str(item, "subject");
                                    var status = Str(item, "status") ?? "pending";
                                    if (!string.IsNullOrWhiteSpace(itemContent))
                                        list.Add(new TodoItem(ToolCatalog.Clip(itemContent, 80), status));
                                }
                                if (list.Count > 0)
                                    s.Todos = list;
                            }
                        }
                        var (act, icon) = ToolCatalog.Classify(name);
                        var text = ToolCatalog.Describe(name, input);
                        s.Pending[id] = new PendingTool(id, name, text, act, icon, ts);
                        s.Log(ts, icon, ToolCatalog.Detail(name, input));   // 기록에는 한글로 바꾸며 빠진 원래 설명도
                        break;
                    }

                    case "server_tool_use":   // 서버에서 바로 끝나는 도구(웹 검색 등) — 결과 줄이 따로 없으니 기록만
                    {
                        var name = Str(b, "name") == "web_search" ? "WebSearch" : (Str(b, "name") ?? "도구");
                        var input = b.TryGetProperty("input", out var inp) ? inp : default;
                        var (_, icon) = ToolCatalog.Classify(name);
                        s.Log(ts, icon, ToolCatalog.Detail(name, input));
                        break;
                    }
                }
            }
        }

        // 도구 호출 없이 끝난 메시지 = 응답 완료, 대기 상태
        var stop = Str(msg, "stop_reason");
        if (!hasToolUse && stop is "end_turn" or "stop_sequence" or "max_tokens" or "refusal")
        {
            s.Pending.Clear();
            s.SetBase(Activity.Idle, "☕", L.T("대기 중", "Standing by"), ts);
            var mid = Str(msg, "id");
            if (mid is null || mid != s.LastCompletedMessageId)
            {
                s.LastCompletedMessageId = mid;
                var summary = !string.IsNullOrWhiteSpace(s.LastReply) ? " — " + ToolCatalog.Clip(s.LastReply, 60) : "";
                s.Log(ts, "✅", L.T("응답 완료", "Reply finished") + summary);
            }
        }
    }

    private static void OnSystem(TranscriptState s, JsonElement r, DateTime ts)
    {
        if (Str(r, "subtype") == "compact_boundary")
        {
            if (ts > s.LastEventUtc) s.LastEventUtc = ts;
            s.Log(ts, "📦", L.T("대화 압축", "Conversation compacted"));
        }
    }

    /// <summary>프롬프트에서 주입 태그 블록을 빼고 한 줄로 만든다. 남는 게 없으면 null.</summary>
    internal static string? CleanPrompt(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        string t;
        try { t = TagBlock().Replace(raw.Length > 32768 ? raw[..32768] : raw, " "); }
        catch (RegexMatchTimeoutException) { t = raw; }
        t = Spaces().Replace(t, " ").Trim();
        return t.Length == 0 ? null : t;
    }

    private static string? Str(JsonElement e, string name) => ToolCatalog.Str(e, name);

    private static bool Bool(JsonElement e, string name) => ToolCatalog.Bool(e, name);

    internal static DateTime? Time(JsonElement r)
    {
        var s = Str(r, "timestamp");
        return s != null && DateTime.TryParse(s, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d) ? d : null;
    }

    /// <summary>tool_result 의 content 는 문자열이거나 블록 배열이다.</summary>
    private static string? TextOf(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.String) return v.GetString();
        if (v.ValueKind != JsonValueKind.Array) return null;
        foreach (var b in v.EnumerateArray())
        {
            var t = Str(b, "text");
            if (t != null) return t;
        }
        return null;
    }
}
