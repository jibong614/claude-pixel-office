using ClaudeOffice.Monitoring;
using Xunit;

namespace ClaudeOffice.Tests;

public class ToolCatalogTests
{
    [Theory]
    [InlineData("Read", Activity.Reading, "📄")]
    [InlineData("NotebookRead", Activity.Reading, "📄")]
    [InlineData("Skill", Activity.Reading, "📚")]
    [InlineData("Edit", Activity.Writing, "✏️")]
    [InlineData("Write", Activity.Writing, "✏️")]
    [InlineData("Grep", Activity.Searching, "🔍")]
    [InlineData("Glob", Activity.Searching, "🔍")]
    [InlineData("Bash", Activity.Running, "⌨️")]
    [InlineData("PowerShell", Activity.Running, "⌨️")]
    [InlineData("WebSearch", Activity.Browsing, "🌐")]
    [InlineData("WebFetch", Activity.Browsing, "🌐")]
    [InlineData("Agent", Activity.Delegating, "👥")]
    [InlineData("Task", Activity.Delegating, "👥")]
    [InlineData("TodoWrite", Activity.Planning, "📋")]
    [InlineData("AskUserQuestion", Activity.Asking, "✋")]
    [InlineData("mcp__sqlite__query", Activity.Working, "🔌")]
    [InlineData("UnknownCustomTool", Activity.Working, "🛠️")]
    public void Classify_ReturnsExpectedActivityAndIcon(string tool, Activity expectedAct, string expectedIcon)
    {
        var (act, icon) = ToolCatalog.Classify(tool);
        Assert.Equal(expectedAct, act);
        Assert.Equal(expectedIcon, icon);
    }

    [Theory]
    [InlineData("Edit", true)]
    [InlineData("MultiEdit", true)]
    [InlineData("Write", true)]
    [InlineData("NotebookEdit", true)]
    [InlineData("Read", false)]
    [InlineData("Bash", false)]
    public void IsEditTool_ClassifiesCorrectly(string tool, bool expected)
    {
        Assert.Equal(expected, ToolCatalog.IsEditTool(tool));
    }

    [Theory]
    [InlineData("안녕하세요", true)]
    [InlineData("Claude Office", false)]
    [InlineData("코드 리팩토링 진행", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void HasKorean_DetectsHangulAccurately(string? text, bool expected)
    {
        Assert.Equal(expected, ToolCatalog.HasKorean(text));
    }
}
