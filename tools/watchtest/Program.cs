// 감시기를 2.5초 돌린 뒤 지금 사무실에 보일 직원과 최근 작업을 출력한다.
//   --hooks 30 : 훅 수신기(127.0.0.1:47821)도 켜고 30초 동안 받은 이벤트와 상태 변화를 출력한 뒤 마지막 모습을 출력
//                (settings.json 을 건드리지 않으므로, 훅은 claude --settings <파일> 이나 curl 로 직접 보내 시험한다)
//   --setup install|uninstall|install-remote|uninstall-remote : CLAUDE_CONFIG_DIR 아래 settings.json 에 훅(원격 훅) 넣기/빼기 시험
//                (실제 설정 말고 복사본 폴더를 가리킬 것. CLAUDEOFFICE_HOOK_PORT 로 훅 포트를 바꿀 수 있다)
//   --phrases 60 : 실제 대화 기록의 최근 도구 호출 60개에 말풍선 규칙을 적용해 "말풍선 ← 원래 설명"으로 출력(한글화 점검)
//   --en         : (어느 것과도 함께) 영어 화면 문구로 — 예) --phrases 60 --en
using System.Text.Json;
using ClaudeOffice.Hooks;
using ClaudeOffice.Monitoring;

if (args.Contains("--en"))
{
    L.En = true;
    args = args.Where(a => a != "--en").ToArray();
}

if (args.Length >= 1 && args[0] == "--phrases")
{
    int take = args.Length >= 2 && int.TryParse(args[1], out var t) ? t : 60;
    var calls = new List<(string Ts, string Name, JsonElement Input)>();
    var docs = new List<JsonDocument>();
    foreach (var file in Directory.EnumerateFiles(TranscriptWatcher.DefaultRoot(), "*.jsonl", SearchOption.AllDirectories)
                                  .Where(f => File.GetLastWriteTimeUtc(f) > DateTime.UtcNow.AddDays(-2)))
    {
        using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var sr = new StreamReader(fs);
        while (sr.ReadLine() is { } line)
        {
            if (!line.Contains("\"tool_use\"")) continue;
            JsonDocument jd;
            try { jd = JsonDocument.Parse(line); } catch (JsonException) { continue; }
            var r = jd.RootElement;
            if (!r.TryGetProperty("message", out var msg) || !msg.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) { jd.Dispose(); continue; }
            foreach (var b in content.EnumerateArray())
                if (ToolCatalog.Str(b, "type") == "tool_use" && b.TryGetProperty("input", out var inp))
                    calls.Add((ToolCatalog.Str(r, "timestamp") ?? "", ToolCatalog.Str(b, "name") ?? "?", inp));
            docs.Add(jd);
        }
    }
    foreach (var (ts, name, inp) in calls.OrderBy(c => c.Ts).TakeLast(take))
    {
        var bubble = ToolCatalog.Describe(name, inp);
        var original = ToolCatalog.Str(inp, "description") ?? ToolCatalog.Str(inp, "query") ?? ToolCatalog.Str(inp, "pattern") ?? ToolCatalog.Str(inp, "file_path");
        Console.WriteLine($"{name,-12} {bubble,-28} ← {ToolCatalog.Clip(original, 70)}");
    }
    docs.ForEach(d => d.Dispose());
    return;
}

if (args.Length >= 2 && args[0] == "--setup")
{
    switch (args[1])
    {
        case "install": HookSetup.Install(); break;
        case "install-remote": HookSetup.InstallRemote(); break;
        case "uninstall-remote": HookSetup.UninstallRemote(); break;
        default: HookSetup.Uninstall(); break;
    }
    Console.WriteLine($"{HookSetup.SettingsPath} installed={HookSetup.IsInstalled()} remote={HookSetup.IsRemoteInstalled()}");
    return;
}

int hookSec = args.Length >= 2 && args[0] == "--hooks" && int.TryParse(args[1], out var n) ? n : 0;
var w = new TranscriptWatcher(TranscriptWatcher.DefaultRoot());
using var hooks = new HookServer(null);
if (hookSec > 0)
{
    hooks.Start();
    Console.WriteLine(hooks.Listening ? $"훅 수신 중: 127.0.0.1:{HookServer.Port}" : $"훅 수신 실패: {hooks.Error}");
    hooks.Received += ev => Console.WriteLine(
        $"{DateTime.Now:HH:mm:ss.fff} ← {ev.Event} session={ev.SessionId[..Math.Min(8, ev.SessionId.Length)]} agent={ev.AgentId ?? "-"} " +
        $"tool={ev.ToolName ?? "-"} text={ev.ToolText ?? "-"} type={ev.NotificationType ?? "-"} reason={ev.Reason ?? "-"}");
    w.AttachHooks(hooks);

    // 직원 상태가 바뀔 때마다 한 줄씩(승인 대기가 언제 뜨고 언제 내려가는지 보려고)
    var last = new Dictionary<string, string>();
    w.SnapshotChanged += json =>
    {
        using var d = JsonDocument.Parse(json);
        foreach (var s in d.RootElement.GetProperty("sessions").EnumerateArray())
        {
            var id = s.GetProperty("id").GetString()!;
            var line = $"[{s.GetProperty("act").GetString(),-10}] {s.GetProperty("icon").GetString()} {s.GetProperty("text").GetString()}";
            if (last.TryGetValue(id, out var prev) && prev == line) continue;
            last[id] = line;
            Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff}   {s.GetProperty("name").GetString()} {line}");
        }
    };
}
w.Start();
await Task.Delay(hookSec > 0 ? hookSec * 1000 : 2500);

var json = w.LastSnapshot ?? "{}";
using var doc = JsonDocument.Parse(json);
if (doc.RootElement.TryGetProperty("hooks", out var h)) Console.WriteLine($"hooks={h}");
foreach (var s in doc.RootElement.GetProperty("sessions").EnumerateArray())
{
    string P(string n) => s.TryGetProperty(n, out var v) ? v.ToString() : "-";
    Console.WriteLine($"[{P("act"),-10}] {P("name")} | team={P("team")} | {P("icon")} {P("text")} | interns={s.GetProperty("interns").GetArrayLength()} | recent={s.GetProperty("recent").GetArrayLength()}");
    var first = s.GetProperty("recent").EnumerateArray().Take(3).Select(r => "    · " + r.GetProperty("icon").GetString() + " " + r.GetProperty("text").GetString());
    foreach (var l in first) Console.WriteLine(l);
}
Console.WriteLine($"json bytes={json.Length}");
