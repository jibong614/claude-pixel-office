namespace ClaudeOffice.Monitoring;

/// <summary>
/// 직원(세션)이 지금 하고 있는 일. 화면의 자세·모니터 화면·말풍선이 이 값으로 정해진다.
/// 이름은 소문자로 바뀌어 화면(office.js)의 ACT 표와 1:1로 맞물린다.
/// </summary>
public enum Activity
{
    Thinking,   // 지시를 받았거나 도구 결과를 받은 뒤 다음 행동을 고민
    Talking,    // 답변 문장 작성
    Reading,    // Read, Skill
    Searching,  // Grep, Glob, ToolSearch
    Writing,    // Edit, Write
    Running,    // Bash, PowerShell
    Browsing,   // WebSearch, WebFetch
    Delegating, // Agent(서브에이전트=인턴) 호출
    Planning,   // 할 일 목록 정리
    Asking,     // AskUserQuestion / ExitPlanMode — 사람의 답을 기다림
    Waiting,    // 파일 수정이 끝나지 않음 — 권한 승인 대기로 추정
    Working,    // 그 밖의 도구(MCP 등)
    Idle,       // 응답을 마치고 대기
    Sleeping,   // 오래 대기 / 응답 없음
    Away,       // 몇 시간째 기록 없음 — 퇴근
}
