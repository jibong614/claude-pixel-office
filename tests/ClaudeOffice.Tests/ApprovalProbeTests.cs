using ClaudeOffice.Monitoring;
using Xunit;

namespace ClaudeOffice.Tests;

public class ApprovalProbeTests
{
    [Fact]
    public void Tokens_ExtractsSignificantWords()
    {
        string cmd = "npm run build --prefix ./frontend";
        var tokens = ApprovalProbe.Tokens(cmd);

        // Tokens should contain meaningful words like "frontend" or "build"
        Assert.NotEmpty(tokens);
        Assert.Contains(tokens, t => t.Equals("frontend", StringComparison.OrdinalIgnoreCase) || t.Equals("build", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Tokens_IgnoresCommonShellWords()
    {
        string cmd = "echo true && exit 0";
        var tokens = ApprovalProbe.Tokens(cmd);

        // "echo", "true", "exit" are in Common ignore list
        Assert.Empty(tokens);
    }

    [Fact]
    public void Tokens_LimitsToMaxThreeTokens_DescendingLength()
    {
        string cmd = "docker compose -f docker-compose.prod.yml up --build";
        var tokens = ApprovalProbe.Tokens(cmd);

        Assert.True(tokens.Length <= 3);
        if (tokens.Length > 1)
        {
            Assert.True(tokens[0].Length >= tokens[1].Length);
        }
    }

    [Fact]
    public async Task RemoteHub_SettleApprovals_ClearsCompletedQuestion()
    {
        var hub = new ClaudeOffice.Remote.RemoteHub(null);
        hub.SetPet(true, "sess-1");

        var hookJson = """{"hook_event_name":"PermissionRequest","session_id":"sess-1","tool_name":"AskUserQuestion","tool_input":{"question":"테스트 질문?"}}""";
        var approveTask = hub.HandleAsync("approve", System.Text.Encoding.UTF8.GetBytes(hookJson), default);

        await Task.Delay(50);
        var stateJsonBefore = hub.StateJson();
        Assert.Contains("질문에 답해주세요", stateJsonBefore);

        // 시뮬레이션: 터미널에서 사용자가 답변하여 transcript 상에서 도구 결과가 처리되고 다음 활동(thinking)으로 넘어감
        var state = new TranscriptState("/path/to/test.jsonl", "sess-1", null);
        state.ResolvedUtc["AskUserQuestion"] = DateTime.UtcNow;
        state.BaseActivity = Activity.Thinking;
        state.LastEventUtc = DateTime.UtcNow.AddSeconds(1);

        hub.SettleApprovals([state]);

        var stateJsonAfter = hub.StateJson();
        Assert.DoesNotContain("질문에 답해주세요", stateJsonAfter);

        // approveTask도 즉시 완료되어야 함
        var result = await approveTask.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.NotNull(result);
    }
}
