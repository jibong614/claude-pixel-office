using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

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
    public string? PageUrl { get; set; }       // 휴대폰 화면 주소(Firebase Hosting) https://<프로젝트>.web.app
    public string? Key { get; set; }           // AES-256 키(base64url)
    public string? Uid { get; set; }           // 익명 로그인 사용자 ID = 데이터 경로 offices/<Uid>
    public string? RefreshToken { get; set; }

    [JsonIgnore]
    public bool Configured => !string.IsNullOrEmpty(DatabaseUrl) && (IsLocal || !string.IsNullOrEmpty(ApiKey));

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
    /// </summary>
    public static (string? ApiKey, string? DatabaseUrl, string? ProjectId, string? Error) Parse(string text)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Pair().Matches(text ?? ""))
            map[m.Groups[1].Value] = m.Groups[2].Value.Trim();

        map.TryGetValue("apiKey", out var apiKey);
        map.TryGetValue("databaseURL", out var db);
        map.TryGetValue("projectId", out var project);

        if (string.IsNullOrEmpty(db))
            return (null, null, null, "databaseURL 이 없습니다. Firebase 콘솔에서 Realtime Database 를 먼저 만든 뒤 설정을 다시 복사해 주세요.");
        if (!Uri.TryCreate(db, UriKind.Absolute, out var u) || (u.Scheme != Uri.UriSchemeHttps && !u.IsLoopback))
            return (null, null, null, $"databaseURL 모양이 이상합니다: {db}");
        if (!u.IsLoopback && string.IsNullOrEmpty(apiKey))
            return (null, null, null, "apiKey 가 없습니다. firebaseConfig 블록을 통째로 붙여 넣어 주세요.");
        if (!u.IsLoopback && string.IsNullOrEmpty(project))
            return (null, null, null, "projectId 가 없습니다. firebaseConfig 블록을 통째로 붙여 넣어 주세요.");
        return (apiKey, db.TrimEnd('/'), project, null);
    }
}
