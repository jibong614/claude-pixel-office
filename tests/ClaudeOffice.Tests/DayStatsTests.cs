using System.Text.Json;
using ClaudeOffice.Monitoring;
using Xunit;

namespace ClaudeOffice.Tests;

public class DayStatsTests
{
    [Fact]
    public void ContextTokens_UpdatesFromAssistantUsage()
    {
        var stats = new DayStats();
        var now = DateTime.UtcNow;

        string jsonAssistant = @"{
            ""type"": ""assistant"",
            ""message"": {
                ""id"": ""msg-100"",
                ""usage"": {
                    ""input_tokens"": 50000,
                    ""cache_creation_input_tokens"": 10000,
                    ""cache_read_input_tokens"": 25000,
                    ""output_tokens"": 1500
                }
            }
        }";

        using var doc = JsonDocument.Parse(jsonAssistant);
        stats.Add(doc.RootElement, "assistant", now);

        // LastContextTokens = input + cache_read + cache_creation = 50000 + 25000 + 10000 = 85000
        Assert.Equal(85000, stats.LastContextTokens);

        var bucket = stats.GetDay(now.ToLocalTime());
        Assert.NotNull(bucket);
        Assert.True(bucket.Messages.ContainsKey("msg-100"));
        Assert.Equal(50000, bucket.Messages["msg-100"].In);
        Assert.Equal(1500, bucket.Messages["msg-100"].Out);
    }

    [Fact]
    public void Add_CountsPromptsOnUserMessage()
    {
        var stats = new DayStats();
        var now = DateTime.UtcNow;

        string jsonUser = @"{
            ""type"": ""user"",
            ""message"": {
                ""content"": ""Hello Claude""
            }
        }";

        using var doc = JsonDocument.Parse(jsonUser);
        stats.Add(doc.RootElement, "user", now);

        var bucket = stats.GetDay(now.ToLocalTime());
        Assert.NotNull(bucket);
        Assert.Equal(1, bucket.Prompts);
    }
}
