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
    /// <summary>프로그램을 켤 때 창을 띄우지 않고 알림 영역(🧑‍💻)에 숨겨 둔다(휴대폰으로만 볼 때).</summary>
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

    /// <summary>GitHub 에서 새 버전이 나왔는지 주기적으로 확인. UpdateNotified: 알림을 띄운 버전(같은 버전은 한 번만).</summary>
    public bool UpdateCheck { get; set; } = true;
    /// <summary>업데이트 확인 주기(시간 단위: 1 | 5 | 12, 기본값 1시간).</summary>
    public int UpdateIntervalHours { get; set; } = 1;
    public string? UpdateNotified { get; set; }

    /// <summary>사용자가 직접 ⚙️ 설정에서 Claude Code 연결을 끊었는지(기본값은 false = 자동 연결 활성화).</summary>
    public bool HooksDisabledByUser { get; set; }

    /// <summary>처음 실행할 때 "Claude Code와 연결할까요?"를 물어봤다(한 번만 묻는다).</summary>
    public bool ConnectAsked { get; set; } = true;

    /// <summary>자리 비움 원격: Windows 화면을 잠그면 자리 비움을 켜고, 잠금을 풀면 끈다(잠금으로 켠 것만).</summary>
    public bool RemoteAwayOnLock { get; set; } = true;
    /// <summary>자리 비움 원격: 마우스·키보드 입력이 이만큼(분) 없으면 자리 비움을 켜고, 돌아와 움직이면 끈다. 0 = 끔.</summary>
    public int RemoteAwayIdleMinutes { get; set; } = 3;

    /// <summary>화면 언어: ko(기본) | en. 말풍선·알림 문구를 만드는 감시기도 알아야 해서 화면 저장소가 아니라 여기에.</summary>
    public string Language { get; set; } = "ko";

    /// <summary>화면 동작 모드: basic(기본 픽셀 오피스) | office(비즈니스 대시보드).</summary>
    public string AppMode { get; set; } = "basic";

    /// <summary>세션 창 이동 대상: auto(자동 감지 · 기본) | vscode(VS Code) | desktop(Claude 데스크톱 앱) | terminal(터미널).</summary>
    public string SessionTarget { get; set; } = "auto";

    /// <summary>프로그램 아이콘: a(기본) | b | c.</summary>
    public string AppIcon { get; set; } = "a";

    /// <summary>바탕화면 미니위젯 크기: large(크게 · 기본 360px) | medium(보통 300px) | small(작게 260px).</summary>
    public string WidgetSize { get; set; } = "large";

    [System.Text.Json.Serialization.JsonIgnore]
    public string PetSize { get => WidgetSize; set => WidgetSize = value; }

    /// <summary>마지막 창 위치·크기(화면 밖이면 쓰지 않는다).</summary>
    public WindowPlacement? Window { get; set; }

    /// <summary>작게 보기(목록 모드): 창을 작게 줄이고 직원·상태 목록만. 위치·폭은 보통 창과 따로 기억한다(높이는 목록에 맞춤).</summary>
    public bool Widget { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("WidgetWindow")]
    public WindowPlacement? CompactPlacement { get; set; }

    /// <summary>바탕화면 미니위젯(MiniWidget / WidgetWindow): 대표 직원 한 명을 바탕화면 오른쪽 아래 작은 창에 꺼내 둔다.</summary>
    public bool Pet { get; set; }
    /// <summary>미니위젯 대표 세션 ID. 비어 있으면 확인 필요 → 최근 활동순으로 자동 선택.</summary>
    public string? PetSessionId { get; set; }
    /// <summary>미니위젯 아래 지시 입력칸을 접어 두고 책상 그림만 작게 볼지.</summary>
    public bool PetCompact { get; set; }
    /// <summary>미니위젯 배경(벽·창문·화분·카펫)을 숨기고 직원·의자·책상·컴퓨터만 보여 줄지.</summary>
    public bool PetNoBg { get; set; }
    /// <summary>미니위젯을 초슬림 미니바(아일랜드) 모드로 보여 줄지.</summary>
    public bool PetIsland { get; set; }
    /// <summary>미니위젯을 시메지 산책 모드로 보여 줄지.</summary>
    public bool PetWalk { get; set; }
    /// <summary>미니위젯을 항상 위(Topmost)에 둘지. 해제 시 다른 일반 창 뒤로 숨는다.</summary>
    public bool PetTopmost { get; set; } = true;
    /// <summary>바탕화면 미니위젯 위치·크기 (높이는 사용자 조절 가능, 폭은 360 고정).</summary>
    [System.Text.Json.Serialization.JsonPropertyName("PetWindow")]
    public WindowPlacement? MiniWidgetPlacement { get; set; }

    /// <summary>바탕화면 미니바(초슬림 아일랜드) 위치 (폭 540, 높이 44 고정).</summary>
    [System.Text.Json.Serialization.JsonPropertyName("MiniBarWindow")]
    public WindowPlacement? MiniBarPlacement { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public WindowPlacement? MiniWidgetWindow { get => MiniWidgetPlacement; set => MiniWidgetPlacement = value; }

    [System.Text.Json.Serialization.JsonIgnore]
    public WindowPlacement? WidgetWindow { get => MiniWidgetPlacement; set => MiniWidgetPlacement = value; }

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
