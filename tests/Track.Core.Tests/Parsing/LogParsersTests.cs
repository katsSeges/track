using Track.Core.Parsing;

namespace Track.Core.Tests.Parsing;

public class LogParsersTests
{
    [Theory]
    [InlineData(Tool.Claude, typeof(ClaudeLogParser))]
    [InlineData(Tool.Codex, typeof(CodexLogParser))]
    [InlineData(Tool.Copilot, typeof(CopilotLogParser))]
    public void Create_returns_the_parser_for_each_tool(Tool tool, Type expected)
    {
        Assert.IsType(expected, LogParsers.Create(tool, "s"));
    }

    [Fact]
    public void Create_passes_saved_state_to_the_parser()
    {
        var parser = LogParsers.Create(Tool.Codex, "s", new ParserState(SessionId: "saved", Model: "m"));

        Assert.Equal(("saved", "m"), (parser.SaveState().SessionId, parser.SaveState().Model));
    }

    [Fact]
    public void Create_rejects_an_unknown_tool()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LogParsers.Create((Tool)99, "s"));
    }

    [Fact]
    public void Total_is_the_sum_of_the_non_overlapping_parts()
    {
        var r = new UsageRecord(Tool.Claude, "s", "m", DateTimeOffset.UnixEpoch, 1, 20, 300, 4000, 50, "k");

        Assert.Equal(4321, r.Total);
    }
}
