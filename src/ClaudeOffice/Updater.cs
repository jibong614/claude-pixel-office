using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using ClaudeOffice.Monitoring;

namespace ClaudeOffice;

/// <summary>
/// 앱 안에서 새 버전으로 바꾸기 — GitHub 릴리스 zip 만으로(업데이트 서버 없음).
///   1. 켜진 채로 zip 을 받고(진행률), 릴리스에 함께 올린 SHA-256 과 대조하고, 프로그램 폴더 안 임시 폴더에 푼다
///      (실패해도 지금 버전은 그대로 — 그래서 끄기 전에 다 받는다)
///   2. 실행 중인 exe 는 덮어쓸 수 없지만 이름은 바꿀 수 있다: 지금 파일·폴더를 *.old 로 비켜 두고 새 것을 그 자리로(하나라도 실패하면 되돌림)
///   3. 새 exe 를 --updated --wait=&lt;지금 프로세스&gt; 로 실행하고 지금 것은 끝낸다 → 새 것은 옛것이 끝나기를 기다렸다 시작하고 *.old 를 지운다
/// 자체 포함 단일 exe 배포본에서만(개발용 빌드는 dll 이 옆에 있다), 프로그램 폴더에 쓸 수 있을 때만.
/// </summary>
internal sealed class Updater
{
    private const string OldSuffix = ".old";
    private const string StagingPrefix = ".update-";

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(10) };

    public Updater() => _http.DefaultRequestHeaders.UserAgent.ParseAdd($"ClaudeOffice/{App.Version}");

    /// <summary>idle | downloading | installing | error</summary>
    public string State { get; private set; } = "idle";
    public int Percent { get; private set; }
    public string? Error { get; private set; }

    /// <summary>상태가 바뀔 때(진행률 포함). 내려받는 스레드에서 불린다.</summary>
    public event Action? Changed;

    private static string AppDir => AppContext.BaseDirectory;

    /// <summary>이 실행 파일이 스스로 바꿀 수 있는지. 안 되면 이유(그때는 릴리스 페이지를 연다).</summary>
    public static string? CannotReason()
    {
        if (File.Exists(Path.Combine(AppDir, "ClaudeOffice.dll")) || Environment.ProcessPath is null)
            return L.T("개발용 빌드라 자동으로 바꾸지 않습니다", "Development build — not updated automatically");
        try
        {
            var probe = Path.Combine(AppDir, ".write-test");
            File.WriteAllText(probe, "");
            File.Delete(probe);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return L.T("프로그램 폴더에 쓸 수 없어(예: Program Files) 자동으로 바꾸지 않습니다", "Can't write to the program folder (e.g. Program Files)");
        }
        return null;
    }

    /// <summary>받기 → 대조 → 풀기 → 바꾸기 → 새 버전 실행. 성공하면 true(부르는 쪽이 이 프로그램을 끝낸다).</summary>
    public async Task<bool> RunAsync(string version, string zipUrl, string shaUrl, IEnumerable<string> extraArgs)
    {
        if (State is "downloading" or "installing") return false;
        var staging = Path.Combine(AppDir, StagingPrefix + version);
        try
        {
            Set("downloading", 0, null);
            var expected = (await _http.GetStringAsync(shaUrl)).Trim().ToLowerInvariant();
            if (expected.Length != 64 || !expected.All(Uri.IsHexDigit))
                throw new InvalidDataException(L.T("확인용 SHA-256 파일 모양이 이상합니다", "The SHA-256 file looks wrong"));

            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            Directory.CreateDirectory(staging);
            var zip = Path.Combine(staging, "release.zip");
            await DownloadAsync(zipUrl, zip);

            Set("installing", 100, null);
            string actual;
            using (var fs = File.OpenRead(zip))
                actual = Convert.ToHexString(await SHA256.HashDataAsync(fs)).ToLowerInvariant();
            if (actual != expected)
                throw new InvalidDataException(L.T("받은 파일이 릴리스와 다릅니다(SHA-256 불일치) — 바꾸지 않았습니다", "Downloaded file doesn't match the release (SHA-256) — nothing changed"));

            var files = Path.Combine(staging, "files");
            ZipFile.ExtractToDirectory(zip, files);   // 폴더 밖으로 나가는 항목은 .NET 이 막는다
            var newExe = Path.Combine(files, "ClaudeOffice.exe");
            if (!File.Exists(newExe) || !File.Exists(Path.Combine(files, "wwwroot", "index.html")))
                throw new InvalidDataException(L.T("릴리스 zip 에 프로그램 파일이 없습니다", "The release zip has no program files"));
            var newVer = FileVersionInfo.GetVersionInfo(newExe).ProductVersion ?? "";
            if (!newVer.StartsWith(version, StringComparison.Ordinal))
                throw new InvalidDataException(L.T($"zip 안의 버전({newVer})이 {version} 이 아닙니다", $"The zip contains version {newVer}, not {version}"));

            Swap(files);
            var args = new List<string> { "--updated", $"--wait={Environment.ProcessId}" };
            args.AddRange(extraArgs);
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!, string.Join(' ', args)) { UseShellExecute = false, WorkingDirectory = AppDir });
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException
                                   or InvalidDataException or System.ComponentModel.Win32Exception)
        {
            Set("error", 0, ex is HttpRequestException or TaskCanceledException ? L.T($"받지 못했습니다: {ex.Message}", $"Download failed: {ex.Message}") : ex.Message);
            try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            return false;
        }
    }

    private async Task DownloadAsync(string url, string path)
    {
        using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();
        long total = resp.Content.Headers.ContentLength ?? 0, done = 0;
        await using var src = await resp.Content.ReadAsStreamAsync();
        await using var dst = File.Create(path);
        var buf = new byte[1 << 16];
        int n, lastPct = -1;
        while ((n = await src.ReadAsync(buf)) > 0)
        {
            await dst.WriteAsync(buf.AsMemory(0, n));
            done += n;
            int pct = total > 0 ? (int)(done * 100 / total) : 0;
            if (pct != lastPct) { lastPct = pct; Set("downloading", pct, null); }
        }
    }

    /// <summary>프로그램 폴더의 같은 이름 파일·폴더를 *.old 로 비켜 두고 새 것을 옮긴다(같은 드라이브라 이동은 이름 바꾸기). 실패하면 되돌린다.</summary>
    private static void Swap(string files)
    {
        var moved = new List<(string Target, string Old)>();
        var placed = new List<string>();
        try
        {
            foreach (var src in Directory.EnumerateFileSystemEntries(files))
            {
                var target = Path.Combine(AppDir, Path.GetFileName(src));
                var old = target + OldSuffix;
                DeletePath(old);   // 지난번 업데이트에서 못 지운 것
                if (File.Exists(target)) { File.Move(target, old); moved.Add((target, old)); }
                else if (Directory.Exists(target)) { Directory.Move(target, old); moved.Add((target, old)); }
                if (Directory.Exists(src)) Directory.Move(src, target); else File.Move(src, target);
                placed.Add(target);
            }
        }
        catch
        {
            foreach (var p in placed) try { DeletePath(p); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            foreach (var (target, old) in moved)
                try { if (Directory.Exists(old)) Directory.Move(old, target); else File.Move(old, target); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    /// <summary>새 버전이 처음 켜질 때: 비켜 둔 옛 파일과 임시 폴더를 지운다(옛 프로세스가 막 끝나 잠겨 있으면 잠시 뒤 다시).</summary>
    public static void CleanupLater() => _ = Task.Run(async () =>
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            bool left = false;
            foreach (var p in Directory.EnumerateFileSystemEntries(AppDir))
            {
                var name = Path.GetFileName(p);
                if (!name.EndsWith(OldSuffix, StringComparison.Ordinal) && !name.StartsWith(StagingPrefix, StringComparison.Ordinal)) continue;
                try { DeletePath(p); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { left = true; }
            }
            if (!left) return;
            await Task.Delay(3000);
        }
    });

    private static void DeletePath(string p)
    {
        if (Directory.Exists(p)) Directory.Delete(p, true);
        else if (File.Exists(p)) File.Delete(p);
    }

    private void Set(string state, int percent, string? error)
    {
        State = state;
        Percent = percent;
        Error = error;
        Changed?.Invoke();
    }
}
