namespace ClaudeOffice.Monitoring;

/// <summary>
/// 화면 언어(⚙️ → 사무실 → 화면 언어). 한국어가 기본이고 English 를 고르면 말풍선·기록·알림·확인 창 문구를 영어로 쓴다.
/// 감시 로직(Monitoring·Hooks)이 App 없이도 컴파일되도록(tools\watchtest) 여기 둔다 — 앱이 시작할 때와 바꿀 때 <see cref="En"/> 을 정한다.
/// </summary>
internal static class L
{
    // 화면 스레드가 바꾸고 감시·올리기 스레드가 읽는다
    private static volatile bool _en;

    /// <summary>영어 화면인지.</summary>
    public static bool En { get => _en; set => _en = value; }

    /// <summary>화면 주소·휴대폰 요약에 싣는 언어 코드(ko | en).</summary>
    public static string Code => _en ? "en" : "ko";

    /// <summary>지금 언어의 문구.</summary>
    public static string T(string ko, string en) => _en ? en : ko;
}
