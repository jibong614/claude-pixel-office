using System.Text.Json;
using ClaudeOffice.Monitoring;

namespace ClaudeOffice.Hooks;

/// <summary>
/// Claude Code 훅이 보낸 이벤트 하나에서 쓰는 값만 추린 것.
/// 훅 입력에는 시각이 없어서 받은 시각(<see cref="Utc"/>)을 쓴다.
/// </summary>
internal sealed record HookEvent(
    string Event,              // hook_event_name : PermissionRequest, Notification, SessionEnd ...
    string SessionId,
    string? AgentId,           // 인턴(서브에이전트)에서 난 이벤트면 그 ID — 기록 파일 이름 agent-<ID>.jsonl 과 같은 모양으로 맞춘다
    string? ToolName,
    string? ToolText,          // 말풍선 문구(기록 파일의 도구 호출과 같은 규칙으로 만든다 → 짝 맞추기에도 쓴다)
    string? NotificationType,  // permission_prompt, idle_prompt, elicitation_dialog ...
    string? Reason,            // SessionEnd 이유 : clear, resume, logout, prompt_input_exit, other
    DateTime Utc,
    string? Command = null)    // 명령 실행(Bash·PowerShell) 승인 요청이면 그 명령 — 승인 뒤 실행 중인지 알아보는 데 쓴다(ApprovalProbe)
{
    public static HookEvent? TryParse(ReadOnlyMemory<byte> body, DateTime utc)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var r = doc.RootElement;
            var ev = ToolCatalog.Str(r, "hook_event_name");
            var sid = ToolCatalog.Str(r, "session_id");
            if (ev is null || sid is null) return null;

            var agent = ToolCatalog.Str(r, "agent_id");
            if (agent != null && agent.StartsWith("agent-", StringComparison.OrdinalIgnoreCase))
                agent = agent["agent-".Length..];

            var tool = ToolCatalog.Str(r, "tool_name");
            var input = r.TryGetProperty("tool_input", out var inp) ? inp : default;

            return new HookEvent(ev, sid, agent, tool,
                tool != null ? ToolCatalog.Describe(tool, input) : null,
                ToolCatalog.Str(r, "notification_type"),
                ToolCatalog.Str(r, "reason"),
                utc,
                tool is "Bash" or "PowerShell" ? ToolCatalog.Str(input, "command") : null);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
