using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ClaudeOffice.Monitoring;

namespace ClaudeOffice.Cloud;

/// <summary>
/// 휴대폰 연결 설정 — 데이터 폴더의 cloud.json.
/// 해독 키와 익명 로그인 토큰이 들어 있으니 이 파일은 남에게 주지 않는다(휴대폰에는 QR 주소의 # 뒤로 키만 건넨다).
/// 고칠 때는 늘 복사본을 만들어 바꾼 뒤 통째로 바꿔 끼운다(감시 스레드와 화면 스레드가 함께 읽으므로).
/// </summary>
internal sealed partial class CloudConfig
{
    public bool Enabled { get; set; }
    public string? ApiKey { get; set; }
    public string? DatabaseUrl { get; set; }   // 무료 서버(RelayDatabaseUrl) 또는 시험 서버 http://127.0.0.1:…
    public string? ProjectId { get; set; }
    public string? PageUrl { get; set; }       // 휴대폰 화면 주소: 공용 페이지(PublicPageUrl) 또는 시험 서버
    [JsonConverter(typeof(DpapiStringConverter))]
    public string? Key { get; set; }           // AES-256 키(base64url, 디스크에는 DPAPI 로 암호화해 저장)
    public string? Uid { get; set; }           // 익명 로그인 사용자 ID = 데이터 경로 offices/<Uid>
    [JsonConverter(typeof(DpapiStringConverter))]
    public string? RefreshToken { get; set; }  // 디스크에는 DPAPI 로 암호화해 저장

    /// <summary>휴대폰에서 별명·메모 고치기 허용 — 휴대폰이 같은 키로 암호화한 변경을 inbox/&lt;Uid&gt; 에 남기면 PC가 가져가 반영한다.</summary>
    public bool PhoneEdit { get; set; } = true;

    /// <summary>
    /// 누구나 쓰는 휴대폰 화면 페이지(GitHub Pages — 이 저장소의 wwwroot 를 그대로 올린 것).
    /// 페이지는 모두에게 같은 파일이고 각자의 데이터 주소·키는 QR 주소의 # 뒤에만 있으므로, 사용자마다 따로 올릴 필요가 없다.
    /// </summary>
    public const string PublicPageUrl = "https://jibong614.github.io/claude-pixel-office/";

    // 무료 서버: 이 프로그램을 쓰는 모두가 함께 쓰는 Firebase 프로젝트(Spark 무료 요금제 — 한도를 넘으면 잠시 멈출 뿐 요금은 없다).
    // 각 PC 는 익명 로그인한 자기 칸(offices/<Uid>)에만 쓰고, 올리는 것은 PC·휴대폰만 아는 키로 암호화한 것뿐이라 서버도 남도 못 읽는다.
    // 규칙은 mobile/database.rules.json(tools\deploy-rules.ps1 로 올림). 웹 앱 설정값은 원래 공개용(브라우저 앱에 그대로 들어가는 값)이다.
    public const string RelayProjectId = "claude-pixel-office-relay";
    public const string RelayApiKey = "AIzaSyDcU9_nnej1xMa-bAqSRvWwiEn-0earjKc";
    public const string RelayDatabaseUrl = "https://claude-pixel-office-relay-default-rtdb.asia-southeast1.firebasedatabase.app";

    /// <summary>이번에 내 Firebase 에서 무료 서버로 옮겼다 — 휴대폰은 QR을 다시 찍어야 한다(화면에 한 번 알린다).</summary>
    [JsonIgnore]
    public bool Moved { get; private set; }

    [JsonIgnore]
    public bool Configured => !string.IsNullOrEmpty(DatabaseUrl) && (IsLocal || !string.IsNullOrEmpty(ApiKey));

    /// <summary>로컬 시험 서버나 Firebase 에뮬레이터(127.0.0.1·localhost) — 로그인 없이 쓴다.</summary>
    [JsonIgnore]
    public bool IsLocal => Uri.TryCreate(DatabaseUrl, UriKind.Absolute, out var u) && u.IsLoopback;

    public CloudConfig Clone() => (CloudConfig)MemberwiseClone();

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static CloudConfig Load(string path)
    {
        CloudConfig? c = null;
        bool needsResave = false;
        try
        {
            if (File.Exists(path))
            {
                var raw = File.ReadAllText(path);
                c = JsonSerializer.Deserialize<CloudConfig>(raw);
                // 예전 평문 Key·RefreshToken 이면 읽은 뒤 바로 DPAPI 로 암호화해 다시 저장한다
                if (c is { Key.Length: > 0 } && !raw.Contains("\"dpapi:", StringComparison.Ordinal))
                    needsResave = true;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or CryptographicException)
        {
            // 깨진 설정은 새로 시작(키가 바뀌므로 휴대폰은 QR을 다시 찍어야 함)
        }
        c ??= new CloudConfig();
        if (c.UseRelay() || needsResave)
        {
            try { c.Save(path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }   // 다음에 고칠 때 다시 저장한다
        }
        return c;
    }

    /// <summary>
    /// cloud.json 의 민감한 값(Key·RefreshToken)을 현재 Windows 계정(DPAPI CurrentUser)으로 암호화해 저장한다.
    /// 파일이 외부로 유출되어도 다른 PC·계정에서는 풀지 못하며, 예전 평문 값도 호환해 읽는다.
    /// </summary>
    private sealed class DpapiStringConverter : JsonConverter<string?>
    {
        private const string Prefix = "dpapi:";

        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var s = reader.GetString();
            if (string.IsNullOrEmpty(s) || !s.StartsWith(Prefix, StringComparison.Ordinal)) return s;
            var plain = ProtectedData.Unprotect(Convert.FromBase64String(s[Prefix.Length..]), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }

        public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
        {
            if (string.IsNullOrEmpty(value)) { writer.WriteNullValue(); return; }
            var enc = ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser);
            writer.WriteStringValue(Prefix + Convert.ToBase64String(enc));
        }
    }

    /// <summary>
    /// 무료 서버를 쓰게 한다(시험 서버는 그대로). 1.1.6 까지의 "내 Firebase" 설정이면 옮기고 Moved — 데이터 주소가 바뀌므로 로그인은 새로,
    /// 키는 그대로 두되 휴대폰은 QR을 다시 찍어야 한다. 바뀐 것이 있으면 true.
    /// </summary>
    private bool UseRelay()
    {
        if (IsLocal || (ProjectId == RelayProjectId && DatabaseUrl == RelayDatabaseUrl && ApiKey == RelayApiKey)) return false;
        Moved = Uid != null;   // 내 Firebase 로 올리던 중이었다
        ApiKey = RelayApiKey;
        DatabaseUrl = RelayDatabaseUrl;
        ProjectId = RelayProjectId;
        PageUrl = PublicPageUrl;
        Uid = RefreshToken = null;
        PhoneEdit = true;   // 무료 서버 규칙에는 휴대폰 → PC 칸이 늘 있다
        return true;
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOptions), new UTF8Encoding(false));
        File.Move(tmp, path, overwrite: true);
    }

    public static string NewKey() => ToBase64Url(RandomNumberGenerator.GetBytes(32));

    public static string ToBase64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] FromBase64Url(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }

    // apiKey: "AIza...", databaseURL: "https://..." 처럼 생긴 줄(따옴표·쉼표 모양은 너그럽게)
    [GeneratedRegex(@"[""']?(\w+)[""']?\s*:\s*[""']([^""']+)[""']")]
    private static partial Regex Pair();

    /// <summary>
    /// 시험 서버 설정({ databaseURL: "http://127.0.0.1:8765" })에서 데이터 주소를 뽑는다 — 점검 도구(tools\pagetest.ps1 + fake-rtdb.py) 전용.
    /// 이 PC 의 시험 서버(127.0.0.1·localhost)만 받는다. 문제가 있으면 Error 에 이유.
    /// </summary>
    public static (string? DatabaseUrl, string? Error) ParseLocal(string text)
    {
        foreach (Match m in Pair().Matches(text ?? ""))
        {
            if (!m.Groups[1].Value.Equals("databaseURL", StringComparison.OrdinalIgnoreCase)) continue;
            var db = m.Groups[2].Value.Trim();
            return Uri.TryCreate(db, UriKind.Absolute, out var u) && u.IsLoopback
                ? (db.TrimEnd('/'), null)
                : (null, L.T($"시험 서버(127.0.0.1) 주소만 받습니다: {db}", $"Only a local test server (127.0.0.1) is accepted: {db}"));
        }
        return (null, L.T("databaseURL 이 없습니다.", "databaseURL is missing."));
    }
}
