using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using ClaudeOffice.Hooks;

namespace ClaudeOffice.Monitoring;

/// <summary>
/// Claude Code 대화 기록 폴더(~/.claude/projects)를 주기적으로 훑어 세션 상태를 갱신하고,
/// 화면에 그릴 스냅샷(JSON)이 바뀌면 <see cref="SnapshotChanged"/>로 알린다.
///
/// FileSystemWatcher 대신 폴링을 쓰는 이유: 기록 파일은 Claude Code가 쉬지 않고 덧붙여 쓰는 중이라
/// 변경 알림이 몰리거나 빠지기 쉽고, 필요한 정보는 "파일 길이가 늘었는가"와 "어디까지 읽었는가"뿐이다.
/// 모든 상태는 감시 루프 스레드 하나에서만 만진다.
/// </summary>
public sealed class TranscriptWatcher : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(700);
    private const int DiscoverEveryTicks = 4;                          // 새 파일 찾기는 약 3초마다

    /// <summary>기본 표시 기간: 마지막 대화가 이보다 오래된 세션은 사무실에 띄우지 않는다(화면 설정 "표시 기간"으로 바꿈).</summary>
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromHours(12);

    /// <summary>처음 보는 파일은 끝부분만 읽는다(수십 MB짜리 기록도 있다). 이름표 줄은 기록 중간중간 반복되므로 끝부분에도 있다.</summary>
    private const long InitialTailBytes = 2L * 1024 * 1024;

    /// <summary>한 번에 이보다 많이 늘었으면 앞부분은 건너뛴다(이미지 첨부 등).</summary>
    private const long MaxReadBytes = 16L * 1024 * 1024;

    /// <summary>훅 이벤트가 왔는데 그 세션(인턴)의 기록 파일을 아직 못 찾았으면 이만큼 다시 시도한다.</summary>
    private static readonly TimeSpan HookBacklogKeep = TimeSpan.FromSeconds(30);

    /// <summary>인턴 기록 파일은 약 3초마다 찾으므로, 인턴에서 난 이벤트는 그 인턴이 보일 때까지 이만큼 기다린다.</summary>
    private static readonly TimeSpan InternWait = TimeSpan.FromSeconds(10);

    private readonly string _root;
    private readonly Dictionary<string, TranscriptState> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<HookEvent> _hookQueue = new();
    private readonly List<HookEvent> _hookBacklog = new();
    private HookServer? _hookServer;
    private bool _hooksInstalled;
    private volatile bool _hookCheckNow;
    private long _windowTicks = DefaultWindow.Ticks;   // 화면 설정이 UI 스레드에서 바꾸므로 Interlocked 로 읽고 쓴다
    private volatile bool _discoverNow;
    private volatile bool _reparseNow;
    private CancellationTokenSource? _cts;
    private volatile string? _lastSnapshot;

    public TranscriptWatcher(string root) => _root = root;

    /// <summary>스냅샷(JSON)이 바뀔 때마다 감시 스레드에서 호출된다.</summary>
    public event Action<string>? SnapshotChanged;

    public string? LastSnapshot => _lastSnapshot;
    public string Root => _root;

    /// <summary>Claude Code 설정 폴더(~/.claude, CLAUDE_CONFIG_DIR 이 있으면 그곳).</summary>
    public static string ConfigDir()
    {
        var cfg = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        return !string.IsNullOrWhiteSpace(cfg)
            ? cfg
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
    }

    public static string DefaultRoot() => Path.Combine(ConfigDir(), "projects");

    /// <summary>훅 수신기를 붙인다. 받은 이벤트는 감시 스레드에서 다음 틱에 반영한다. Start 전에 부를 것.</summary>
    internal void AttachHooks(HookServer server)
    {
        _hookServer = server;
        server.Received += _hookQueue.Enqueue;
    }

    /// <summary>훅을 넣거나 뺀 직후 설치 상태를 바로 다시 읽게 한다.</summary>
    public void RefreshHookStatus() => _hookCheckNow = true;

    /// <summary>표시 기간: 마지막 대화가 이 안에 있는 세션만 사무실에 띄운다.</summary>
    public TimeSpan Window => TimeSpan.FromTicks(Interlocked.Read(ref _windowTicks));

    public void SetWindow(TimeSpan window)
    {
        if (Interlocked.Exchange(ref _windowTicks, window.Ticks) != window.Ticks)
            _discoverNow = true;   // 늘렸으면 더 오래된 기록 파일을 3초 기다리지 않고 바로 찾아 들인다
    }

    /// <summary>
    /// 화면 언어를 바꿨을 때: 말풍선·최근 작업 문구는 읽을 때 만들어 쌓아 두므로, 기록 파일을 처음처럼 다시 읽어 새 언어로 만든다.
    /// (그대로 두면 쉬는 직원의 "대기 중" 같은 문구가 다음 일을 할 때까지 예전 언어로 남는다)
    /// </summary>
    public void Reparse() => _reparseNow = true;

    public void Start()
    {
        if (_cts != null) return;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _ = Task.Run(() => LoopAsync(ct));
    }

    public void Dispose() => _cts?.Cancel();

    private async Task LoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(PollInterval);
        int tick = 0;
        do
        {
            try
            {
                if (_reparseNow)
                {
                    _reparseNow = false;
                    ResetStates();
                }
                bool discoverTick = tick++ % DiscoverEveryTicks == 0 || _discoverNow;
                if (discoverTick)
                {
                    _discoverNow = false;
                    Discover();
                }
                if (_hookServer != null && (discoverTick || _hookCheckNow))
                {
                    _hookCheckNow = false;
                    _hooksInstalled = HookSetup.IsInstalled();
                }
                Poll();
                var now = DateTime.UtcNow;
                ApplyHooks(now);   // 기록 파일을 읽은 뒤에 해야 승인 요청을 그 도구 호출과 짝지을 수 있다

                var json = SnapshotBuilder.Build(_states.Values, now, Window, HookStatus());
                if (!string.Equals(json, _lastSnapshot, StringComparison.Ordinal))
                {
                    _lastSnapshot = json;
                    SnapshotChanged?.Invoke(json);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ClaudeOffice] 감시 중 오류: {ex}");
            }
        }
        while (await WaitTick(timer, ct));
    }

    private static async Task<bool> WaitTick(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }

    // ───────────────────────── 훅 ─────────────────────────

    private void ApplyHooks(DateTime now)
    {
        while (_hookQueue.TryDequeue(out var ev)) _hookBacklog.Add(ev);
        _hookBacklog.RemoveAll(ev => TryApply(ev, now) || now - ev.Utc > HookBacklogKeep);
        foreach (var s in _states.Values) HookApplier.Settle(s, now);
    }

    /// <summary>훅 이벤트를 해당 세션(또는 인턴)에 반영한다. 그 기록 파일을 아직 못 찾았으면 false → 다음 틱에 다시.</summary>
    private bool TryApply(HookEvent ev, DateTime now)
    {
        var session = _states.Values.FirstOrDefault(s => s.ParentId == null && s.Id.Equals(ev.SessionId, StringComparison.OrdinalIgnoreCase));
        if (session is null) return false;

        var interns = _states.Values.Where(s => s.ParentId != null && s.ParentId.Equals(session.Id, StringComparison.OrdinalIgnoreCase)).ToList();
        var target = session;
        if (ev.AgentId != null)
        {
            var intern = interns.FirstOrDefault(s => s.Id.Equals(ev.AgentId, StringComparison.OrdinalIgnoreCase));
            if (intern is null && now - ev.Utc < InternWait) return false;
            target = intern ?? session;   // 끝내 못 찾으면 세션에라도 표시(사람이 응답할 곳은 어차피 그 세션 창)
        }

        HookApplier.Apply(session, target, interns, ev);
        return true;
    }

    private HooksDto? HookStatus()
    {
        if (_hookServer is null) return null;   // 훅을 쓰지 않는 실행(점검 도구 등)
        var last = _hookServer.LastReceivedUtc;
        var state = !_hooksInstalled ? "off" : _hookServer.Listening ? "on" : "error";
        return new HooksDto(state, _hookServer.Error, SnapshotBuilder.Ms(last));
    }

    // ───────────────────────── 파일 찾기 ─────────────────────────

    private void Discover()
    {
        if (!Directory.Exists(_root)) return;
        var cutoff = DateTime.UtcNow - Window;

        foreach (var projDir in SafeDirs(_root))
            foreach (var file in SafeFiles(projDir, "*.jsonl"))
                Track(file, null, cutoff);

        // 인턴(서브에이전트)은 사무실에 있는 세션 것만 찾는다: <프로젝트>\<세션ID>\subagents\agent-*.jsonl
        foreach (var s in _states.Values.Where(x => x.ParentId == null).ToList())
        {
            var subDir = Path.Combine(Path.GetDirectoryName(s.FilePath)!, s.Id, "subagents");
            if (!Directory.Exists(subDir)) continue;
            foreach (var file in SafeFiles(subDir, "agent-*.jsonl"))
                Track(file, s.Id, cutoff);
        }

        // 지워졌거나 오래된 파일, 소속 세션이 사라진 인턴은 정리
        var sessionIds = _states.Values.Where(x => x.ParentId == null).Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var stale = _states
            .Where(kv => !File.Exists(kv.Key)
                         || kv.Value.FileWriteUtc < cutoff
                         || (kv.Value.ParentId != null && !sessionIds.Contains(kv.Value.ParentId)))
            .Select(kv => kv.Key)
            .ToList();
        foreach (var key in stale) _states.Remove(key);
    }

    private void Track(string file, string? parentId, DateTime cutoff)
    {
        if (_states.ContainsKey(file)) return;

        var fi = new FileInfo(file);
        if (!fi.Exists || fi.LastWriteTimeUtc < cutoff) return;

        var name = Path.GetFileNameWithoutExtension(file);
        var id = parentId != null && name.StartsWith("agent-", StringComparison.OrdinalIgnoreCase) ? name["agent-".Length..] : name;
        var s = new TranscriptState(file, id, parentId)
        {
            StartedUtc = fi.CreationTimeUtc,
            FileWriteUtc = fi.LastWriteTimeUtc,
        };
        if (parentId != null)
            ReadAgentMeta(s, Path.Combine(fi.DirectoryName!, name + ".meta.json"));

        _states[file] = s;
    }

    /// <summary>
    /// 모든 기록 파일을 처음 보는 것처럼 다시 읽게 한다(<see cref="Reparse"/>). 기록 파일에 없는 것 — 훅이 알려 준 대기·세션 종료,
    /// 인턴 메타 — 는 옮겨 둔다. 같은 틱의 Poll 이 바로 다시 읽으므로 화면에 빈 사무실이 보이지 않는다.
    /// </summary>
    private void ResetStates()
    {
        foreach (var (file, old) in _states.ToList())
        {
            _states[file] = new TranscriptState(old.FilePath, old.Id, old.ParentId)
            {
                StartedUtc = old.StartedUtc,
                FileWriteUtc = old.FileWriteUtc,
                AgentType = old.AgentType,
                Description = old.Description,
                Wait = old.Wait,
                EndedUtc = old.EndedUtc,
            };
        }
    }

    private static void ReadAgentMeta(TranscriptState s, string metaPath)
    {
        try
        {
            if (!File.Exists(metaPath)) return;
            using var doc = JsonDocument.Parse(File.ReadAllBytes(metaPath));
            s.AgentType = ToolCatalog.Str(doc.RootElement, "agentType");
            s.Description = ToolCatalog.Str(doc.RootElement, "description");
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // 메타가 없어도 인턴은 보여 준다
        }
    }

    // ───────────────────────── 늘어난 부분 읽기 ─────────────────────────

    private void Poll()
    {
        foreach (var s in _states.Values)
        {
            try
            {
                var fi = new FileInfo(s.FilePath);
                if (!fi.Exists) continue;
                s.FileWriteUtc = fi.LastWriteTimeUtc;

                long len = fi.Length;
                if (len < s.Offset)   // 파일이 통째로 다시 써짐
                {
                    s.Offset = 0;
                    s.Initialized = false;
                }
                if (len > s.Offset) ReadAppended(s, len);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 쓰는 중이라 잠깐 못 여는 경우 — 다음 틱에 다시
            }
        }
    }

    private static void ReadAppended(TranscriptState s, long len)
    {
        long start = s.Initialized ? s.Offset : Math.Max(0, len - InitialTailBytes);
        bool skipFirstLine = !s.Initialized && start > 0;   // 중간부터 읽으면 첫 줄은 잘린 줄
        if (len - start > MaxReadBytes)
        {
            start = len - MaxReadBytes;
            skipFirstLine = true;
        }

        // 처음 읽을 때 앞부분을 건너뛰면 그 안의 오늘 줄은 근무 일지(토큰 등)에만 따로 센다
        if (!s.Initialized && start > 0 && File.GetLastWriteTime(s.FilePath).Date == DateTime.Now.Date)
        {
            try { DayStats.ScanPrefix(s, start); }
            catch (IOException) { }
        }

        var buf = new byte[len - start];
        using (var fs = new FileStream(s.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            fs.Seek(start, SeekOrigin.Begin);
            fs.ReadExactly(buf);
        }

        int lastNl = Array.LastIndexOf(buf, (byte)'\n');
        if (lastNl < 0)
        {
            // 아직 줄이 끝나지 않았다. 처음 읽는데 줄바꿈이 하나도 없으면(초대형 한 줄) 그냥 넘어간다.
            if (!s.Initialized)
            {
                s.Offset = len;
                s.Initialized = true;
            }
            return;
        }

        int pos = 0;
        if (skipFirstLine) pos = Array.IndexOf(buf, (byte)'\n') + 1;

        while (pos <= lastNl)
        {
            int nl = Array.IndexOf(buf, (byte)'\n', pos, lastNl - pos + 1);
            int lineLen = nl - pos;
            if (lineLen > 0 && buf[pos + lineLen - 1] == (byte)'\r') lineLen--;
            if (lineLen > 0)
            {
                try { TranscriptParser.Apply(s, new ReadOnlyMemory<byte>(buf, pos, lineLen)); }
                catch (JsonException) { /* 깨진 줄은 건너뛴다 */ }
            }
            pos = nl + 1;
        }

        s.Offset = start + lastNl + 1;   // 끝나지 않은 마지막 줄은 다음에 다시 읽는다
        s.Initialized = true;
    }

    private static IEnumerable<string> SafeDirs(string path)
    {
        try { return Directory.EnumerateDirectories(path).ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Array.Empty<string>(); }
    }

    private static IEnumerable<string> SafeFiles(string path, string pattern)
    {
        try { return Directory.EnumerateFiles(path, pattern).ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Array.Empty<string>(); }
    }
}
