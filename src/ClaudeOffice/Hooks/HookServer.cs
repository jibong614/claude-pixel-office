using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using ClaudeOffice.Monitoring;

namespace ClaudeOffice.Hooks;

/// <summary>
/// Claude Code 훅 이벤트를 받는 아주 작은 HTTP 수신기(127.0.0.1 전용).
///
/// HttpListener 대신 TcpListener를 쓰는 이유: HttpListener(HTTP.sys)는 주소에 따라 관리자 권한이나
/// URL 예약(netsh)을 요구한다. 받는 것은 curl이 보내는 "POST /hook + JSON 본문" 하나뿐이라 직접 읽는 편이 단순하다.
/// 응답은 언제나 본문 없는 200 — Claude Code에는 "아무 결정 없음"이므로 승인·작업 흐름에 끼어들지 않는다.
/// </summary>
internal sealed class HookServer : IDisposable
{
    public const int Port = 47821;

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

    public bool Listening { get; private set; }
    public string? Error { get; private set; }
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
                : L.T($"훅 수신을 시작하지 못했습니다: {ex.Message}", $"Couldn't start the hook receiver: {ex.Message}");
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
            if (request.Length < 2 || request[0] != "POST" || !request[1].StartsWith("/hook", StringComparison.Ordinal))
            {
                await stream.WriteAsync(NotFound, token);
                return;
            }

            // curl --data-binary 는 본문을 다 읽은 뒤 보내므로 항상 Content-Length 가 붙는다(chunked 는 오지 않음)
            int length = -1;
            bool expectContinue = false;
            foreach (var line in lines.Skip(1))
            {
                int colon = line.IndexOf(':');
                if (colon <= 0) continue;
                var name = line[..colon].Trim();
                var value = line[(colon + 1)..].Trim();
                if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) int.TryParse(value, out length);
                else if (name.Equals("Expect", StringComparison.OrdinalIgnoreCase)) expectContinue = value.Equals("100-continue", StringComparison.OrdinalIgnoreCase);
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
                if (expectContinue) await stream.WriteAsync(Continue, token);   // 본문이 크면 curl이 먼저 물어본다
                await stream.ReadExactlyAsync(body.AsMemory(have), token);
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
