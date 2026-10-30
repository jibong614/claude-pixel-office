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

    // ── 알림(창을 숨겨 둬도 알아야 해서 앱 쪽에) ──
    /// <summary>✋ 승인·질문을 기다리기 시작하면 Windows 알림.</summary>
    public bool NotifyWaiting { get; set; } = true;
    /// <summary>오래(NotifyDoneMinutes 분 넘게) 일하던 직원이 일을 마치면 알림.</summary>
    public bool NotifyDone { get; set; } = true;
    public int NotifyDoneMinutes { get; set; } = 10;
    public bool NotifySound { get; set; } = true;
    /// <summary>방해 금지 시간대(시, 0~23). 시작 &gt; 끝이면 자정을 넘긴다(22 → 8).</summary>
    public bool QuietEnabled { get; set; }
    public int QuietStart { get; set; } = 22;
    public int QuietEnd { get; set; } = 8;

    /// <summary>GitHub 에서 새 버전이 나왔는지 하루 두 번 확인. UpdateNotified: 알림을 띄운 버전(같은 버전은 한 번만).</summary>
    public bool UpdateCheck { get; set; } = true;
    public string? UpdateNotified { get; set; }

    /// <summary>자리 비움 원격: Windows 화면을 잠그면 자리 비움을 켜고, 잠금을 풀면 끈다(잠금으로 켠 것만).</summary>
    public bool RemoteAwayOnLock { get; set; } = true;

    /// <summary>화면 언어: ko(기본) | en. 말풍선·알림 문구를 만드는 감시기도 알아야 해서 화면 저장소가 아니라 여기에.</summary>
    public string Language { get; set; } = "ko";

    /// <summary>마지막 창 위치·크기(화면 밖이면 쓰지 않는다).</summary>
    public WindowPlacement? Window { get; set; }

    /// <summary>작게 보기(위젯): 창을 작게 줄이고 직원·상태 목록만. 위치·폭은 보통 창과 따로 기억한다(높이는 목록에 맞춤).</summary>
    public bool Widget { get; set; }
    public WindowPlacement? WidgetWindow { get; set; }

    public sealed class WindowPlacement
    {
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public bool Maximized { get; set; }
    }

    /// <summary>지금이 방해 금지 시간대인지.</summary>
    public bool InQuietHours(DateTime local)
    {
        if (!QuietEnabled || QuietStart == QuietEnd) return false;
        int h = local.Hour;
        return QuietStart < QuietEnd ? h >= QuietStart && h < QuietEnd : h >= QuietStart || h < QuietEnd;
    }

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
