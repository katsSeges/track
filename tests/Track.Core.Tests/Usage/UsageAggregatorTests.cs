using Track.Core.Ingest;
using Track.Core.Parsing;
using Track.Core.Pricing;
using Track.Core.Usage;

namespace Track.Core.Tests.Usage;

public class UsageAggregatorTests
{
    private static readonly DateTimeOffset Hour = new(2026, 11, 18, 10, 0, 0, TimeSpan.Zero);
    private static readonly TimeRange Day = new(Hour.AddHours(-10), Hour.AddHours(14));

    // $1 per input token and $10 per output token keeps the arithmetic obvious.
    private static readonly PriceTable Prices = PriceTable.Parse("""
        {"cheap":{"input_cost_per_token":1,"output_cost_per_token":10},
         "pricey":{"input_cost_per_token":2,"output_cost_per_token":20}}
        """)!;

    private readonly UsageStore _store = new();
    private readonly UsageAggregator _usage;

    public UsageAggregatorTests() => _usage = new UsageAggregator(_store) { Prices = Prices };

    private void Add(Tool tool, string model, DateTimeOffset at, long input, long output = 0) =>
        _store.Add([new UsageRecord(tool, "s", model, at, input, 0, 0, output, 0, "k")]);

    [Fact]
    public void Last_used_is_the_latest_session_activity_of_that_tool()
    {
        Assert.Null(_usage.LastUsed(Tool.Claude));
        Add(Tool.Claude, "cheap", Hour.AddMinutes(-30), 1);
        Add(Tool.Claude, "cheap", Hour.AddMinutes(12), 1);
        Add(Tool.Codex, "cheap", Hour.AddHours(2), 1);

        Assert.Equal(Hour.AddMinutes(12), _usage.LastUsed(Tool.Claude));
    }

    [Fact]
    public void Totals_sum_tokens_and_cost_for_a_tool_inside_the_range()
    {
        Add(Tool.Claude, "cheap", Hour, 100, 10);
        Add(Tool.Claude, "pricey", Hour.AddHours(1), 5);
        Add(Tool.Claude, "cheap", Hour.AddDays(-2), 999);
        Add(Tool.Codex, "cheap", Hour, 7);

        var claude = _usage.Totals(Tool.Claude, Day);

        Assert.Equal(115, claude.Tokens.Total);
        Assert.Equal(100 + 100 + 10, claude.Cost);
        Assert.False(claude.IsPartial);
        Assert.Equal(122, _usage.Totals(null, Day).Tokens.Total);
    }

    [Fact]
    public void Unpriced_models_count_tokens_and_mark_the_cost_as_partial()
    {
        Add(Tool.Codex, "cheap", Hour, 10);
        Add(Tool.Codex, "codex-auto-review", Hour, 1000);

        var codex = _usage.Totals(Tool.Codex, Day);

        Assert.Equal(1010, codex.Tokens.Total);
        Assert.Equal(10m, codex.Cost);
        Assert.True(codex.IsPartial);
    }

    [Fact]
    public void Only_unpriced_models_means_no_cost_at_all()
    {
        Add(Tool.Codex, "unknown", Hour, 50);

        var codex = _usage.Totals(Tool.Codex, Day);

        Assert.Null(codex.Cost);
        Assert.False(codex.IsPartial);
        Assert.Equal(50, codex.UnpricedTokens);
    }

    [Fact]
    public void No_usage_costs_zero()
    {
        var none = _usage.Totals(Tool.Copilot, Day);

        Assert.Equal(UsageTotals.Empty, none);
        Assert.Equal(0m, none.Cost);
        Assert.False(none.IsPartial);
    }

    [Fact]
    public void By_model_lists_largest_first_and_adds_up_to_the_tool_total()
    {
        Add(Tool.Copilot, "cheap", Hour, 5);
        Add(Tool.Copilot, "pricey", Hour, 50);
        Add(Tool.Copilot, "cheap", Hour.AddHours(2), 40);
        Add(Tool.Copilot, "zzz", Hour, 3);
        Add(Tool.Copilot, "aaa", Hour, 3);

        var models = _usage.ByModel(Tool.Copilot, Day);

        Assert.Equal(["pricey", "cheap", "aaa", "zzz"], models.Select(m => m.Model));
        Assert.Equal(45, models[1].Totals.Tokens.Total);
        Assert.Null(models[2].Totals.Cost);
        Assert.Equal(_usage.Totals(Tool.Copilot, Day).Tokens.Total, models.Sum(m => m.Totals.Tokens.Total));
    }

    [Fact]
    public void Latest_model_is_the_biggest_one_in_the_newest_hour()
    {
        Assert.Null(_usage.LatestModel(Tool.Claude));

        Add(Tool.Claude, "old", Hour.AddHours(-3), 1000);
        Add(Tool.Claude, "small", Hour, 1);
        Add(Tool.Claude, "big", Hour, 9);
        Add(Tool.Codex, "other-tool", Hour.AddHours(5), 1);

        Assert.Equal("big", _usage.LatestModel(Tool.Claude));
    }

    [Fact]
    public void At_monday_midnight_the_week_total_leaves_out_sunday()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Copenhagen");
        var mondayMidnight = new DateTimeOffset(2026, 11, 16, 0, 0, 0, TimeSpan.FromHours(1));
        var periods = new PeriodCalculator(new Microsoft.Extensions.Time.Testing.FakeTimeProvider(mondayMidnight), zone);
        Add(Tool.Claude, "cheap", mondayMidnight.AddMinutes(-1), 500); // Sunday 23:59 local
        Add(Tool.Claude, "cheap", mondayMidnight, 7);                  // Monday 00:00 local

        Assert.Equal(7, _usage.Totals(Tool.Claude, periods.Range(Period.Week)).Tokens.Total);
        Assert.Equal(500, _usage.Totals(Tool.Claude, periods.Range(WeekWindow.LastFullWeek)).Tokens.Total);
    }

    [Fact]
    public void Uses_the_bundled_prices_until_replaced()
    {
        Assert.Same(PriceTable.Bundled, new UsageAggregator(_store).Prices);
    }
}
