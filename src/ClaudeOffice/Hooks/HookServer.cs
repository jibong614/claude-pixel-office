using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ClaudeOffice.Monitoring;

namespace ClaudeOffice.Hooks;

/// <summary>
/// Claude Code 훅 이벤트를 받는 아주 작은 HTTP 수신기(127.0.0.1 전용).
///
/// HttpListener 대신 TcpListener를 쓰는 이유: HttpListener(HTTP.sys)는 주소에 따라 관리자 권한이나
/// URL 예약(netsh)을 요구한다. 받는 것은 curl이 보내는 "POST /hook + JSON 본문" 하나뿐이라 직접 읽는 편이 단순하다.
/// /hook 응답은 언제나 본문 없는 200 — Claude Code에는 "아무 결정 없음"이므로 승인·작업 흐름에 끼어들지 않는다.
/// /remote/listen·/remote/approve 는 자리 비움 원격(RemoteHub)이 답을 정할 때까지 연결을 붙잡아 둔다.
/// 브라우저가 보낸 요청(Origin 헤더)은 받지 않는다 — 웹 페이지가 이 포트로 가짜 이벤트를 보내지 못하게.
/// </summary>
internal sealed class HookServer : IDisposable
{
    /// <summary>훅 포트. CLAUDEOFFICE_HOOK_PORT 는 점검용(실제로 켜 둔 Claude Office 와 겹치지 않게 시험할 때).</summary>
    public static readonly int Port =
        int.TryParse(Environment.GetEnvironmentVariable("CLAUDEOFFICE_HOOK_PORT"), out var p) && p is > 1024 and < 65536 ? p : 47821;

    private const int MaxHeaderBytes = 16 * 1024;
    private const int MaxBodyBytes = 16 * 1024 * 1024;   // Write 도구로 큰 파일을 쓰면 그 내용이 통째로 실려 온다
    private const long LogMaxBytes = 1024 * 1024;
    private static readonly TimeSpan ClientTimeout = TimeSpan.FromSeconds(5);

    private static readonly byte[] Ok = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
    private static readonly byte[] NotFound = Encoding.ASCII.GetBytes("HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
    private static readonly byte[] TooLarge = Encoding.ASCII.GetBytes("HTTP/1.1 413 Payload Too Large\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
    private static readonly byte[] Continue = Encoding.ASCII.GetBytes("HTTP/1.1 100 Continue\r\n\r\n");

    private readonly string? _logPath;
    private readonly object _logLock = new();
    private readonly CancellationTokenSource _cts = new();
    private TcpListener? _listener;
    private long _lastReceivedTicks;

    /// <param name="logPath">받은 이벤트를 한 줄씩 남길 파일(훅이 실제로 언제·무엇이 오는지 확인용). null이면 남기지 않는다.</param>
    public HookServer(string? logPath) => _logPath = logPath;

    /// <summary>이벤트를 받을 때마다 수신 스레드에서 호출된다.</summary>
    public event Action<HookEvent>? Received;

    /// <summary>Claude 데스크톱 앱 MCP 도구(office_report_status)로 상태가 전달되었을 때 호출된다.</summary>
    public event Action<McpUpdateEvent>? McpUpdated;

    /// <summary>MCP office_get_status 도구 요청 시 돌려줄 현재 사무실 상태 요약 JSON 제공자.</summary>
    public Func<string>? OfficeSummaryProvider { get; set; }

    /// <summary>자리 비움 원격(/remote/...). 없으면 원격 요청에 바로 빈 답.</summary>
    public Remote.RemoteHub? Remote { get; set; }

    private volatile bool _listening;
    private volatile string? _error;
    public bool Listening { get => _listening; private set => _listening = value; }
    public string? Error { get => _error; private set => _error = value; }
    public DateTime LastReceivedUtc => new(Interlocked.Read(ref _lastReceivedTicks), DateTimeKind.Utc);

    public void Start()
    {
        if (_listener != null) return;
        try
        {
            // 다른 프로그램이 같은 포트를 겹쳐 잡지 못하게 한다(이벤트가 엉뚱한 곳으로 새지 않도록)
            var l = new TcpListener(IPAddress.Loopback, Port) { ExclusiveAddressUse = true };
            l.Start();
            _listener = l;
            Listening = true;
            Error = null;
            _ = AcceptLoopAsync(l, _cts.Token);
        }
        catch (SocketException ex)
        {
            Error = ex.SocketErrorCode == SocketError.AddressAlreadyInUse
                ? L.T($"{Port} 포트를 이미 쓰는 프로그램이 있습니다(Claude Office가 두 개 켜져 있나요?)",
                      $"Another program is using port {Port} (is Claude Office running twice?)")
                : L.T($"Claude Code 알림을 받을 준비를 하지 못했습니다: {ex.Message}", $"Couldn't start listening for Claude Code: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener?.Stop();
        Listening = false;
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(ct); }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException) { return; }
            catch (SocketException ex) { Debug.WriteLine($"[ClaudeOffice] 훅 연결 받기 실패: {ex.Message}"); continue; }
            _ = HandleAsync(client, ct);
        }
    }

    private async Task HandleAsync(TcpClient client, CancellationToken ct)
    {
        using var _ = client;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(ClientTimeout);   // 붙기만 하고 아무것도 안 보내는 연결이 쌓이지 않게
        var token = cts.Token;
        try
        {
            var stream = client.GetStream();

            // 머리(요청 줄 + 헤더)는 빈 줄까지
            var buf = new byte[4096];
            int filled = 0, headEnd;
            while ((headEnd = buf.AsSpan(0, filled).IndexOf("\r\n\r\n"u8)) < 0)
            {
                if (filled == buf.Length)
                {
                    if (buf.Length >= MaxHeaderBytes) return;
                    Array.Resize(ref buf, buf.Length * 2);
                }
                int n = await stream.ReadAsync(buf.AsMemory(filled), token);
                if (n == 0) return;
                filled += n;
            }

            var lines = Encoding.ASCII.GetString(buf, 0, headEnd).Split("\r\n");
            var request = lines[0].Split(' ');
            var method = request.Length >= 1 ? request[0] : "";
            var path = request.Length >= 2 ? request[1] : "";
            string? remote = path switch { "/remote/listen" => "listen", "/remote/approve" => "approve", "/remote/pretool" => "pretool", _ => null };
            bool isMcpUpdate = method == "POST" && path == "/mcp/update";
            bool isMcpStatus = (method == "GET" || method == "POST") && path == "/mcp/status";
            bool isMcpInstruction = (method == "GET" || method == "POST") && (path == "/mcp/instruction" || path.StartsWith("/mcp/instruction?"));
            if (request.Length < 2 || (method != "POST" && !isMcpStatus && !isMcpInstruction) || (remote is null && path != "/hook" && !isMcpUpdate && !isMcpStatus && !isMcpInstruction))
            {
                await stream.WriteAsync(NotFound, token);
                return;
            }

            // curl --data-binary
            int length = -1;
            bool expectContinue = false, fromBrowser = false, json = false;
            string? hookToken = null;
            foreach (var line in lines.Skip(1))
            {
                int colon = line.IndexOf(':');
                if (colon <= 0) continue;
                var name = line[..colon].Trim();
                var value = line[(colon + 1)..].Trim();
                if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) length = int.TryParse(value, out var parsed) ? parsed : -1;
                else if (name.Equals("Expect", StringComparison.OrdinalIgnoreCase)) expectContinue = value.Equals("100-continue", StringComparison.OrdinalIgnoreCase);
                else if (name.Equals("Origin", StringComparison.OrdinalIgnoreCase)
                      || name.Equals("Referer", StringComparison.OrdinalIgnoreCase)
                      || name.StartsWith("Sec-Fetch-", StringComparison.OrdinalIgnoreCase)) fromBrowser = true;
                else if (name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)) json = value.StartsWith("application/json", StringComparison.OrdinalIgnoreCase);
                else if (name.Equals("X-ClaudeOffice-Token", StringComparison.OrdinalIgnoreCase)) hookToken = value;
            }
            //
            if (fromBrowser || (hookToken != null && !HookSetup.ValidToken(hookToken)) || (!isMcpStatus && !isMcpInstruction && !json))
            {
                if (remote != "listen") await stream.WriteAsync(NotFound, token);
                return;
            }

            if (isMcpStatus)
            {
                var summary = OfficeSummaryProvider?.Invoke() ?? "{}";
                var respBytes = Encoding.UTF8.GetBytes(summary);
                var header = $"HTTP/1.1 200 OK\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: {respBytes.Length}\r\nConnection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(header), token);
                await stream.WriteAsync(respBytes, token);
                return;
            }

            if (length < 0 || length > MaxBodyBytes)
            {
                await stream.WriteAsync(TooLarge, token);
                return;
            }

            var body = new byte[length];
            int bodyStart = headEnd + 4;
            int have = Math.Min(filled - bodyStart, length);
            Array.Copy(buf, bodyStart, body, 0, have);
            if (have < length)
            {
                if (expectContinue) await stream.WriteAsync(Continue, token);
                await stream.ReadExactlyAsync(body.AsMemory(have), token);
            }

            if (isMcpUpdate)
            {
                string? instruction = null;
                try
                {
                    using var doc = JsonDocument.Parse(body);
                    var r = doc.RootElement;
                    var proj = ToolCatalog.Str(r, "project_name") ?? ToolCatalog.Str(r, "projectName");
                    var sess = ToolCatalog.Str(r, "session_name") ?? ToolCatalog.Str(r, "sessionName");
                    var st = ToolCatalog.Str(r, "status");
                    var msg = ToolCatalog.Str(r, "message");
                    var detail = ToolCatalog.Str(r, "detail");
                    McpUpdated?.Invoke(new McpUpdateEvent(proj, sess, st, msg, detail, DateTime.UtcNow));

                    var projClean = !string.IsNullOrWhiteSpace(proj) ? proj.Trim() : "Claude Desktop";
                    var sessClean = !string.IsNullOrWhiteSpace(sess) ? sess.Trim() : "프로젝트";
                    var key = "virtual:mcp:" + projClean.ToLowerInvariant() + ":" + sessClean.ToLowerInvariant();
                    var sid = "mcp_" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key)))[..8].ToLowerInvariant();
                    instruction = Remote?.PopPendingInstruction(sid, proj, sess);
                }
                catch { }

                var respJson = instruction != null
                    ? $"{{\"ok\":true,\"instruction\":{JsonSerializer.Serialize(instruction)}}}"
                    : "{\"ok\":true}";
                var respBytes = Encoding.UTF8.GetBytes(respJson);
                var header = $"HTTP/1.1 200 OK\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: {respBytes.Length}\r\nConnection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(header), token);
                await stream.WriteAsync(respBytes, token);
                return;
            }

            if (isMcpInstruction)
            {
                string? proj = null;
                string? sess = null;
                if (length > 0)
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(body);
                        var r = doc.RootElement;
                        proj = ToolCatalog.Str(r, "project_name") ?? ToolCatalog.Str(r, "projectName");
                        sess = ToolCatalog.Str(r, "session_name") ?? ToolCatalog.Str(r, "sessionName");
                    }
                    catch { }
                }
                var projClean = !string.IsNullOrWhiteSpace(proj) ? proj.Trim() : "Claude Desktop";
                var sessClean = !string.IsNullOrWhiteSpace(sess) ? sess.Trim() : "프로젝트";
                var key = "virtual:mcp:" + projClean.ToLowerInvariant() + ":" + sessClean.ToLowerInvariant();
                var sid = "mcp_" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key)))[..8].ToLowerInvariant();
                var instruction = Remote?.PopPendingInstruction(sid, proj, sess);

                var respJson = instruction != null
                    ? $"{{\"ok\":true,\"instruction\":{JsonSerializer.Serialize(instruction)}}}"
                    : "{\"ok\":true,\"instruction\":null}";
                var respBytes = Encoding.UTF8.GetBytes(respJson);
                var header = $"HTTP/1.1 200 OK\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: {respBytes.Length}\r\nConnection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(header), token);
                await stream.WriteAsync(respBytes, token);
                return;
            }

            if (remote != null)
            {
                await HandleRemoteAsync(stream, remote, body, ct);
                return;
            }

            await stream.WriteAsync(Ok, token);   // 먼저 답해서 curl(=훅)이 바로 끝나게 한다

            var ev = HookEvent.TryParse(body, DateTime.UtcNow);
            if (ev is null) return;
            Interlocked.Exchange(ref _lastReceivedTicks, ev.Utc.Ticks);
            Log(ev);
            Received?.Invoke(ev);
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException or EndOfStreamException)
        {
            // 연결이 끊기거나 시간 초과 — 훅 쪽은 exit 0 으로 끝나므로 여기서도 그냥 버린다
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ClaudeOffice] 훅 처리 중 오류: {ex}");
        }
    }

    /// <summary>
    /// 원격 요청: 답이 정해질 때까지(몇 시간일 수도) 연결을 붙잡아 둔다. 훅이 끝나거나(시간 초과·세션 종료) 죽으면 연결이 닫히는데,
    /// 그걸 알아채려고 남은 입력을 계속 읽어 둔다(curl 은 본문 뒤로 더 보내지 않으므로 읽힘 = 끊김).
    /// </summary>
    private async Task HandleRemoteAsync(NetworkStream stream, string kind, byte[] body, CancellationToken ct)
    {
        using var gone = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _ = Task.Run(async () =>
        {
            try
            {
                var one = new byte[1];
                while (await stream.ReadAsync(one, gone.Token) > 0) { }
            }
            catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException) { }
            try { gone.Cancel(); } catch (ObjectDisposedException) { }
        });

        var reply = Remote is { } hub ? await hub.HandleAsync(kind, body, gone.Token) : [];
        if (gone.IsCancellationRequested) return;
        var head = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Length: {reply.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(head, ct);
        await stream.WriteAsync(reply, ct);
        await stream.FlushAsync(ct);
        gone.Cancel();   // 끊김 감시도 끝낸다
    }

    private void Log(HookEvent ev)
    {
        if (_logPath is null) return;
        var line = $"{ev.Utc.ToLocalTime():yyyy-MM-dd HH:mm:ss.fff}\t{ev.Event}\t{ev.SessionId}\t{ev.AgentId ?? "-"}\t{ev.ToolName ?? "-"}\t{ev.ToolText ?? "-"}\t{ev.NotificationType ?? "-"}\t{ev.Reason ?? "-"}\r\n";
        lock (_logLock)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
                var fi = new FileInfo(_logPath);
                if (fi.Exists && fi.Length > LogMaxBytes)
                    File.Move(_logPath, Path.ChangeExtension(_logPath, ".old.log"), overwrite: true);
                File.AppendAllText(_logPath, line);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 기록 못 해도 표시엔 지장 없음
            }
        }
    }
}

/// <summary>Claude 데스크톱 앱 MCP 도구(office_report_status)로 수신한 프로젝트 세션 갱신 정보.</summary>
internal sealed record McpUpdateEvent(
    string? ProjectName,
    string? SessionName,
    string? Status,
    string? Message,
    string? Detail,
    DateTime Utc);
