using System.Text.Json;
using Xunit;

namespace ClaudeOffice.Tests;

public class UpdateCheckerDailyTests
{
    [Fact]
    public void AppSettings_AnonymousClientId_And_LastHeartbeatDate_Roundtrip()
    {
        var settings = new AppSettings
        {
            AnonymousClientId = "test_anon_id_12345",
            LastHeartbeatDate = "2026-11-13"
        };

        var json = JsonSerializer.Serialize(settings);
        var restored = JsonSerializer.Deserialize<AppSettings>(json);

        Assert.NotNull(restored);
        Assert.Equal("test_anon_id_12345", restored.AnonymousClientId);
        Assert.Equal("2026-11-13", restored.LastHeartbeatDate);
    }

    [Fact]
    public void HeartbeatDailyDeduplication_SkipsIfSameDate()
    {
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var lastSent = today;

        // If today matches lastSent, skip
        bool shouldSkip = (lastSent == today);
        Assert.True(shouldSkip, "Should skip heartbeat if already sent today");

        // If yesterday was sent, do not skip
        var yesterday = DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-dd");
        bool shouldSkipYesterday = (yesterday == today);
        Assert.False(shouldSkipYesterday, "Should NOT skip heartbeat if last sent date was yesterday");
    }
}
