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
}
