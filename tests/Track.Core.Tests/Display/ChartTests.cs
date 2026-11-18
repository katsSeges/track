using Microsoft.Extensions.Time.Testing;
using Track.Core.Display;
using Track.Core.Ingest;
using Track.Core.Parsing;
using Track.Core.Usage;

namespace Track.Core.Tests.Display;

public class ChartTests
{
    private static readonly TimeZoneInfo Copenhagen = TimeZoneInfo.FindSystemTimeZoneById("Europe/Copenhagen");

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 5)]
    [InlineData(9, 10)]
    [InlineData(10, 10)]
    [InlineData(11, 20)]
    [InlineData(21, 25)]
    [InlineData(26, 50)]
    [InlineData(51, 100)]
    [InlineData(999_499, 1_000_000)]
    [InlineData(21_400_000, 25_000_000)]
    [InlineData(160_000_000, 200_000_000)]
    public void Top_value_is_the_next_1_2_2_5_or_5_step(long max, long top)
    {
        Assert.Equal(top, ChartViewModel.NiceMax(max));
    }

    [Fact]
    public void Tiny_parts_stay_visible_and_big_parts_are_true_to_scale()
    {
        var bar = new ChartBar(0, DateTimeOffset.UnixEpoch, [new ChartSegment(Tool.Claude, 0, 0.5), new ChartSegment(Tool.Codex, 0.5, 0.5001)], false, false, false, "");

        Assert.Equal([32.0, ChartViewModel.MinimumBarPixels], ChartViewModel.SegmentPixels(bar, 64));
    }

    [Fact]
    public void An_empty_period_says_no_usage_yet_and_has_no_segments()
    {
        var now = new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);
        var time = new FakeTimeProvider(now);
        var store = new UsageStore();
        var series = new UsageSeries(store, new UsageAggregator(store), new PeriodCalculator(time, Copenhagen), time);

        var chart = ChartViewModel.Build(Period.Month, series.For(Period.Month), null);

        Assert.Equal(("This month by day", "no usage yet"), (chart.Title, chart.TopText));
        Assert.All(chart.Bars, b => Assert.Empty(b.Segments));
    }

    [Fact]
    public void Axis_labels_on_a_25_hour_day_end_at_24_after_the_last_bar()
    {
        var local = new DateTime(2026, 10, 25, 12, 0, 0);
        var time = new FakeTimeProvider(new DateTimeOffset(local, Copenhagen.GetUtcOffset(local)));
        var store = new UsageStore();
        var series = new UsageSeries(store, new UsageAggregator(store), new PeriodCalculator(time, Copenhagen), time);

        var chart = ChartViewModel.Build(Period.Today, series.For(Period.Today), null);

        Assert.Equal(25, chart.Bars.Count);
        Assert.Equal([new AxisLabel(0.5, "00"), new AxisLabel(7.5, "06"), new AxisLabel(13.5, "12"), new AxisLabel(19.5, "18"), new AxisLabel(25, "24")], chart.Axis);
    }

    [Fact]
    public void Toggle_selects_a_bar_clears_it_on_a_second_click_and_ignores_future_bars()
    {
        var t0 = DateTimeOffset.UnixEpoch;
        var chart = new ChartViewModel("t", [
            new ChartBar(0, t0, [], false, false, false, ""),
            new ChartBar(1, t0.AddHours(1), [], false, true, false, ""),
        ], "top 1", []);

        Assert.Equal(t0, UsagePanelBuilder.Toggle(chart, 0, null));
        Assert.Null(UsagePanelBuilder.Toggle(chart, 0, t0));
        Assert.Null(UsagePanelBuilder.Toggle(chart, 1, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => UsagePanelBuilder.PeriodLabel((Period)9));
    }
}
