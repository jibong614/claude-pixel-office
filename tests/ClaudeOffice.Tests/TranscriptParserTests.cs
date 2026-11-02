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
}
