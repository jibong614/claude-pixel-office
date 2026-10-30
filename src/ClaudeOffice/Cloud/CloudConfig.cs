using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ClaudeOffice.Monitoring;

namespace ClaudeOffice.Cloud;

/// <summary>
/// 휴대폰 보기(클라우드) 설정 — 데이터 폴더의 cloud.json.
/// 해독 키와 익명 로그인 토큰이 들어 있으니 이 파일은 남에게 주지 않는다(휴대폰에는 QR 주소의 # 뒤로 키만 건넨다).
/// 고칠 때는 늘 복사본을 만들어 바꾼 뒤 통째로 바꿔 끼운다(감시 스레드와 화면 스레드가 함께 읽으므로).
/// </summary>
internal sealed partial class CloudConfig
{
    public bool Enabled { get; set; }
    public string? ApiKey { get; set; }
    public string? DatabaseUrl { get; set; }   // https://<프로젝트>-default-rtdb.<지역>.firebasedatabase.app
    public string? ProjectId { get; set; }
    public string? PageUrl { get; set; }       // 휴대폰 화면 주소: 공용 페이지(PublicPageUrl) 또는 내 Firebase Hosting https://<프로젝트>.web.app
    public string? Key { get; set; }           // AES-256 키(base64url)
    public string? Uid { get; set; }           // 익명 로그인 사용자 ID = 데이터 경로 offices/<Uid>
    public string? RefreshToken { get; set; }

    /// <summary>휴대폰에서 별명·메모 고치기 허용 — 휴대폰이 같은 키로 암호화한 변경을 inbox/&lt;Uid&gt; 에 남기면 PC가 가져가 반영한다(새 규칙 필요).</summary>
    public bool PhoneEdit { get; set; }

    /// <summary>
    /// 누구나 쓰는 휴대폰 화면 페이지(GitHub Pages — 이 저장소의 wwwroot 를 그대로 올린 것).
    /// 페이지는 모두에게 같은 파일이고 각자의 데이터 주소·키는 QR 주소의 # 뒤에만 있으므로, 사용자마다 따로 올릴 필요가 없다.
    /// </summary>
    public const string PublicPageUrl = "https://jibong614.github.io/claude-pixel-office/";

    public static string OwnPageUrl(string projectId) => $"https://{projectId}.web.app/";

    [JsonIgnore]
    public bool Configured => !string.IsNullOrEmpty(DatabaseUrl) && (IsLocal || !string.IsNullOrEmpty(ApiKey));

    /// <summary>웹 앱 설정(apiKey·projectId)은 받았다 — 데이터베이스는 아직 없을 수 있다(설정 도우미가 찾는다).</summary>
    [JsonIgnore]
    public bool HasProject => IsLocal || (!string.IsNullOrEmpty(ApiKey) && !string.IsNullOrEmpty(ProjectId));

    /// <summary>휴대폰 화면 페이지 종류: public(공용) | own(내 Firebase Hosting) | local(시험 서버).</summary>
    [JsonIgnore]
    public string PageMode => IsLocal ? "local" : PageUrl is null || PageUrl == PublicPageUrl ? "public" : "own";

    /// <summary>로컬 시험 서버나 Firebase 에뮬레이터(127.0.0.1·localhost) — 로그인 없이 쓴다.</summary>
    [JsonIgnore]
    public bool IsLocal => Uri.TryCreate(DatabaseUrl, UriKind.Absolute, out var u) && u.IsLoopback;

    public CloudConfig Clone() => (CloudConfig)MemberwiseClone();

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static CloudConfig Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<CloudConfig>(File.ReadAllText(path)) ?? new CloudConfig();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // 깨진 설정은 새로 시작(키가 바뀌므로 휴대폰은 QR을 다시 찍어야 함)
        }
        return new CloudConfig();
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
    /// Firebase 콘솔의 웹 앱 설정(const firebaseConfig = { apiKey: ..., databaseURL: ..., projectId: ... })을
    /// 통째로 붙여 넣은 글에서 값을 뽑는다. 문제가 있으면 Error 에 사람이 읽을 이유.
    /// databaseURL 은 없어도 된다 — 데이터베이스보다 웹 앱을 먼저 만들면 빠져 있는데, 설정 도우미가 프로젝트 ID 로 찾아낸다.
    /// </summary>
    public static (string? ApiKey, string? DatabaseUrl, string? ProjectId, string? Error) Parse(string text)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Pair().Matches(text ?? ""))
            map[m.Groups[1].Value] = m.Groups[2].Value.Trim();

        map.TryGetValue("apiKey", out var apiKey);
        map.TryGetValue("databaseURL", out var db);
        map.TryGetValue("projectId", out var project);

        bool local = false;
        if (!string.IsNullOrEmpty(db))
        {
            if (!Uri.TryCreate(db, UriKind.Absolute, out var u) || (u.Scheme != Uri.UriSchemeHttps && !u.IsLoopback))
                return (null, null, null, L.T($"databaseURL 모양이 이상합니다: {db}", $"databaseURL looks wrong: {db}"));
            local = u.IsLoopback;
            db = db.TrimEnd('/');
        }
        if (local) return (apiKey, db, project, null);
        if (string.IsNullOrEmpty(apiKey) && string.IsNullOrEmpty(project))
            return (null, null, null, L.T("설정을 알아보지 못했습니다. 웹 앱 등록 화면의 firebaseConfig 블록({ apiKey: … } 부분)을 통째로 붙여 넣어 주세요.",
                                          "Couldn't read the config. Paste the whole firebaseConfig block ({ apiKey: … }) from the web app registration screen."));
        if (string.IsNullOrEmpty(apiKey))
            return (null, null, null, L.T("apiKey 가 없습니다. firebaseConfig 블록을 통째로 붙여 넣어 주세요.", "apiKey is missing. Paste the whole firebaseConfig block."));
        if (string.IsNullOrEmpty(project) || !ProjectIdShape().IsMatch(project))
            return (null, null, null, L.T("projectId 가 없거나 모양이 이상합니다. firebaseConfig 블록을 통째로 붙여 넣어 주세요.",
                                          "projectId is missing or looks wrong. Paste the whole firebaseConfig block."));
        return (apiKey, string.IsNullOrEmpty(db) ? null : db, project, null);
    }

    // Firebase 프로젝트 ID: 소문자·숫자·하이픈(주소에 그대로 넣으므로 다른 글자는 받지 않는다)
    [GeneratedRegex(@"^[a-z0-9][a-z0-9-]{3,61}[a-z0-9]$")]
    private static partial Regex ProjectIdShape();
}
