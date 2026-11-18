using System.Text.Json;
using Track.Core.Parsing;

namespace Track.Core.Tests.Parsing;

public class JsonLineTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{not json")]
    [InlineData("[1,2]")]
    [InlineData("42")]
    public void Parse_returns_null_for_lines_that_are_not_a_json_object(string? line)
    {
        Assert.Null(JsonLine.Parse(line));
    }

    [Fact]
    public void Parse_returns_the_object_and_it_outlives_the_document()
    {
        var root = JsonLine.Parse("""{"a":{"b":"c"}}""");

        Assert.Equal("c", JsonLine.String(JsonLine.Get(root, "a", "b")));
    }

    [Fact]
    public void Get_returns_null_when_a_step_is_missing_or_not_an_object()
    {
        var root = JsonLine.Parse("""{"a":{"b":"c"},"n":1}""");

        Assert.Null(JsonLine.Get(root, "missing"));
        Assert.Null(JsonLine.Get(root, "n", "x"));
        Assert.Null(JsonLine.Get(null, "a"));
        Assert.Equal(JsonValueKind.Object, JsonLine.Get(root)!.Value.ValueKind);
    }

    [Fact]
    public void String_only_reads_json_strings()
    {
        var root = JsonLine.Parse("""{"s":"x","n":1}""");

        Assert.Equal("x", JsonLine.String(JsonLine.Get(root, "s")));
        Assert.Null(JsonLine.String(JsonLine.Get(root, "n")));
        Assert.Null(JsonLine.String(null));
    }

    [Theory]
    [InlineData("""{"v":12}""", 12)]
    [InlineData("""{"v":0}""", 0)]
    [InlineData("""{"v":-5}""", 0)]
    [InlineData("""{"v":1.5}""", 0)]
    [InlineData("""{"v":"12"}""", 0)]
    [InlineData("""{"v":null}""", 0)]
    [InlineData("""{}""", 0)]
    public void Long_reads_non_negative_whole_numbers_and_defaults_to_zero(string json, long expected)
    {
        Assert.Equal(expected, JsonLine.Long(JsonLine.Get(JsonLine.Parse(json), "v")));
    }

    [Fact]
    public void Timestamp_parses_iso_text_as_utc()
    {
        var root = JsonLine.Parse("""{"z":"2026-11-18T16:52:44.936Z","o":"2026-11-18T17:52:44+01:00","bad":"yesterday","n":5}""");

        Assert.Equal(new DateTimeOffset(2026, 11, 18, 16, 52, 44, 936, TimeSpan.Zero), JsonLine.Timestamp(JsonLine.Get(root, "z")));
        Assert.Equal(TimeSpan.Zero, JsonLine.Timestamp(JsonLine.Get(root, "o"))!.Value.Offset);
        Assert.Equal(new DateTimeOffset(2026, 11, 18, 16, 52, 44, TimeSpan.Zero), JsonLine.Timestamp(JsonLine.Get(root, "o")));
        Assert.Null(JsonLine.Timestamp(JsonLine.Get(root, "bad")));
        Assert.Null(JsonLine.Timestamp(JsonLine.Get(root, "n")));
    }
}
