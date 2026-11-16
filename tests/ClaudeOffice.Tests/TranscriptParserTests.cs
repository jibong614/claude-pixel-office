using System.Text;
using ClaudeOffice.Monitoring;
using Xunit;

namespace ClaudeOffice.Tests;

public class TranscriptParserTests
{
    [Fact]
    public void Apply_ParsesCustomTitleAndAiTitle()
    {
        var state = new TranscriptState("test.jsonl", "sess-1", null);
        string json1 = "{\"type\":\"custom-title\",\"customTitle\":\"My Special Project\"}";
        string json2 = "{\"type\":\"ai-title\",\"aiTitle\":\"Auth Refactor\"}";

        TranscriptParser.Apply(state, Encoding.UTF8.GetBytes(json1));
        TranscriptParser.Apply(state, Encoding.UTF8.GetBytes(json2));

        Assert.Equal("My Special Project", state.CustomTitle);
        Assert.Equal("Auth Refactor", state.AiTitle);
    }

    [Fact]
    public void Apply_DetectsSecretInMessage()
    {
        var state = new TranscriptState("test.jsonl", "sess-2", null);
        // sk-ant-api03-... pattern
        string jsonWithSecret = "{\"type\":\"assistant\",\"message\":{\"id\":\"m1\",\"content\":[{\"type\":\"text\",\"text\":\"Here is your key: sk-ant-api03-abcdef1234567890abcdef1234567890\"}]}}";

        TranscriptParser.Apply(state, Encoding.UTF8.GetBytes(jsonWithSecret));

        Assert.True(state.HasSecret);
    }

    [Fact]
    public void Apply_UpdatesPromptFromUserMessage()
    {
        var state = new TranscriptState("test.jsonl", "sess-3", null);
        string jsonUser = "{\"type\":\"user\",\"message\":{\"content\":\"Fix the bug in main loop\"}}";

        TranscriptParser.Apply(state, Encoding.UTF8.GetBytes(jsonUser));

        Assert.True(state.Seen);
        Assert.Equal("Fix the bug in main loop", state.LastPrompt);
    }

    [Fact]
    public void Apply_Antigravity_UserInput_UpdatesPromptAndThinking()
    {
        var state = new TranscriptState("transcript.jsonl", "agy-1", null);
        string json = "{\"step_index\":10,\"source\":\"USER_EXPLICIT\",\"type\":\"USER_INPUT\",\"status\":\"DONE\",\"created_at\":\"2026-11-16T08:00:00Z\",\"content\":\"<USER_REQUEST>\\n버그 수정해줘\\n</USER_REQUEST>\"}";

        TranscriptParser.Apply(state, Encoding.UTF8.GetBytes(json));

        Assert.True(state.Seen);
        Assert.Equal("antigravity", state.Entrypoint);
        Assert.Equal("버그 수정해줘", state.LastPrompt);
        Assert.Equal(Activity.Thinking, state.BaseActivity);
    }

    [Fact]
    public void Apply_Antigravity_ToolCalls_UpdatesRunningStatus()
    {
        var state = new TranscriptState("transcript.jsonl", "agy-2", null);
        string json = "{\"step_index\":11,\"source\":\"MODEL\",\"type\":\"PLANNER_RESPONSE\",\"status\":\"DONE\",\"created_at\":\"2026-11-16T08:00:05Z\",\"tool_calls\":[{\"name\":\"run_command\",\"args\":{\"toolSummary\":\"테스트 빌드 실행\"}}]}";

        TranscriptParser.Apply(state, Encoding.UTF8.GetBytes(json));

        Assert.Equal(Activity.Running, state.BaseActivity);
        Assert.Equal("⌨️", state.BaseIcon);
        Assert.Equal("테스트 빌드 실행", state.BaseText);
    }

    [Fact]
    public void Apply_Antigravity_Completion_UpdatesIdleStatus()
    {
        var state = new TranscriptState("transcript.jsonl", "agy-3", null);
        string json = "{\"step_index\":12,\"source\":\"MODEL\",\"type\":\"PLANNER_RESPONSE\",\"status\":\"DONE\",\"created_at\":\"2026-11-16T08:00:10Z\",\"content\":\"모든 작업이 완료되었습니다.\"}";

        TranscriptParser.Apply(state, Encoding.UTF8.GetBytes(json));

        Assert.Equal(Activity.Idle, state.BaseActivity);
        Assert.Equal("✨", state.BaseIcon);
        Assert.Equal("모든 작업이 완료되었습니다.", state.LastReply);
    }
}
