using Microsoft.Extensions.Time.Testing;
using Track.Core.Display;
using Track.Core.Ingest;
using Track.Core.Parsing;
using Track.Core.Pricing;
using Track.Core.Usage;

namespace Track.Core.Tests.Display;

public class AnalyticsBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Builds_range_local_provider_model_heatmap_and_full_session_analytics()
    {
        var store = new UsageStore();
        store.Add(
        [
            Record(Tool.Claude, "s,\"cross", "priced", Now.AddDays(-2).AddHours(11).AddMinutes(30), 3, 0, 0, 0, "before"),
            Record(Tool.Claude, "s,\"cross", "priced", Now.AddDays(-1).AddHours(-11).AddMinutes(15), 10, 5, 15, 20, "inside"),
            Record(Tool.Codex, "codex-session", "unpriced", Now.AddMinutes(75), 80, 0, 0, 0, "codex"),
            Record(Tool.Claude, "last-session", "priced", Now.AddHours(11).AddMinutes(59), 2, 0, 0, 0, "last"),
            Record(Tool.Codex, "at-end", "unpriced", Now.AddHours(12), 100, 0, 0, 0, "end"),
            Record(Tool.Claude, "before-range", "priced", Now.AddDays(-2).AddHours(10), 1, 0, 0, 0, "past"),
            Record(Tool.Ollama, "ollama-session", "local-model", Now.AddHours(-1), 500, 0, 0, 0, "ollama"),
        ]);
        var builder = CreateBuilder(store);
        var range = builder.CustomRange(new DateOnly(2026, 11, 17), new DateOnly(2026, 11, 18));

        var result = builder.Build(range, [Tool.Claude, Tool.Codex, Tool.Ollama]);

        Assert.Equal([Tool.Claude, Tool.Codex], result.Providers);
        Assert.Equal(2, result.Days.Count);
        Assert.Equal(50, result.Days[0].Providers[Tool.Claude].Tokens.Total);
        Assert.Equal(82, result.Days[1].Providers[Tool.Claude].Tokens.Total + result.Days[1].Providers[Tool.Codex].Tokens.Total);
        Assert.Equal(132, result.Total.Tokens.Total);
        Assert.Equal(127m, result.Total.KnownCost);
        Assert.Equal(80, result.Total.UnpricedTokens);
        Assert.Equal(15m / 112m, result.CacheHitRate);
        Assert.Equal(168, result.Heatmap.Count);
        Assert.Equal(50, result.Heatmap.Single(cell => (cell.DayOfWeekMondayFirst, cell.Hour) == (1, 1)).Tokens);
        Assert.Equal(80, result.Heatmap.Single(cell => (cell.DayOfWeekMondayFirst, cell.Hour) == (2, 13)).Tokens);
        Assert.Equal(["unpriced", "priced"], result.Models.Select(model => model.Model));

        Assert.Equal(3, result.SessionCount);
        Assert.Equal(TimeSpan.Zero, result.MedianObservedSpan);
        Assert.Equal(["codex-session", "s,\"cross", "last-session"], result.TopSessions.Select(session => session.SessionId));
        Assert.Equal(53, result.TopSessions[1].Usage.Tokens.Total);
        Assert.Equal(TimeSpan.FromMinutes(105), result.TopSessions[1].ObservedSpan);

        Assert.Equal("", AnalyticsCsv.Escape(null));
        Assert.Equal("", AnalyticsCsv.Escape(""));
        var csv = AnalyticsCsv.Write(result);
        Assert.Contains("summary,", csv);
        Assert.Contains("daily_provider,2026-11-17,Claude Code", csv);
        Assert.Contains("\"s,\"\"cross\"", csv);
        Assert.Contains("heatmap,", csv);
        var heatmapRow = csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .First(line => line.StartsWith("heatmap,", StringComparison.Ordinal)
                && line.Split(',')[8] == "1"
                && line.Split(',')[9] == "1");
        var heatmapFields = heatmapRow.Split(',');
        Assert.Equal("", heatmapFields[14]);
        Assert.Equal("50", heatmapFields[16]);
    }

    [Fact]
    public void Uses_local_calendar_days_and_limits_ranges_to_retention()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Copenhagen");
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 3, 29, 12, 0, 0, TimeSpan.Zero));
        var builder = CreateBuilder(new UsageStore(), time, zone);

        var oneDay = builder.LastDays(1);

        Assert.Equal(new DateOnly(2026, 3, 29), oneDay.StartDate);
        Assert.Equal(TimeSpan.FromHours(23), oneDay.UtcRange.EndUtc - oneDay.UtcRange.StartUtc);
        Assert.Equal(90, builder.LastDays(90).Days);
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.LastDays(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.LastDays(91));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.CustomRange(new DateOnly(2026, 11, 18), new DateOnly(2026, 11, 17)));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.CustomRange(new DateOnly(2026, 8, 1), new DateOnly(2026, 10, 30)));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.CustomRange(DateOnly.MaxValue, DateOnly.MaxValue));
    }

    [Fact]
    public void Empty_and_even_session_ranges_have_null_cache_rate_and_exact_median()
    {
        var store = new UsageStore();
        var builder = CreateBuilder(store);
        var range = builder.CustomRange(new DateOnly(2026, 11, 17), new DateOnly(2026, 11, 18));
        var empty = builder.Build(range, [Tool.Ollama]);

        Assert.Empty(empty.Providers);
        Assert.Null(empty.CacheHitRate);
        Assert.Equal(0, empty.SessionCount);
        Assert.Null(empty.MedianObservedSpan);

        store.Add(
        [
            Record(Tool.Claude, "one-hour", "unknown", Now.AddDays(-1).AddHours(1), 0, 0, 0, 0, "a"),
            Record(Tool.Claude, "one-hour", "unknown", Now.AddDays(-1).AddHours(2), 0, 0, 0, 0, "b"),
            Record(Tool.Claude, "three-hour", "unknown", Now.AddHours(-4), 0, 0, 0, 0, "c"),
            Record(Tool.Claude, "three-hour", "unknown", Now.AddHours(-1), 0, 0, 0, 0, "d"),
        ]);

        var result = builder.Build(range, [Tool.Claude]);

        Assert.Equal(2, result.SessionCount);
        Assert.Equal(TimeSpan.FromHours(2), result.MedianObservedSpan);
        Assert.Contains(",0,0,", AnalyticsCsv.Write(result));
    }

    private static AnalyticsBuilder CreateBuilder(UsageStore store, TimeProvider? time = null, TimeZoneInfo? zone = null)
    {
        time ??= new FakeTimeProvider(Now);
        zone ??= TimeZoneInfo.Utc;
        var periods = new PeriodCalculator(time, zone);
        var prices = PriceTable.Parse("""{"priced":{"input_cost_per_token":1,"output_cost_per_token":2,"cache_creation_input_token_cost":3,"cache_read_input_token_cost":4}}""")!;
        return new AnalyticsBuilder(store, new UsageAggregator(store) { Prices = prices }, periods, time);
    }

    private static UsageRecord Record(
        Tool tool,
        string session,
        string model,
        DateTimeOffset at,
        long input,
        long cacheWrite,
        long cacheRead,
        long output,
        string key) =>
        new(tool, session, model, at, input, cacheWrite, cacheRead, output, 0, key);

    [Theory]
    [InlineData("=HYPERLINK(\"x\")", "\"'=HYPERLINK(\"\"x\"\")\"")]
    [InlineData("+cmd", "'+cmd")]
    [InlineData("-rm", "'-rm")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("\tx", "'\tx")]
    [InlineData("\rx", "\"'\rx\"")]
    [InlineData("-12.5", "-12.5")]
    [InlineData("+3", "+3")]
    [InlineData("claude-opus-5-5", "claude-opus-5-5")]
    public void Csv_cells_from_logs_cannot_run_as_spreadsheet_formulas(string value, string expected)
    {
        Assert.Equal(expected, AnalyticsCsv.Escape(value));
    }
}