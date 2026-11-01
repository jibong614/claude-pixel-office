using System.Buffers;
using System.IO;
using System.Text;
using System.Text.Json;

namespace ClaudeOffice.Monitoring;

/// <summary>
/// 한 세션의 일자별 근무 일지(최근 7일 보관) 및 현재 대화 문맥(컨텍스트) 크기 — 일한 시간·지시 횟수·도구 사용·토큰.
///   - 일한 시간: 줄과 줄 사이 시간을 더한다. 단 사람이 쓴 지시 줄 앞의 틈(사람이 생각한 시간)과 30분 넘는 틈(자리 비움)은 뺀다.
///     승인을 기다린 시간·오래 도는 명령 시간은 들어간다.
///   - 토큰: 답변 메시지(message.id)마다 usage 의 마지막 값. 한 메시지가 블록마다 여러 줄로 나뉘어 같은 usage 가 되풀이되므로 ID로 한 번만 센다.
///   - 문맥(LastContextTokens): 가장 최근 assistant 응답의 입력 토큰 합(input + cache_read + cache_creation)으로 현재 대화창이 얼마나 찼는지 나타낸다.
/// </summary>
internal sealed class DayStats
{
    private static readonly TimeSpan MaxGap = TimeSpan.FromMinutes(30);
    private const int KeepDays = 8;

    public sealed class Bucket
    {
        public long WorkMs;
        public int Prompts;
        public readonly Dictionary<string, int> Tools = new(StringComparer.Ordinal);
        public readonly Dictionary<string, Usage> Messages = new(StringComparer.Ordinal);
        public DateTime LastUtc;
    }

    public readonly record struct Usage(long In, long Out, long CacheRead, long CacheWrite);

    private readonly SortedDictionary<DateTime, Bucket> _days = new();
    private DateTime _latestDay;

    /// <summary>가장 최근 assistant 메시지의 총 입력 문맥 토큰(input + cache_read + cache_creation).</summary>
    public long LastContextTokens;

    public Bucket? GetDay(DateTime localDate) => _days.GetValueOrDefault(localDate.Date);

    public IEnumerable<Bucket> GetRange(DateTime fromLocalDate, DateTime toLocalDate)
    {
        var from = fromLocalDate.Date;
        var to = toLocalDate.Date;
        foreach (var (d, b) in _days)
            if (d >= from && d <= to)
                yield return b;
    }

    /// <summary>user·assistant 줄 하나. 최근 7일 밖의 오래된 줄은 무시하고 일자별 버킷에 쌓는다.</summary>
    public void Add(JsonElement r, string type, DateTime ts)
    {
        var day = ts.ToLocalTime().Date;
        if (day > _latestDay)
        {
            _latestDay = day;
            var cutoff = _latestDay.AddDays(-KeepDays);
            foreach (var old in _days.Keys.Where(k => k < cutoff).ToList())
                _days.Remove(old);
        }

        if (_latestDay != default && day < _latestDay.AddDays(-KeepDays)) return;

        if (!_days.TryGetValue(day, out var b))
            _days[day] = b = new Bucket();

        bool prompt = type == "user" && IsPrompt(r);
        if (prompt) b.Prompts++;
        else if (b.LastUtc != default && ts > b.LastUtc && ts - b.LastUtc <= MaxGap)
            b.WorkMs += (long)(ts - b.LastUtc).TotalMilliseconds;
        if (ts > b.LastUtc) b.LastUtc = ts;

        if (type != "assistant" || !r.TryGetProperty("message", out var msg) || msg.ValueKind != JsonValueKind.Object) return;
        if (msg.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
            foreach (var blk in content.EnumerateArray())
                if (blk.ValueKind == JsonValueKind.Object && Str(blk, "type") == "tool_use" && Str(blk, "name") is { } name)
                    b.Tools[name] = b.Tools.GetValueOrDefault(name) + 1;
        if (Str(msg, "id") is { } id && msg.TryGetProperty("usage", out var u) && u.ValueKind == JsonValueKind.Object)
        {
            var usage = new Usage(Num(u, "input_tokens"), Num(u, "output_tokens"), Num(u, "cache_read_input_tokens"), Num(u, "cache_creation_input_tokens"));
            b.Messages[id] = usage;
            long ctx = usage.In + usage.CacheRead + usage.CacheWrite;
            if (ctx > 0) LastContextTokens = ctx;
        }
    }

    /// <summary>사람이 쓴 지시인지(도구 결과·스킬 본문·압축 요약이 아닌 user 줄).</summary>
    private static bool IsPrompt(JsonElement r)
    {
        if (Bool(r, "isMeta") || Bool(r, "isCompactSummary")) return false;
        if (!r.TryGetProperty("message", out var msg) || msg.ValueKind != JsonValueKind.Object
            || !msg.TryGetProperty("content", out var c)) return false;
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
    /// 처음 읽을 때 감시기는 파일 끝 몇 MB만 읽으므로, 그 앞부분에서 최근 7일 줄만 골라 통계에만 넣는다.
    /// 줄마다 JSON 을 풀면 느려서, 최근 날짜 글자가 있는 줄만 푼다.
    /// </summary>
    public static void ScanPrefix(TranscriptState s, long end)
    {
        if (end <= 0) return;
        var today = DateTime.Now.Date;
        var marks = Enumerable.Range(-7, 9)
            .Select(i => today.AddDays(i).ToUniversalTime())
            .Select(d => Encoding.ASCII.GetBytes($"\"timestamp\":\"{d:yyyy-MM-dd}T"))
            .Distinct(new BytesComparer())
            .ToArray();

        using var fs = new FileStream(s.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16);
        var buf = ArrayPool<byte>.Shared.Rent(1 << 20);
        try
        {
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
                    ReadOnlyMemory<byte> line;
                    if (carry.Length > 0)
                    {
                        carry.Write(buf, pos, nl - pos);
                        line = new ReadOnlyMemory<byte>(carry.GetBuffer(), 0, (int)carry.Length);
                        carry.SetLength(0);
                    }
                    else line = new ReadOnlyMemory<byte>(buf, pos, nl - pos);
                    pos = nl + 1;
                    bool recent = false;
                    foreach (var m in marks) recent |= line.Span.IndexOf(m) >= 0;
                    if (!recent) continue;
                    try
                    {
                        using var doc = JsonDocument.Parse(line);
                        var r = doc.RootElement;
                        if (r.ValueKind != JsonValueKind.Object || Str(r, "type") is not ("user" or "assistant") || TranscriptParser.Time(r) is not { } ts) continue;
                        s.Today.Add(r, Str(r, "type")!, ts);
                    }
                    catch (Exception ex) when (ex is JsonException or InvalidOperationException) { }
                }
            }
        }
        finally { ArrayPool<byte>.Shared.Return(buf); }
    }

    private sealed class BytesComparer : IEqualityComparer<byte[]>
    {
        public bool Equals(byte[]? a, byte[]? b) => a.AsSpan().SequenceEqual(b);
        public int GetHashCode(byte[] b) => b.Length == 0 ? 0 : b[^1] ^ (b[^2] << 8) ^ (b[^4] << 16);
    }

    private static string? Str(JsonElement e, string name) => ToolCatalog.Str(e, name);
    private static bool Bool(JsonElement e, string name) => ToolCatalog.Bool(e, name);
    private static long Num(JsonElement e, string name) => ToolCatalog.Num(e, name);
}
