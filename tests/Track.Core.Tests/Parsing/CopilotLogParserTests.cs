using Track.Core.Parsing;

namespace Track.Core.Tests.Parsing;

public class CopilotLogParserTests
{
    private const string Line =
        """{"type":"session.usage_record","id":"ev-1","timestamp":"2026-11-17T10:40:34.053Z","data":{"usage":{"model":"gpt-6.1-sol","inputTokens":46433,"outputTokens":326,"cacheReadTokens":1000,"cacheWriteTokens":45000,"reasoningTokens":210}}}""";

    private readonly CopilotLogParser _parser = new("session-folder");

    [Fact]
    public void Parses_usage_and_takes_cache_tokens_out_of_input()
    {
        var r = _parser.Parse(Line)!;

        Assert.Equal(Tool.Copilot, r.Tool);
        Assert.Equal("session-folder", r.SessionId);
        Assert.Equal("gpt-6.1-sol", r.Model);
        Assert.Equal(new DateTimeOffset(2026, 11, 17, 10, 40, 34, 53, TimeSpan.Zero), r.Timestamp);
        Assert.Equal((433, 45000, 1000, 326, 210), (r.Input, r.CacheWrite, r.CacheRead, r.Output, r.Reasoning));
        Assert.Equal(46759, r.Total);
        Assert.Equal("copilot:ev-1", r.DedupeKey);
    }

    [Fact]
    public void Nothing_needs_saving_between_restarts()
    {
        Assert.Same(ParserState.Empty, _parser.SaveState());
    }

    [Fact]
    public void Cache_larger_than_input_never_gives_negative_input()
    {
        Assert.Equal(0, _parser.Parse(Line.Replace("\"inputTokens\":46433", "\"inputTokens\":100"))!.Input);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("""{"type":"user.message","id":"u","data":{}}""")]
    [InlineData("""{"type":"assistant.message","data":{"content":"session.usage_record"}}""")]
    [InlineData("""session.usage_record {broken""")]
    public void Lines_without_usage_give_nothing(string? line)
    {
        Assert.Null(_parser.Parse(line));
    }

    [Theory]
    [InlineData("\"model\":\"gpt-6.1-sol\",", "")]
    [InlineData("\"id\":\"ev-1\",", "")]
    [InlineData("\"timestamp\":\"2026-11-17T10:40:34.053Z\",", "")]
    public void Lines_missing_what_a_record_needs_give_nothing(string find, string replace)
    {
        Assert.Null(_parser.Parse(Line.Replace(find, replace)));
    }

    [Fact]
    public void A_call_with_no_tokens_gives_nothing()
    {
        Assert.Null(_parser.Parse("""{"type":"session.usage_record","id":"e","timestamp":"2026-11-17T10:40:34Z","data":{"usage":{"model":"m"}}}"""));
    }

    [Fact]
    public void Real_session_fixture_totals_match_hand_computed_numbers()
    {
        var records = Fixture.Parse(Tool.Copilot, "copilot.jsonl", "fixture-copilot");

        Assert.Equal(13, records.Count);
        Assert.Equal(39, records.Sum(r => r.Input));
        Assert.Equal(91224, records.Sum(r => r.CacheWrite));
        Assert.Equal(754905, records.Sum(r => r.CacheRead));
        Assert.Equal(8466, records.Sum(r => r.Output));
        Assert.Equal(5278, records.Sum(r => r.Reasoning));
        Assert.Equal(854634, records.Sum(r => r.Total));
    }
}
