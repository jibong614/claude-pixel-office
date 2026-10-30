using System.IO;
using System.Text;
using System.Text.Json;

namespace ClaudeOffice;

/// <summary>
/// 창이 뜨기 전에 알아야 하는 설정 — 데이터 폴더의 app.json.
/// (그 밖의 화면 설정은 화면이 localStorage 에 둔다. 화면은 창이 뜬 뒤에야 읽히므로 여기 둘 수밖에 없는 것만)
/// </summary>
internal sealed class AppSettings
{
    /// <summary>프로그램을 켤 때 창을 띄우지 않고 알림 영역(🏢)에 숨겨 둔다(휴대폰으로만 볼 때).</summary>
    public bool StartMinimized { get; set; }

    /// <summary>✕ 를 눌러도 끝내지 않고 알림 영역으로 숨긴다(실수로 꺼서 휴대폰 보기가 멈추지 않게). 끝내기는 알림 영역 아이콘 메뉴.</summary>
    public bool CloseToTray { get; set; } = true;

    private static string FilePath => Path.Combine(App.DataDir, "app.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // 깨졌으면 기본값으로
        }
        return new AppSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(App.DataDir);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        File.Move(tmp, FilePath, overwrite: true);
    }
}
