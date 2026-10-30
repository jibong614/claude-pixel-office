using System.IO;
using System.Text;
using System.Text.Json;

namespace ClaudeOffice.Monitoring;

/// <summary>
/// 오늘(이 PC 시각 기준) 한 세션의 근무 일지 — 일한 시간·지시 횟수·도구 사용·토큰.
///   - 일한 시간: 줄과 줄 사이 시간을 더한다. 단 사람이 쓴 지시 줄 앞의 틈(사람이 생각한 시간)과 30분 넘는 틈(자리 비움)은 뺀다.
///     승인을 기다린 시간·오래 도는 명령 시간은 들어간다.
///   - 토큰: 답변 메시지(message.id)마다 usage 의 마지막 값. 한 메시지가 블록마다 여러 줄로 나뉘어 같은 usage 가 되풀이되므로 ID로 한 번만 센다.
/// </summary>
internal sealed class DayStats
{
    private static readonly TimeSpan MaxGap = TimeSpan.FromMinutes(30);

    public DateTime Day;   // 지역 날짜(자정)
    public long WorkMs;
    public int Prompts;
    public readonly Dictionary<string, int> Tools = new(StringComparer.Ordinal);
    public readonly Dictionary<string, Usage> Messages = new(StringComparer.Ordinal);
    private DateTime _lastUtc;

    public readonly record struct Usage(long In, long Out, long CacheRead, long CacheWrite);

    public bool IsToday(DateTime nowUtc) => Day == nowUtc.ToLocalTime().Date;

    /// <summary>user·assistant 줄 하나. 오늘이 아닌 줄은 무시하고, 날짜가 바뀌면 새로 센다.</summary>
    public void Add(JsonElement r, string type, DateTime ts)
    {
        var day = ts.ToLocalTime().Date;
        if (day < Day) return;
        if (day > Day)
        {
            Day = day;
            WorkMs = 0;
            Prompts = 0;
            Tools.Clear();
            Messages.Clear();
            _lastUtc = default;
        }

        bool prompt = type == "user" && IsPrompt(r);
        if (prompt) Prompts++;
        else if (_lastUtc != default && ts > _lastUtc && ts - _lastUtc <= MaxGap)
            WorkMs += (long)(ts - _lastUtc).TotalMilliseconds;
        if (ts > _lastUtc) _lastUtc = ts;

        if (type != "assistant" || !r.TryGetProperty("message", out var msg) || msg.ValueKind != JsonValueKind.Object) return;
        if (msg.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
            foreach (var b in content.EnumerateArray())
                if (b.ValueKind == JsonValueKind.Object && Str(b, "type") == "tool_use" && Str(b, "name") is { } name)
                    Tools[name] = Tools.GetValueOrDefault(name) + 1;
        if (Str(msg, "id") is { } id && msg.TryGetProperty("usage", out var u) && u.ValueKind == JsonValueKind.Object)
            Messages[id] = new Usage(Num(u, "input_tokens"), Num(u, "output_tokens"), Num(u, "cache_read_input_tokens"), Num(u, "cache_creation_input_tokens"));
    }

    /// <summary>사람이 쓴 지시인지(도구 결과·스킬 본문·압축 요약이 아닌 user 줄).</summary>
    private static bool IsPrompt(JsonElement r)
    {
        if (Bool(r, "isMeta") || Bool(r, "isCompactSummary")) return false;
        if (!r.TryGetProperty("message", out var msg) || !msg.TryGetProperty("content", out var c)) return false;
        if (c.ValueKind == JsonValueKind.String) return true;
        if (c.ValueKind != JsonValueKind.Array) return false;
        bool text = false;
        foreach (var b in c.EnumerateArray())
        {
            var t = Str(b, "type");
            if (t == "tool_result") return false;
            if (t == "text") text = true;
        }
        return text;
    }

    /// <summary>
    /// 처음 읽을 때 감시기는 파일 끝 몇 MB만 읽으므로, 그 앞부분에서 오늘 줄만 골라 통계에만 넣는다.
    /// 줄마다 JSON 을 풀면 느려서, 오늘·어제(UTC 로는 이틀에 걸침) 날짜 글자가 있는 줄만 푼다.
    /// </summary>
    public static void ScanPrefix(TranscriptState s, long end)
    {
        if (end <= 0) return;
        var today = DateTime.Now.Date;
        var marks = new[] { today.ToUniversalTime(), today.AddDays(1).AddTicks(-1).ToUniversalTime() }
            .Select(d => Encoding.ASCII.GetBytes($"\"timestamp\":\"{d:yyyy-MM-dd}T")).Distinct(new BytesComparer()).ToArray();

        using var fs = new FileStream(s.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16);
        var buf = new byte[1 << 20];
        var carry = new MemoryStream();
        long read = 0;
        while (read < end)
        {
            int n = fs.Read(buf, 0, (int)Math.Min(buf.Length, end - read));
            if (n <= 0) break;
            read += n;
            int pos = 0;
            while (pos < n)
            {
                int nl = Array.IndexOf(buf, (byte)'\n', pos, n - pos);
                if (nl < 0) { carry.Write(buf, pos, n - pos); break; }
                ReadOnlySpan<byte> line;
                if (carry.Length > 0)
                {
                    carry.Write(buf, pos, nl - pos);
                    line = carry.ToArray();
                    carry.SetLength(0);
                }
                else line = buf.AsSpan(pos, nl - pos);
                pos = nl + 1;
                bool todayish = false;
                foreach (var m in marks) todayish |= line.IndexOf(m) >= 0;
                if (!todayish) continue;
                try
                {
                    using var doc = JsonDocument.Parse(line.ToArray());
                    var r = doc.RootElement;
                    if (r.ValueKind != JsonValueKind.Object || Str(r, "type") is not ("user" or "assistant") || TranscriptParser.Time(r) is not { } ts) continue;
                    s.Today.Add(r, Str(r, "type")!, ts);
                }
                catch (JsonException) { }
            }
        }
    }

    private sealed class BytesComparer : IEqualityComparer<byte[]>
    {
        public bool Equals(byte[]? a, byte[]? b) => a.AsSpan().SequenceEqual(b);
        public int GetHashCode(byte[] b) => b.Length == 0 ? 0 : b[^1] ^ (b[^2] << 8) ^ (b[^4] << 16);
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static long Num(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : 0;
}
