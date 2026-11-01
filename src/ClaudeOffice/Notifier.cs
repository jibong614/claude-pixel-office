using System.Text.Json;
using ClaudeOffice.Monitoring;

namespace ClaudeOffice;

/// <summary>알림 한 건. SessionId: 누르면 열어 줄 직원.</summary>
internal sealed record Notice(string Title, string Text, string? SessionId);

/// <summary>
/// PC 알림을 띄울 때를 고른다 — 감시기가 만든 스냅샷(JSON)을 받아 직원별 상태 변화를 본다.
///   - ✋ 승인·질문을 기다리기 시작함(훅 없이 추정한 승인 대기는 15초 넘게 이어질 때만 — 잠깐 걸린 파일 수정을 잘못 알리지 않게)
///   - 오래(설정 분 넘게) 일하던 직원이 일을 마치고 쉬기 시작함
/// 띄울지 말지(창을 보고 있는지·방해 금지 시간)는 부르는 쪽이 정한다. 감시 스레드에서 불린다.
/// </summary>
internal sealed class Notifier
{
    private static readonly TimeSpan EstimatedWaitDelay = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan TooOld = TimeSpan.FromMinutes(10);   // 프로그램을 켜기 전부터 기다리던 것까지 한꺼번에 알리지 않게

    // 일하는 중으로 보는 상태(승인 대기는 일하던 도중에 잠깐 멈춘 것이라 이어서 센다)
    private static readonly HashSet<string> Busy = new(StringComparer.Ordinal)
    {
        "thinking", "talking", "reading", "searching", "writing", "running", "browsing", "delegating", "planning", "working", "waiting",
    };

    private sealed class Track
    {
        public bool WaitNotified;
        public long BusySince;   // 이번 일을 시작한 시각(ms). 0 = 일하는 중 아님
    }

    private readonly Dictionary<string, Track> _tracks = new(StringComparer.Ordinal);

    /// <param name="nameOf">세션 ID·원래 이름 → 보여 줄 이름(별명이 있으면 별명)</param>
    public List<Notice> OnSnapshot(string json, Func<string, string, string> nameOf)
    {
        var notices = new List<Notice>();
        var s = App.Settings;
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        long now = root.GetProperty("now").GetInt64();
        bool hooksOn = root.TryGetProperty("hooks", out var h) && h.ValueKind == JsonValueKind.Object
                       && h.TryGetProperty("state", out var hs) && hs.GetString() == "on";

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in root.GetProperty("sessions").EnumerateArray())
        {
            var id = e.GetProperty("id").GetString()!;
            var act = e.GetProperty("act").GetString() ?? "";
            long since = e.GetProperty("since").GetInt64();
            var name = nameOf(id, e.GetProperty("name").GetString() ?? "");
            var text = e.GetProperty("text").GetString() ?? "";
            var team = e.GetProperty("team").GetString() ?? "";
            seen.Add(id);
            if (!_tracks.TryGetValue(id, out var t)) _tracks[id] = t = new Track();

            // ✋ 기다리기 시작
            if (act is "waiting" or "asking")
            {
                long waited = now - since;
                bool estimated = act == "waiting" && !hooksOn;
                if (!t.WaitNotified && waited >= (estimated ? EstimatedWaitDelay.TotalMilliseconds : 0))
                {
                    t.WaitNotified = true;
                    if (s.NotifyWaiting && waited < TooOld.TotalMilliseconds)
                        notices.Add(new Notice($"✋ {name}", $"{(act == "asking" ? L.T("질문에 답해 주세요", "Has a question for you") : text)} · {team}", id));
                }
            }
            else t.WaitNotified = false;

            // 오래 걸린 일이 끝남
            if (Busy.Contains(act))
            {
                if (t.BusySince == 0) t.BusySince = since;
            }
            else
            {
                if (act == "idle" && t.BusySince > 0)
                {
                    long worked = since - t.BusySince;
                    if (s.NotifyDone && worked >= s.NotifyDoneMinutes * 60_000L && now - since < TooOld.TotalMilliseconds)
                    {
                        var reply = e.TryGetProperty("reply", out var rp) && rp.ValueKind == JsonValueKind.String ? rp.GetString() : null;
                        var meta = L.T($"{Duration(worked)} 동안 일했어요 · {team}", $"Worked for {Duration(worked)} · {team}");
                        var body = !string.IsNullOrWhiteSpace(reply) ? $"{ToolCatalog.Clip(reply.Trim(), 110)}\n{meta}" : meta;
                        notices.Add(new Notice(L.T($"✅ {name} 일을 마쳤어요", $"✅ {name} is done"), body, id));
                    }
                }
                t.BusySince = 0;   // 쉬는 중·질문 중·잠·퇴근이면 다음 일은 새로 센다
            }
        }
        foreach (var id in _tracks.Keys.Where(k => !seen.Contains(k)).ToList()) _tracks.Remove(id);

        // 한꺼번에 여럿이면 하나로 묶는다(알림이 줄줄이 쌓이지 않게)
        if (notices.Count > 1)
        {
            var names = string.Join(", ", notices.Select(n => n.Title));
            return [new Notice(L.T($"Claude Office 알림 {notices.Count}건", $"Claude Office: {notices.Count} alerts"), names, notices[0].SessionId)];
        }
        return notices;
    }

    private static string Duration(long ms)
    {
        var t = TimeSpan.FromMilliseconds(ms);
        return t.TotalHours >= 1
            ? L.T($"{(int)t.TotalHours}시간 {t.Minutes}분", $"{(int)t.TotalHours}h {t.Minutes}m")
            : L.T($"{Math.Max(1, (int)t.TotalMinutes)}분", $"{Math.Max(1, (int)t.TotalMinutes)} min");
    }
}
