using System.Net.Http;
using System.Text.Json;
using ClaudeOffice.Monitoring;

namespace ClaudeOffice;

/// <summary>
/// 새 버전 알림: GitHub 릴리스의 최신 버전을 켠 뒤 30초, 그 뒤로 12시간마다 확인한다(설정에서 끌 수 있음).
/// 화면이 설정을 열 때 마지막 확인이 10분 넘었으면 "update-check" 로 한 번 더 부른다.
/// 보내는 것은 익명 GET 하나(GitHub API 가 요구하는 User-Agent 에 버전만). 받아서 바꾸는 일은 Updater(사용자가 [업데이트]를 누를 때).
/// </summary>
internal sealed class UpdateChecker : IDisposable
{
    public const string Repo = "jibong614/claude-pixel-office";
    public const string ReleasesUrl = $"https://github.com/{Repo}/releases/latest";

    private static readonly TimeSpan FirstDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(12);

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly CancellationTokenSource _cts = new();
    private int _running;

    public UpdateChecker()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"ClaudeOffice/{App.Version}");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    /// <summary>최신 버전(예: 1.0.8). 아직 모르면 null.</summary>
    public string? Latest { get; private set; }
    public string Url { get; private set; } = ReleasesUrl;
    public string? Error { get; private set; }
    public long CheckedMs { get; private set; }

    /// <summary>최신 릴리스의 zip·SHA-256 파일 주소(없으면 null — 그때는 릴리스 페이지에서 받게).</summary>
    public string? ZipUrl { get; private set; }
    public string? ShaUrl { get; private set; }

    public bool IsNewer => Version.TryParse(Latest, out var l) && Version.TryParse(App.Version, out var c) && l > c;

    /// <summary>확인을 마칠 때마다(새 버전이 없어도). 확인 스레드에서 불린다.</summary>
    public event Action? Checked;

    public void Start() => _ = Task.Run(async () =>
    {
        try
        {
            await Task.Delay(FirstDelay, _cts.Token);
            while (!_cts.IsCancellationRequested)
            {
                if (App.Settings.UpdateCheck) await CheckAsync();
                await Task.Delay(Interval, _cts.Token);
            }
        }
        catch (OperationCanceledException) { }
    });

    public async Task CheckAsync()
    {
        if (Interlocked.Exchange(ref _running, 1) == 1) return;
        try
        {
            using var resp = await _http.GetAsync($"https://api.github.com/repos/{Repo}/releases/latest", _cts.Token);
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) { Error = null; Latest = null; }   // 아직 릴리스가 없음
            else if (!resp.IsSuccessStatusCode) Error = L.T($"확인 실패({(int)resp.StatusCode})", $"Check failed ({(int)resp.StatusCode})");
            else
            {
                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(_cts.Token));
                var r = doc.RootElement;
                Latest = r.GetProperty("tag_name").GetString()?.TrimStart('v', 'V');
                if (r.TryGetProperty("html_url", out var u) && u.GetString() is { } url && url.StartsWith("https://github.com/", StringComparison.Ordinal))
                    Url = url;
                // 앱 안 업데이트용: 릴리스에 붙은 zip 과 그 SHA-256 파일(자동 빌드가 함께 올린다). 둘 다 이 저장소의 github.com 주소만
                ZipUrl = ShaUrl = null;
                if (r.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                    foreach (var a in assets.EnumerateArray())
                    {
                        var name = a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                        var dl = a.TryGetProperty("browser_download_url", out var d) ? d.GetString() : null;
                        if (dl is null || !dl.StartsWith($"https://github.com/{Repo}/releases/download/", StringComparison.Ordinal)) continue;
                        if (name.EndsWith("-win-x64.zip", StringComparison.OrdinalIgnoreCase)) ZipUrl = dl;
                        else if (name.EndsWith("-win-x64.zip.sha256", StringComparison.OrdinalIgnoreCase)) ShaUrl = dl;
                    }
                Error = null;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            if (_cts.IsCancellationRequested) return;
            Error = L.T("확인 실패(인터넷 연결?)", "Check failed (no internet?)");
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
        }
        CheckedMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Checked?.Invoke();
    }

    public void Dispose() => _cts.Cancel();
}
