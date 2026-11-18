using Microsoft.Extensions.Time.Testing;
using Track.Core.Ingest;
using Track.Core.Parsing;
using Track.Core.Usage;

namespace Track.Core.Tests.Usage;

public class ToolVisibilityPolicyTests
{
    private static readonly VisibilitySettings Defaults = new();
    private static readonly VisibilitySettings Either = new() { TrayRule = TrayRule.BelowEither };
    private static readonly VisibilitySettings Strict = new() { ShowWhenRunning = false };

    [Fact]
    public void Defaults_match_the_round_3_choices()
    {
        Assert.Equal(
            (5_000_000L, 10_000_000L, TrayRule.BelowBoth, WeekWindow.LastFullWeek, MonthWindow.LastFullMonth, true, 30),
            (Defaults.TrayLimitWeek, Defaults.TrayLimitMonth, Defaults.TrayRule, Defaults.WeekWindow, Defaults.MonthWindow, Defaults.ShowWhenRunning, Defaults.RemoveAfterNoUsageDays));
    }

    [Theory]
    // week, month, running → in tray, reason (rule: below both limits)
    [InlineData(5_000_000, 0, false, true, VisibilityReason.Used)]          // exactly at the week limit counts as used
    [InlineData(0, 10_000_000, false, true, VisibilityReason.Used)]         // exactly at the month limit
    [InlineData(4_999_999, 9_999_999, false, false, VisibilityReason.BelowLimits)]
    [InlineData(4_999_999, 9_999_999, true, true, VisibilityReason.BelowLimitsButRunning)]
    public void Below_both_rule(long week, long month, bool running, bool inTray, VisibilityReason reason)
    {
        Assert.Equal(new ToolVisibility(inTray, true, reason), ToolVisibilityPolicy.Decide(week, month, true, running, Defaults));
    }

    [Theory]
    [InlineData(4_999_999, 20_000_000, false)]
    [InlineData(6_000_000, 9_999_999, false)]
    [InlineData(5_000_000, 10_000_000, true)]
    public void Below_either_rule(long week, long month, bool inTray)
    {
        Assert.Equal(inTray, ToolVisibilityPolicy.Decide(week, month, true, false, Either).InTray);
    }

    [Fact]
    public void Running_does_not_rescue_a_tool_when_that_option_is_off()
    {
        Assert.Equal(new ToolVisibility(false, true, VisibilityReason.BelowLimits), ToolVisibilityPolicy.Decide(0, 0, true, true, Strict));
    }

    [Fact]
    public void A_tool_without_recent_usage_is_hidden_unless_it_is_running()
    {
        Assert.Equal(new ToolVisibility(false, false, VisibilityReason.NoUsage), ToolVisibilityPolicy.Decide(0, 0, false, false, Defaults));
        Assert.Equal(new ToolVisibility(true, true, VisibilityReason.NoUsageButRunning), ToolVisibilityPolicy.Decide(0, 0, false, true, Strict));
    }

    [Fact]
    public void Decide_reads_the_windows_from_stored_usage()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Copenhagen");
        var periods = new PeriodCalculator(new FakeTimeProvider(new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero)), zone);
        var store = new UsageStore();
        var usage = new UsageAggregator(store);
        store.Add([
            new UsageRecord(Tool.Claude, "s", "m", new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero), 6_000_000, 0, 0, 0, 0, "a"), // last full week
            new UsageRecord(Tool.Codex, "s", "m", new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero), 1, 0, 0, 0, 0, "b"),         // this week only
        ]);

        Assert.Equal(VisibilityReason.Used, ToolVisibilityPolicy.Decide(Tool.Claude, false, usage, periods, Defaults).Reason);
        Assert.Equal(VisibilityReason.BelowLimits, ToolVisibilityPolicy.Decide(Tool.Codex, false, usage, periods, Defaults).Reason);
        Assert.Equal(VisibilityReason.NoUsage, ToolVisibilityPolicy.Decide(Tool.Copilot, false, usage, periods, Defaults).Reason);
    }

    [Fact]
    public void Heavy_use_this_week_or_month_keeps_a_tool_in_the_tray()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Copenhagen");
        var periods = new PeriodCalculator(new FakeTimeProvider(new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero)), zone);
        var store = new UsageStore();
        var usage = new UsageAggregator(store);
        store.Add([
            new UsageRecord(Tool.Claude, "s", "m", new DateTimeOffset(2026, 11, 18, 9, 0, 0, TimeSpan.Zero), 98_000_000, 0, 0, 0, 0, "a"), // today only
            new UsageRecord(Tool.Codex, "s", "m", new DateTimeOffset(2026, 11, 3, 9, 0, 0, TimeSpan.Zero), 11_000_000, 0, 0, 0, 0, "b"),  // this month, not this week
        ]);
        var asChosen = Defaults with { IncludeCurrentPeriod = false };

        Assert.True(Defaults.IncludeCurrentPeriod);
        Assert.Equal(VisibilityReason.Used, ToolVisibilityPolicy.Decide(Tool.Claude, false, usage, periods, Defaults).Reason);
        Assert.Equal(VisibilityReason.Used, ToolVisibilityPolicy.Decide(Tool.Codex, false, usage, periods, Defaults).Reason);
        Assert.Equal(VisibilityReason.BelowLimits, ToolVisibilityPolicy.Decide(Tool.Claude, false, usage, periods, asChosen).Reason);
    }
}
