using Track.Core.Parsing;

namespace Track.Core.Tests.Parsing;

public class CodexLogParserTests
{
    private const string Meta = """{"timestamp":"2026-11-18T09:11:10Z","type":"session_meta","payload":{"id":"sess-x"}}""";
    private const string Context = """{"timestamp":"2026-11-18T09:11:11Z","type":"turn_context","payload":{"model":"gpt-5.6-luna"}}""";

    private static string Tokens(long input, long cached, long output, long reasoning, string ts = "2026-11-18T09:11:13Z", long cacheWrite = 0)
    {
        var usage = $"\"input_tokens\":{input},\"cached_input_tokens\":{cached},\"cache_write_input_tokens\":{cacheWrite},"
                  + $"\"output_tokens\":{output},\"reasoning_output_tokens\":{reasoning},\"total_tokens\":{input + output}";
        return "{\"timestamp\":\"" + ts + "\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"info\":{\"total_token_usage\":{"
             + usage + "}}}}";
    }

    [Fact]
    public void First_event_counts_the_whole_cumulative_total_split_into_parts()
    {
        var parser = new CodexLogParser("file-id");
        Assert.Null(parser.Parse(Meta));
        Assert.Null(parser.Parse(Context));

        var r = parser.Parse(Tokens(input: 13379, cached: 4000, output: 25, reasoning: 5))!;

        Assert.Equal(Tool.Codex, r.Tool);
        Assert.Equal("sess-x", r.SessionId);
        Assert.Equal("gpt-5.6-luna", r.Model);
        Assert.Equal(new DateTimeOffset(2026, 11, 18, 9, 11, 13, TimeSpan.Zero), r.Timestamp);
        Assert.Equal((9379, 0, 4000, 25, 5), (r.Input, r.CacheWrite, r.CacheRead, r.Output, r.Reasoning));
        Assert.Equal(13404, r.Total);
        Assert.Equal("codex:sess-x:13404", r.DedupeKey);
    }

    [Fact]
    public void Later_events_count_only_the_growth()
    {
        var parser = new CodexLogParser("f");
        parser.Parse(Tokens(1000, 0, 100, 10));

        var r = parser.Parse(Tokens(5000, 3000, 160, 30))!;

        Assert.Equal((1000, 0, 3000, 60, 20), (r.Input, r.CacheWrite, r.CacheRead, r.Output, r.Reasoning));
        Assert.Equal(4060, r.Total);
    }

    [Fact]
    public void Cache_writes_are_taken_out_of_input()
    {
        var r = new CodexLogParser("f").Parse(Tokens(1000, 200, 10, 0, cacheWrite: 300))!;

        Assert.Equal((500, 300, 200), (r.Input, r.CacheWrite, r.CacheRead));
        Assert.Equal(1010, r.Total);
    }

    [Fact]
    public void A_repeated_event_gives_nothing()
    {
        var parser = new CodexLogParser("f");
        parser.Parse(Tokens(1000, 0, 100, 10));

        Assert.Null(parser.Parse(Tokens(1000, 0, 100, 10, ts: "2026-11-18T09:12:00Z")));
    }

    [Fact]
    public void Counters_going_backwards_start_a_new_baseline()
    {
        var parser = new CodexLogParser("f");
        parser.Parse(Tokens(1000, 0, 100, 10));

        var r = parser.Parse(Tokens(300, 0, 20, 0))!;
        var next = parser.Parse(Tokens(400, 0, 30, 0))!;

        Assert.Equal(320, r.Total);
        Assert.Equal(110, next.Total);
    }

    [Fact]
    public void Without_session_meta_or_turn_context_the_fallbacks_are_used()
    {
        var parser = new CodexLogParser("rollout-file");
        parser.Parse("""{"type":"session_meta","payload":{}}""");
        parser.Parse("""{"type":"turn_context","payload":{"cwd":"x"}}""");

        var r = parser.Parse(Tokens(10, 0, 1, 0))!;

        Assert.Equal("rollout-file", r.SessionId);
        Assert.Equal(CodexLogParser.UnknownModel, r.Model);
    }

    [Fact]
    public void Saved_state_continues_the_growth_after_a_restart()
    {
        var first = new CodexLogParser("f");
        first.Parse(Meta);
        first.Parse(Context);
        first.Parse(Tokens(1000, 0, 100, 10));

        var state = first.SaveState();
        var restarted = new CodexLogParser("other", state);
        var r = restarted.Parse(Tokens(1500, 0, 120, 10))!;

        Assert.Equal(("sess-x", "gpt-5.6-luna"), (state.SessionId, state.Model));
        Assert.Equal([1000, 0, 0, 100, 10, 1100], state.Previous!);
        Assert.Equal(("sess-x", "gpt-5.6-luna", 520L), (r.SessionId, r.Model, r.Total));
    }

    [Fact]
    public void Saved_totals_of_the_wrong_shape_start_from_zero()
    {
        var r = new CodexLogParser("f", new ParserState(Previous: [1, 2, 3])).Parse(Tokens(100, 0, 10, 0))!;

        Assert.Equal(110, r.Total);
        Assert.Equal(CodexLogParser.UnknownModel, r.Model);
        Assert.Equal("f", r.SessionId);
    }

    [Fact]
    public void Cached_more_than_input_never_gives_negative_input()
    {
        Assert.Equal(0, new CodexLogParser("f").Parse(Tokens(100, 150, 5, 0))!.Input);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("""{"type":"response_item","payload":{"type":"message"}}""")]
    [InlineData("""{"type":"event_msg","payload":{"type":"token_count","info":null},"timestamp":"2026-11-18T09:11:13Z"}""")]
    [InlineData("""{"type":"event_msg","payload":{"type":"agent_message","text":"token_count"}}""")]
    [InlineData("""{"type":"response_item","payload":{"text":"session_meta turn_context"}}""")]
    [InlineData("""{"type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"total_tokens":5}}}}""")]
    [InlineData("""token_count {broken""")]
    public void Lines_without_new_usage_give_nothing(string? line)
    {
        Assert.Null(new CodexLogParser("f").Parse(line));
    }

    [Fact]
    public void Real_session_fixture_adds_up_to_the_final_cumulative_totals()
    {
        // The fixture's last token_count reports these cumulative totals; the growth records must add up to them.
        var records = Fixture.Parse(Tool.Codex, "codex.jsonl");

        Assert.Equal(31, records.Count);
        Assert.Equal(122624, records.Sum(r => r.Input));
        Assert.Equal(0, records.Sum(r => r.CacheWrite));
        Assert.Equal(2580608, records.Sum(r => r.CacheRead));
        Assert.Equal(15813, records.Sum(r => r.Output));
        Assert.Equal(4412, records.Sum(r => r.Reasoning));
        Assert.Equal(2719045, records.Sum(r => r.Total));
        Assert.All(records, r => Assert.Equal("fixture-codex", r.SessionId));
    }
}
