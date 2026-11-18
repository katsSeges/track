using Track.Core.Parsing;

namespace Track.Core.Tests.Parsing;

public class ClaudeLogParserTests
{
    private const string Line =
        """{"type":"assistant","timestamp":"2026-11-18T16:52:46.997Z","sessionId":"sess-1","requestId":"req_1","message":{"id":"msg_1","model":"claude-opus-5-5","usage":{"input_tokens":2,"cache_creation_input_tokens":23060,"cache_read_input_tokens":48963,"output_tokens":510,"output_tokens_details":{"thinking_tokens":102}}}}""";

    private readonly ClaudeLogParser _parser = new("fallback");

    [Fact]
    public void Parses_usage_into_non_overlapping_parts()
    {
        var r = _parser.Parse(Line)!;

        Assert.Equal(Tool.Claude, r.Tool);
        Assert.Equal("sess-1", r.SessionId);
        Assert.Equal("claude-opus-5-5", r.Model);
        Assert.Equal(new DateTimeOffset(2026, 11, 18, 16, 52, 46, 997, TimeSpan.Zero), r.Timestamp);
        Assert.Equal((2, 23060, 48963, 510, 102), (r.Input, r.CacheWrite, r.CacheRead, r.Output, r.Reasoning));
        Assert.Equal(72535, r.Total);
        Assert.Equal("claude:msg_1:req_1", r.DedupeKey);
    }

    [Fact]
    public void The_same_response_logged_per_content_block_is_counted_once()
    {
        var parser = new ClaudeLogParser("f");

        Assert.NotNull(parser.Parse(Line));
        Assert.Null(parser.Parse(Line.Replace("\"type\":\"assistant\"", "\"type\":\"assistant\",\"apiBlockIndex\":1")));
    }

    [Fact]
    public void Saved_state_keeps_catching_duplicates_after_a_restart()
    {
        var first = new ClaudeLogParser("f");
        first.Parse(Line);

        var restarted = new ClaudeLogParser("f", first.SaveState());

        Assert.Equal(["claude:msg_1:req_1"], first.SaveState().RecentKeys!);
        Assert.Null(restarted.Parse(Line));
    }

    [Fact]
    public void Only_the_most_recent_keys_are_remembered()
    {
        var parser = new ClaudeLogParser("f", new ParserState(RecentKeys: ["claude:msg_1:req_1", "claude:msg_1:req_1"]));
        Assert.Single(parser.SaveState().RecentKeys!);

        for (var i = 0; i < ClaudeLogParser.RecentKeyCapacity; i++)
        {
            Assert.NotNull(parser.Parse(Line.Replace("msg_1", $"other_{i}")));
        }

        var keys = parser.SaveState().RecentKeys!;
        Assert.Equal(ClaudeLogParser.RecentKeyCapacity, keys.Length);
        Assert.DoesNotContain("claude:msg_1:req_1", keys);
        Assert.NotNull(parser.Parse(Line));
    }

    [Fact]
    public void Without_request_id_the_key_is_the_message_id()
    {
        Assert.Equal("claude:msg_1", _parser.Parse(Line.Replace("\"requestId\":\"req_1\",", ""))!.DedupeKey);
    }

    [Fact]
    public void Without_session_id_the_fallback_is_used()
    {
        Assert.Equal("fallback", _parser.Parse(Line.Replace("\"sessionId\":\"sess-1\",", ""))!.SessionId);
    }

    [Fact]
    public void Missing_thinking_details_means_zero_reasoning()
    {
        Assert.Equal(0, _parser.Parse(Line.Replace(",\"output_tokens_details\":{\"thinking_tokens\":102}", ""))!.Reasoning);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("""{"type":"user","message":{"content":"hi"}}""")]
    [InlineData("""{"type":"assistant","usage" broken""")]
    [InlineData("""{"type":"user","message":{"content":"the word \"usage\" in a prompt"}}""")]
    public void Lines_without_usage_give_nothing(string? line)
    {
        Assert.Null(_parser.Parse(line));
    }

    [Theory]
    [InlineData("\"model\":\"claude-opus-5-5\"", "\"model\":\"<synthetic>\"")]
    [InlineData("\"model\":\"claude-opus-5-5\",", "")]
    [InlineData("\"id\":\"msg_1\",", "")]
    [InlineData("\"timestamp\":\"2026-11-18T16:52:46.997Z\",", "")]
    public void Lines_missing_what_a_record_needs_give_nothing(string find, string replace)
    {
        Assert.Null(_parser.Parse(Line.Replace(find, replace)));
    }

    [Fact]
    public void A_response_with_no_tokens_gives_nothing()
    {
        Assert.Null(_parser.Parse("""{"timestamp":"2026-11-18T16:52:46Z","message":{"id":"m","model":"claude-opus-5-5","usage":{"input_tokens":0,"output_tokens":0}}}"""));
    }

    [Fact]
    public void Real_session_fixture_totals_match_hand_computed_numbers()
    {
        // Expected numbers computed separately from the same fixture (first copy of each message id + request id).
        var records = Fixture.Parse(Tool.Claude, "claude.jsonl");

        Assert.Equal(17, records.Count);
        Assert.Equal(36, records.Sum(r => r.Input));
        Assert.Equal(76066, records.Sum(r => r.CacheWrite));
        Assert.Equal(1731430, records.Sum(r => r.CacheRead));
        Assert.Equal(10768, records.Sum(r => r.Output));
        Assert.Equal(1818300, records.Sum(r => r.Total));
        Assert.All(records, r => Assert.Equal("fixture-claude", r.SessionId));
    }
}
