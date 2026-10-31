using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClaudeOffice.Monitoring;
using ClaudeOffice.Remote;

namespace ClaudeOffice.Cloud;

/// <summary>
/// 휴대폰 연결: 화면용 요약(스냅샷 + 사무실 이름·별명·메모)을 암호화해 무료 서버(모두가 함께 쓰는 Firebase Realtime Database)에 올린다.
///   - 대화 원본(jsonl)은 올리지 않는다. 요약(수 KB)만, 그것도 PC에서 gzip 후 AES-256-GCM 으로 암호화해서(키는 휴대폰 QR 에만)
///   - 바뀔 때만, 최대 3초에 한 번. 바뀐 게 없어도 1분마다 한 번(휴대폰이 PC가 살아 있는지 알 수 있게)
///   - 로그인은 Firebase 익명 로그인: 처음 한 번 사용자를 만들고 그 ID(uid)가 데이터 경로 offices/&lt;uid&gt; 가 된다
///   - 보내기만 하므로 PC에 들어오는 포트를 열 필요가 없다
/// </summary>
internal sealed partial class CloudSync : IDisposable
{
    private static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan RetryAfterError = TimeSpan.FromSeconds(30);

    private static readonly JsonSerializerOptions StatusJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string _configPath;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _wake = new(0);
    private readonly object _lock = new();

    // _lock 으로 지키는 것(화면 스레드·감시 스레드에서 들어온다)
    private CloudConfig _cfg;
    private string? _snapshot;
    private string _share = "{}";

    // 주로 올리기 루프가 만진다. 설정을 바꿀 때 화면 스레드가 비우기만 한다(어긋나도 한 번 더 올리는 정도라 잠그지 않음)
    private string? _lastSentKey;
    private DateTime _lastSentUtc, _nextTryUtc;
    private string? _idToken;
    private DateTime _idTokenExpiresUtc;
    private volatile bool _clearPending;   // 껐을 때 클라우드에 남은 요약을 지워야 함

    // 화면에 알릴 상태
    private volatile string _state = "off";   // off | setup | starting | on | error
    private volatile string? _error, _notice;
    private long _lastOkMs;

    // 화면 언어(ko | en) — 휴대폰이 PC와 같은 언어로 그리도록 요약에 함께 싣는다. 앱이 정해 준다(여기서는 App 을 모른다)
    private volatile string _lang = "ko";

    public CloudSync(string dataDir)
    {
        _configPath = Path.Combine(dataDir, "cloud.json");
        _cfg = CloudConfig.Load(_configPath);
        if (_cfg.Moved)
            _notice = L.T("📦 무료 서버로 옮겼어요 — 휴대폰에서 QR을 한 번 다시 찍어 주세요(내 Firebase 프로젝트는 이제 쓰지 않아요)",
                          "📦 Moved to the free server — scan the QR on your phone once more (your own Firebase project is no longer used)");
    }

    /// <summary>휴대폰에서 고친 별명·메모(해독한 것). 올리기 스레드에서 불린다.</summary>
    public event Action<List<NoteEdit>>? NotesFromPhone;

    /// <summary>휴대폰에서 보낸 원격 지시·승인(해독한 것). 올리기 스레드에서 불린다.</summary>
    public event Action<List<RemoteMsg>>? RemoteFromPhone;

    // 자리 비움 원격 상태(RemoteHub.StateJson) — 휴대폰이 승인 요청·답·지시 처리 상태를 보도록 요약과 함께 올린다
    private string? _remote;
    private volatile bool _remoteInstalled, _remoteAway;

    /// <summary>상태가 바뀔 때마다 화면에 보낼 JSON({type:"cloud", ...}). 올리기 스레드나 화면 스레드에서 불린다.</summary>
    public event Action<string>? StatusChanged;

    public void Start() => _ = Task.Run(() => LoopAsync(_cts.Token));

    public void Dispose() => _cts.Cancel();

    /// <summary>감시기가 만든 새 스냅샷.</summary>
    public void OnSnapshot(string json)
    {
        lock (_lock) _snapshot = json;
        Wake();
    }

    /// <summary>화면이 보낸 사무실 이름·별명·메모({office, notes}). 휴대폰에서도 같은 이름으로 보이게 함께 올린다.</summary>
    public void OnShare(string json)
    {
        lock (_lock) _share = json;
        Wake();
    }

    /// <summary>
    /// 원격 상태. 원격 훅이 있으면 휴대폰이 보낸 것을 받아야 하므로 inbox 를 보고, 자리 비움 중에는 더 자주(2초) 본다.
    /// </summary>
    public void OnRemote(string json, bool installed, bool away)
    {
        lock (_lock) _remote = json;
        bool wasAway = _remoteAway;
        _remoteInstalled = installed;
        _remoteAway = away;
        if (away && !wasAway) _nextInboxUtc = default;   // 막 자리를 비웠으면 바로 보기 시작
        Wake();
    }

    /// <summary>화면 언어를 바꾼다. 바뀌면 다음 올리기에 실린다(요약이 그대로여도 다시 올린다).</summary>
    public void SetLanguage(string lang)
    {
        _lang = lang == "en" ? "en" : "ko";
        Wake();
    }

    /// <summary>점검 도구 전용: 무료 서버 대신 이 PC 의 시험 서버(fake-rtdb.py)를 쓴다. 화면 파일도 그 서버에서. 문제가 있으면 이유를 돌려준다.</summary>
    public string? Setup(string text)
    {
        var (db, error) = CloudConfig.ParseLocal(text);
        if (error != null) return error;
        Update(c =>
        {
            c.ApiKey = c.ProjectId = null;
            c.DatabaseUrl = c.PageUrl = db;
            c.Key ??= CloudConfig.NewKey();
            c.Uid = c.RefreshToken = null;
        });
        _idToken = null;
        _lastSentKey = null;
        Publish();
        Wake();
        return null;
    }

    public void SetEnabled(bool on)
    {
        Update(c =>
        {
            c.Enabled = on;
            c.Key ??= CloudConfig.NewKey();
        });
        _lastSentKey = null;
        _notice = null;
        _nextTryUtc = default;
        _clearPending = !on;   // 끄면 마지막으로 올린 암호문도 지운다(꺼 둔 동안 남아 있지 않게)
        Publish();
        Wake();
    }

    /// <summary>휴대폰에서 별명·메모 고치기 허용.</summary>
    public void SetPhoneEdit(bool on)
    {
        Update(c => c.PhoneEdit = on);
        _lastSentKey = null;   // 휴대폰이 입력칸을 열 수 있게 바로 다시 올린다
        Publish();
        Wake();
    }

    /// <summary>키를 새로 만든다 — 지금 휴대폰은 더 이상 해독하지 못하고 QR을 다시 찍어야 한다.</summary>
    public void Rekey()
    {
        Update(c => c.Key = CloudConfig.NewKey());
        _lastSentKey = null;
        Publish();
        Wake();
    }

    /// <summary>
    /// 프로그램을 끝낼 때: 클라우드 값에 "꺼짐" 시각(off)만 덧붙여(PATCH) 휴대폰이 1분 반을 기다리지 않고 바로 알게 한다.
    /// 다음에 켜져 통째로 올리면(PUT) off 는 저절로 사라진다. 끝내는 길을 오래 막지 않게 timeout 안에서만.
    /// </summary>
    public void SendOffline(TimeSpan timeout)
    {
        CloudConfig cfg;
        lock (_lock) cfg = _cfg;
        if (!cfg.Enabled || !cfg.Configured || cfg.Uid is null || _lastSentUtc == default) return;   // 올린 적이 없으면 알릴 것도 없다
        try
        {
            var ms = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            Task.Run(async () =>
            {
                var token = await GetTokenAsync(CancellationToken.None);
                using var req = new HttpRequestMessage(HttpMethod.Patch, OfficeUrl(cfg, token))
                {
                    Content = new StringContent($"{{\"off\":{ms}}}", Encoding.UTF8, "application/json"),
                };
                using var resp = await _http.SendAsync(req);
            }).Wait(timeout);
        }
        catch (AggregateException)
        {
            // 못 알려도 휴대폰은 1분 반 뒤 "PC 응답 없음"으로 안다
        }
    }

    public string StatusJson()
    {
        CloudConfig c;
        lock (_lock) c = _cfg;
        return JsonSerializer.Serialize(new
        {
            type = "cloud",
            configured = c.Configured,
            enabled = c.Enabled,
            state = _state,
            error = _error,
            notice = _notice,
            last = Interlocked.Read(ref _lastOkMs),
            local = c.IsLocal,
            phoneEdit = c.PhoneEdit,
            url = MobileUrl(c),
        }, StatusJsonOptions);
    }

    /// <summary>휴대폰에서 열 주소. 데이터 주소·경로·키는 모두 # 뒤에 둬서 웹 서버(GitHub Pages)로는 전송되지 않는다.</summary>
    private static string? MobileUrl(CloudConfig c)
    {
        if (!c.Configured || c.Key is null || c.Uid is null) return null;
        var page = c.PageUrl ?? CloudConfig.PublicPageUrl;
        return $"{page.TrimEnd('/')}/#db={Uri.EscapeDataString(c.DatabaseUrl!)}&u={Uri.EscapeDataString(c.Uid)}&k={c.Key}";
    }

    private void Update(Action<CloudConfig> change)
    {
        lock (_lock)
        {
            var c = _cfg.Clone();
            change(c);
            _cfg = c;
            c.Save(_configPath);
        }
    }

    private void Wake()
    {
        if (_wake.CurrentCount == 0) _wake.Release();
    }

    private void Publish() => StatusChanged?.Invoke(StatusJson());

    private void SetState(string state, string? error)
    {
        if (_state == state && _error == error) return;
        _state = state;
        _error = error;
        Publish();
    }

    // ───────────────────────── 올리기 ─────────────────────────

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await _wake.WaitAsync(TimeSpan.FromSeconds(1), ct); }
            catch (OperationCanceledException) { return; }

            try
            {
                await TickAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is HttpRequestException or CloudException or TaskCanceledException or JsonException or IOException)
            {
                _nextTryUtc = DateTime.UtcNow + RetryAfterError;
                SetState("error", ex is CloudException ? ex.Message : L.T($"연결 실패: {ex.Message}", $"Connection failed: {ex.Message}"));
            }
        }
    }

    // 스냅샷 맨 앞의 "now" 만 바뀐 것은 내용이 바뀐 게 아니다
    [GeneratedRegex(@"^\{""now"":\d+,")]
    private static partial Regex NowField();

    private async Task TickAsync(CancellationToken ct)
    {
        CloudConfig cfg;
        string? snap, remote;
        string share;
        lock (_lock) { cfg = _cfg; snap = _snapshot; share = _share; remote = _remote; }

        if (!cfg.Configured) { SetState("setup", null); return; }
        var now = DateTime.UtcNow;
        if (!cfg.Enabled)
        {
            if (_clearPending && cfg.Uid != null && now >= _nextTryUtc)
            {
                var clearToken = await GetTokenAsync(ct);
                using var del = await _http.DeleteAsync(OfficeUrl(cfg, clearToken), ct);
                if (!del.IsSuccessStatusCode) throw new CloudException(L.T($"끄면서 클라우드의 요약을 지우지 못했습니다({(int)del.StatusCode}) — 30초 뒤 다시 시도",
                                                                           $"Couldn't delete the summary from the cloud while turning off ({(int)del.StatusCode}) — retrying in 30 s"));
                _clearPending = false;
            }
            SetState("off", null);
            return;
        }
        if (now < _nextTryUtc) return;
        if ((cfg.PhoneEdit || _remoteInstalled) && cfg.Uid != null && now >= _nextInboxUtc)
        {
            _nextInboxUtc = now + (_remoteAway ? InboxEveryAway : InboxEvery);
            await PollInboxAsync(ct);
        }
        if (snap is null) return;
        if (_state is "off" or "setup") SetState("starting", null);

        var lang = _lang;
        var key = NowField().Replace(snap, "{") + "\n" + share + "\n" + remote + "\n" + cfg.Key + cfg.PhoneEdit + lang;   // 언어만 바꿔도 다시 올린다
        bool changed = key != _lastSentKey;
        if (!changed && now - _lastSentUtc < Heartbeat) return;
        if (changed && now - _lastSentUtc < MinInterval) return;   // 1초 뒤 다시 깨어나 보낸다

        var token = await GetTokenAsync(ct);
        lock (_lock) cfg = _cfg;   // 로그인하며 Uid 가 생겼을 수 있다

        // edit: 휴대폰에서 별명·메모를 고칠 수 있는지(휴대폰이 입력칸을 열지), lang: 화면 언어(다르면 휴대폰이 그 언어로 다시 연다),
        // remote: 자리 비움 원격 상태(승인 요청·Claude 의 마지막 답·지시 처리 상태), ver: PC 앱 버전(휴대폰 설정에 화면 버전과 나란히)
        var body = Encrypt(cfg.Key!, $"{{\"snap\":{snap},\"share\":{share},\"edit\":{(cfg.PhoneEdit ? "true" : "false")},\"lang\":\"{lang}\",\"ver\":\"{App.Version}\"" +
                                     (remote != null ? $",\"remote\":{remote}" : "") + "}", now);
        using var resp = await _http.PutAsync(OfficeUrl(cfg, token), new StringContent(body, Encoding.UTF8, "application/json"), ct);
        if (!resp.IsSuccessStatusCode)
        {
            var detail = await resp.Content.ReadAsStringAsync(ct);
            if (resp.StatusCode == HttpStatusCode.Unauthorized) _idToken = null;
            throw new CloudException(resp.StatusCode switch
            {
                HttpStatusCode.Unauthorized => L.T("무료 서버가 쓰기를 거부했어요 — 30초 뒤 다시 로그인해 봅니다", "The free server refused to write — signing in again in 30 s"),
                HttpStatusCode.NotFound => L.T("무료 서버를 찾지 못했어요 — 30초 뒤 다시 해 봅니다", "Couldn't find the free server — retrying in 30 s"),
                _ => L.T($"올리기 실패({(int)resp.StatusCode}): {Short(detail)}", $"Upload failed ({(int)resp.StatusCode}): {Short(detail)}"),
            });
        }

        _lastSentKey = key;
        _lastSentUtc = now;
        Interlocked.Exchange(ref _lastOkMs, new DateTimeOffset(now).ToUnixTimeMilliseconds());
        _state = "on";
        _error = null;
        Publish();   // "마지막 올림" 시각을 화면에 알린다(최대 3초에 한 번)
    }

    // ───────────────────────── 휴대폰 → PC: 별명·메모 고친 것, 원격 지시·승인 ─────────────────────────

    private static readonly TimeSpan InboxEvery = TimeSpan.FromSeconds(4), InboxEveryAway = TimeSpan.FromSeconds(2);
    private DateTime _nextInboxUtc;

    private static string Auth(string? token) => token != null ? "?auth=" + Uri.EscapeDataString(token) : "";

    /// <summary>
    /// 휴대폰이 inbox/&lt;Uid&gt; 에 남긴 변경을 가져와 해독하고 지운다. 해독이 안 되는 것(키가 다르거나 남이 넣은 것)은 버리기만 한다.
    /// 읽기가 막히면(401) 아무것도 안 하고 다음에 다시 본다.
    /// </summary>
    private async Task PollInboxAsync(CancellationToken ct)
    {
        CloudConfig cfg;
        lock (_lock) cfg = _cfg;
        var token = await GetTokenAsync(ct);
        var url = $"{cfg.DatabaseUrl}/inbox/{Uri.EscapeDataString(cfg.Uid!)}";
        using var resp = await _http.GetAsync(url + ".json" + Auth(token), ct);
        if (!resp.IsSuccessStatusCode) return;
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return;
        var edits = new List<NoteEdit>();
        var remote = new List<RemoteMsg>();
        foreach (var m in doc.RootElement.EnumerateObject().OrderBy(m => m.Name, StringComparer.Ordinal).Take(100))   // 키(푸시 ID)가 보낸 순서
        {
            switch (TryDecrypt(cfg.Key!, m.Value))
            {
                case NoteEdit e when cfg.PhoneEdit: edits.Add(e); break;   // 고치기를 끈 뒤에 온 것은 버린다
                case RemoteMsg r when _remoteInstalled: remote.Add(r); break;
            }
            using var del = await _http.DeleteAsync($"{url}/{Uri.EscapeDataString(m.Name)}.json{Auth(token)}", ct);
        }
        if (edits.Count > 0) NotesFromPhone?.Invoke(edits);
        if (remote.Count > 0) RemoteFromPhone?.Invoke(remote);
    }

    /// <summary>
    /// 휴대폰이 보낸 {v, iv, ct} 를 해독한다. AES-256-GCM(태그는 암호문 뒤 — WebCrypto 모양) — 키가 맞아야 풀리므로 QR 을 찍은 휴대폰이 보낸 것만 남는다.
    /// k 가 없으면 별명·메모 {id, nick, memo, t}, 있으면 원격 {k: say|ok|no, sid, req, text, t, n}.
    /// </summary>
    private static object? TryDecrypt(string keyB64Url, JsonElement v)
    {
        try
        {
            if (v.ValueKind != JsonValueKind.Object) return null;
            var iv = Convert.FromBase64String(v.GetProperty("iv").GetString()!);
            var all = Convert.FromBase64String(v.GetProperty("ct").GetString()!);
            if (iv.Length != 12 || all.Length < 17 || all.Length > 8000) return null;
            var plain = new byte[all.Length - 16];
            using (var aes = new AesGcm(CloudConfig.FromBase64Url(keyB64Url), 16))
                aes.Decrypt(iv, all.AsSpan(0, all.Length - 16), all.AsSpan(all.Length - 16), plain);
            using var doc = JsonDocument.Parse(plain);
            var r = doc.RootElement;
            string? Text(string name, int max) =>
                r.TryGetProperty(name, out var x) && x.ValueKind == JsonValueKind.String && x.GetString() is { } s ? (s.Length > max ? s[..max] : s) : null;
            long t = r.TryGetProperty("t", out var tt) && tt.ValueKind == JsonValueKind.Number && tt.TryGetInt64(out var tv) ? tv : 0;

            if (Text("k", 10) is { } kind)
                return kind is "say" or "ok" or "no" or "away"
                    ? new RemoteMsg(kind, Text("sid", 100), Text("req", 40), Text("text", RemoteHub.MaxText), t, Text("n", 64))
                    : null;

            var id = Text("id", 101);
            if (string.IsNullOrEmpty(id) || id.Length > 100) return null;
            return new NoteEdit(id, Text("nick", 30)?.Trim(), Text("memo", 2000), t);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    private static string OfficeUrl(CloudConfig cfg, string? token) =>
        $"{cfg.DatabaseUrl}/offices/{Uri.EscapeDataString(cfg.Uid!)}.json" + (token != null ? "?auth=" + Uri.EscapeDataString(token) : "");

    /// <summary>gzip → AES-256-GCM. 휴대폰(WebCrypto)은 암호문 뒤에 인증 태그가 붙은 모양을 받는다.</summary>
    private static string Encrypt(string keyB64Url, string plain, DateTime now)
    {
        byte[] packed;
        using (var ms = new MemoryStream())
        {
            using (var gz = new GZipStream(ms, CompressionLevel.SmallestSize, leaveOpen: true))
                gz.Write(Encoding.UTF8.GetBytes(plain));
            packed = ms.ToArray();
        }

        var nonce = RandomNumberGenerator.GetBytes(12);
        var sealedBytes = new byte[packed.Length + 16];
        using (var aes = new AesGcm(CloudConfig.FromBase64Url(keyB64Url), 16))
            aes.Encrypt(nonce, packed, sealedBytes.AsSpan(0, packed.Length), sealedBytes.AsSpan(packed.Length));

        return JsonSerializer.Serialize(new
        {
            v = 1,
            z = 1,   // gzip
            t = new DateTimeOffset(now).ToUnixTimeMilliseconds(),
            iv = Convert.ToBase64String(nonce),
            ct = Convert.ToBase64String(sealedBytes),
        });
    }

    // ───────────────────────── Firebase 익명 로그인 ─────────────────────────

    // 올리기 루프와 끝낼 때 "꺼짐" 알리기가 동시에 처음 로그인해 익명 사용자가 둘 생기지 않게
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    /// <summary>데이터베이스에 쓸 ID 토큰. 로컬 시험 서버면 로그인 없이 null.</summary>
    private async Task<string?> GetTokenAsync(CancellationToken ct)
    {
        await _tokenLock.WaitAsync(ct);
        try { return await GetTokenCoreAsync(ct); }
        finally { _tokenLock.Release(); }
    }

    private async Task<string?> GetTokenCoreAsync(CancellationToken ct)
    {
        CloudConfig cfg;
        lock (_lock) cfg = _cfg;
        if (cfg.IsLocal)
        {
            if (cfg.Uid is null) { Update(c => c.Uid = "local"); Publish(); }
            return null;
        }
        if (_idToken != null && DateTime.UtcNow < _idTokenExpiresUtc) return _idToken;

        if (cfg.RefreshToken != null)
        {
            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = cfg.RefreshToken,
            });
            using var resp = await _http.PostAsync($"https://securetoken.googleapis.com/v1/token?key={Uri.EscapeDataString(cfg.ApiKey!)}", form, ct);
            var text = await resp.Content.ReadAsStringAsync(ct);
            if (resp.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(text);
                var r = doc.RootElement;
                _idToken = r.GetProperty("id_token").GetString();
                _idTokenExpiresUtc = DateTime.UtcNow + Expires(r, "expires_in");
                var newRefresh = r.GetProperty("refresh_token").GetString();
                if (newRefresh != cfg.RefreshToken) Update(c => c.RefreshToken = newRefresh);
                return _idToken;
            }
            // 익명 사용자가 지워졌거나 토큰이 무효 — 새로 만든다(데이터 경로가 바뀌어 휴대폰은 QR을 다시 찍어야 함)
            if (!(text.Contains("USER_NOT_FOUND") || text.Contains("TOKEN_EXPIRED") || text.Contains("INVALID_REFRESH_TOKEN") || text.Contains("USER_DISABLED")))
                throw new CloudException(L.T($"로그인 갱신 실패: {FirebaseError(text)}", $"Sign-in refresh failed: {FirebaseError(text)}"));
            _notice = L.T("로그인을 새로 만들었습니다 — 휴대폰에서 QR을 다시 찍어 주세요", "Created a new sign-in — scan the QR on your phone again");
        }

        using (var resp = await _http.PostAsync($"https://identitytoolkit.googleapis.com/v1/accounts:signUp?key={Uri.EscapeDataString(cfg.ApiKey!)}",
                   new StringContent("{\"returnSecureToken\":true}", Encoding.UTF8, "application/json"), ct))
        {
            var text = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                var code = FirebaseError(text);
                throw new CloudException(L.T($"무료 서버에 로그인하지 못했어요({code}) — 30초 뒤 다시 해 봅니다", $"Couldn't sign in to the free server ({code}) — retrying in 30 s"));
            }
            using var doc = JsonDocument.Parse(text);
            var r = doc.RootElement;
            _idToken = r.GetProperty("idToken").GetString();
            _idTokenExpiresUtc = DateTime.UtcNow + Expires(r, "expiresIn");
            var uid = r.GetProperty("localId").GetString();
            var refresh = r.GetProperty("refreshToken").GetString();
            Update(c => { c.Uid = uid; c.RefreshToken = refresh; });
            Publish();   // 이제 휴대폰 주소(QR)를 만들 수 있다
            return _idToken;
        }
    }

    /// <summary>토큰 유효 시간(초, 문자열로 온다)에서 5분 여유를 뺀 것.</summary>
    private static TimeSpan Expires(JsonElement r, string name) =>
        TimeSpan.FromSeconds(Math.Max(60, (int.TryParse(r.GetProperty(name).GetString(), out var s) ? s : 3600) - 300));

    private static string FirebaseError(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.TryGetProperty("error", out var e))
                return e.ValueKind == JsonValueKind.Object && e.TryGetProperty("message", out var m) ? m.GetString() ?? text : e.ToString();
        }
        catch (JsonException) { }
        return Short(text);
    }

    private static string Short(string s) => s.Length <= 160 ? s : s[..159] + "…";

    private sealed class CloudException(string message) : Exception(message);
}

/// <summary>휴대폰에서 고친 별명·메모 하나. Nick·Memo 가 모두 비면 지운 것. T: 휴대폰에서 고친 시각(ms).</summary>
internal sealed record NoteEdit(string Id, string? Nick, string? Memo, long T);
