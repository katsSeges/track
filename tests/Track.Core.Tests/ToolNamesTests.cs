using Track.Core;

namespace Track.Core.Tests;

public class ToolNamesTests
{
    [Theory]
    [InlineData(Tool.Claude, "Claude Code")]
    [InlineData(Tool.Codex, "Codex")]
    [InlineData(Tool.Copilot, "Copilot")]
    public void Display_returns_the_name_shown_in_the_ui(Tool tool, string expected)
    {
        Assert.Equal(expected, ToolNames.Display(tool));
    }

    [Fact]
    public void Display_rejects_an_unknown_tool()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ToolNames.Display((Tool)99));
    }
}
