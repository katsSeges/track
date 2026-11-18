using Microsoft.Extensions.Time.Testing;
using Track.Core.Ingest;
using Track.Core.Parsing;
using Track.Core.Pricing;
using Track.Core.Usage;

namespace Track.Core.Tests.Usage;

public class UsageSeriesTests
{
    private static readonly TimeZoneInfo Copenhagen = TimeZoneInfo.FindSystemTimeZoneById("Europe/Copenhagen");
    private static readonly TimeZoneInfo Kolkata = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata"); // UTC+05:30

    // $1 per input token, $10 per output token: prices are easy to check by hand.
    private static readonly PriceTable Prices = PriceTable.Parse("""{"m":{"input_cost_per_token":1,"output_cost_per_token":10}}""")!;

    private readonly UsageStore _store = new();

    private (UsageSeries Series, UsageAggregator Usage, PeriodCalculator Periods, FakeTimeProvider Time) Create(DateTimeOffset now, TimeZoneInfo? zone = null)
    {
        var time = new FakeTimeProvider(now);
        var periods = new PeriodCalculator(time, zone ?? Copenhagen);
        var usage = new UsageAggregator(_store) { Prices = Prices };
        return (new UsageSeries(_store, usage, periods, time), usage, periods, time);
    }

    private void Add(Tool tool, DateTimeOffset at, long input, long output = 0, string model = "m") =>
        _store.Add([new UsageRecord(tool, "s", model, at, input, 0, 0, output, 0, Guid.NewGuid().ToString())]);

    private static DateTimeOffset Local(int y, int mo, int d, int h, int mi = 0) =>
        new DateTimeOffset(new DateTime(y, mo, d, h, mi, 0), Copenhagen.GetUtcOffset(new DateTime(y, mo, d, h, mi, 0)));

    [Fact]
    public void Today_has_24_local_hours_with_labels_and_now_and_future_flags()
    {
        var (series, _, _, _) = Create(Local(2026, 11, 18, 20, 53));

        var hours = series.For(Period.Today);

        Assert.Equal(24, hours.Count);
        Assert.Equal(("00:00–01:00", "00"), (hours[0].Label, hours[0].ShortLabel));
        Assert.Equal(("23:00–24:00", "23"), (hours[23].Label, hours[23].ShortLabel));
        Assert.Equal(Local(2026, 11, 18, 0), hours[0].StartUtc);
        Assert.Equal(Local(2026, 11, 19, 0), hours[23].EndUtc);
        Assert.Equal([20], hours.Where(h => h.IsCurrent).Select(h => h.Index));
        Assert.Equal(Enumerable.Range(21, 3), hours.Where(h => h.IsFuture).Select(h => h.Index));
        Assert.Equal(Enumerable.Range(0, 24), hours.Select(h => h.Index));
    }

    [Fact]
    public void Usage_lands_in_the_hour_and_day_it_started_in_with_prices()
    {
        var (series, _, _, _) = Create(Local(2026, 11, 18, 20, 53));
        Add(Tool.Claude, Local(2026, 11, 18, 14, 59), 100, 5);  // 14:00–15:00, $150
        Add(Tool.Claude, Local(2026, 11, 18, 15, 0), 7);        // 15:00–16:00
        Add(Tool.Copilot, Local(2026, 11, 18, 14, 10), 30);     // 14:00–15:00
        Add(Tool.Codex, Local(2026, 11, 17, 23, 59), 1000);     // yesterday: not today

        var hours = series.For(Period.Today);

        Assert.Equal(105, hours[14].PerTool[Tool.Claude].Tokens.Total);
        Assert.Equal(150m, hours[14].PerTool[Tool.Claude].Cost);
        Assert.Equal(30, hours[14].PerTool[Tool.Copilot].Tokens.Total);
        Assert.Equal(135, hours[14].Total.Tokens.Total);
        Assert.Equal(180m, hours[14].Total.Cost);
        Assert.Equal(7, hours[15].Total.Tokens.Total);
        Assert.Equal(UsageTotals.Empty, hours[14].PerTool[Tool.Codex]);
        Assert.Equal(142, hours.Sum(h => h.Total.Tokens.Total));
    }

    [Fact]
    public void Week_is_monday_to_sunday_and_month_is_every_day()
    {
        var (series, _, _, _) = Create(Local(2026, 11, 18, 20, 53)); // Wednesday

        var week = series.For(Period.Week);
        var month = series.For(Period.Month);

        Assert.Equal(["M", "T", "W", "T", "F", "S", "S"], week.Select(d => d.ShortLabel));
        Assert.Equal(("Mon 16 Nov", "Sun 22 Nov"), (week[0].Label, week[6].Label));
        Assert.Equal([2], week.Where(d => d.IsCurrent).Select(d => d.Index));
        Assert.Equal([3, 4, 5, 6], week.Where(d => d.IsFuture).Select(d => d.Index));
        Assert.Equal(30, month.Count);
        Assert.Equal(("Sun 1 Nov", "1", "Mon 30 Nov", "30"), (month[0].Label, month[0].ShortLabel, month[29].Label, month[29].ShortLabel));
        Assert.Equal(Local(2026, 11, 1, 0), month[0].StartUtc);
        Assert.Equal(Local(2026, 12, 1, 0), month[29].EndUtc);
    }

    [Theory]
    [InlineData(2026, 2, 28)]
    [InlineData(2028, 2, 29)]
    [InlineData(2026, 4, 30)]
    [InlineData(2026, 1, 31)]
    public void Months_have_their_real_number_of_days(int year, int month, int days)
    {
        var (series, _, _, _) = Create(Local(year, month, 10, 12));

        Assert.Equal(days, series.For(Period.Month).Count);
    }

    [Fact]
    public void Clock_change_days_have_23_or_25_hours_with_honest_labels()
    {
        var (spring, _, _, _) = Create(Local(2026, 3, 29, 12));
        var (autumn, _, _, _) = Create(Local(2026, 10, 25, 12));

        var s = spring.For(Period.Today);
        var a = autumn.For(Period.Today);

        Assert.Equal(23, s.Count);
        Assert.Equal(["01:00–02:00", "03:00–04:00"], s.Skip(1).Take(2).Select(h => h.Label)); // 02:00 does not exist
        Assert.Equal(25, a.Count);
        Assert.Equal(["02:00–03:00", "02:00–03:00 (2nd)", "03:00–04:00"], a.Skip(2).Take(3).Select(h => h.Label));
        Assert.All(a, h => Assert.Equal(TimeSpan.FromHours(1), h.EndUtc - h.StartUtc));
    }

    [Fact]
    public void A_week_across_the_autumn_clock_change_still_has_seven_days()
    {
        var (series, _, _, _) = Create(Local(2026, 10, 28, 12)); // week of Mon 26 Oct; the change was Sun 25 Oct
        var (before, _, _, _) = Create(Local(2026, 10, 21, 12));   // week of Mon 19 Oct contains Sun 25 Oct

        Assert.Equal(7, series.For(Period.Week).Count);
        var week = before.For(Period.Week);
        Assert.Equal(7, week.Count);
        Assert.Equal(TimeSpan.FromHours(25), week[6].EndUtc - week[6].StartUtc);
    }

    [Fact]
    public void Each_stored_hour_is_counted_once_so_buckets_add_up_to_the_period_total()
    {
        var (series, usage, periods, _) = Create(Local(2026, 11, 18, 20, 53));
        var tools = Enum.GetValues<Tool>();
        for (var h = 0; h < 24 * 25; h += 7)
        {
            Add(tools[h % 3], Local(2026, 11, 1, 0).AddHours(h), 10 + h, h % 4);
        }

        Add(Tool.Claude, Local(2026, 10, 31, 23, 59), 999_999); // the month before
        Add(Tool.Claude, Local(2026, 12, 1, 0), 999_999);        // the month after

        foreach (var period in Enum.GetValues<Period>())
        {
            var buckets = series.For(period);
            var range = periods.Range(period);
            Assert.Equal((range.StartUtc, range.EndUtc), (buckets[0].StartUtc, buckets[^1].EndUtc));
            Assert.All(buckets.Skip(1), (b, i) => Assert.Equal(buckets[i].EndUtc, b.StartUtc)); // no gaps, no overlaps
            foreach (var tool in tools)
            {
                var expected = usage.Totals(tool, range);
                var summed = buckets.Select(b => b.PerTool[tool]).Aggregate(UsageTotals.Empty, (a, b) => a + b);
                Assert.Equal(expected, summed);
            }

            Assert.Equal(usage.Totals(null, range), buckets.Select(b => b.Total).Aggregate(UsageTotals.Empty, (a, b) => a + b));
        }
    }

    [Fact]
    public void Current_and_future_flags_switch_exactly_at_midnight()
    {
        var (series, _, _, time) = Create(Local(2026, 11, 18, 23, 59).AddSeconds(59));
        Assert.True(series.For(Period.Today)[23].IsCurrent);
        Assert.Equal("Wed 18 Nov", series.For(Period.Week).Single(d => d.IsCurrent).Label);

        time.Advance(TimeSpan.FromSeconds(1)); // 00:00:00 on Thursday 19 Nov

        var today = series.For(Period.Today);
        Assert.Equal(Local(2026, 11, 19, 0), today[0].StartUtc);
        Assert.True(today[0].IsCurrent);
        Assert.All(today.Skip(1), h => Assert.True(h.IsFuture));
        Assert.Equal("Thu 19 Nov", series.For(Period.Week).Single(d => d.IsCurrent).Label);
    }

    [Fact]
    public void Half_hour_time_zones_count_each_utc_hour_by_its_start()
    {
        var now = new DateTimeOffset(2026, 11, 18, 20, 0, 0, TimeSpan.FromHours(5.5));
        var (series, _, _, _) = Create(now, Kolkata);
        // Local 00:00 IST = 18:30 UTC the day before. The UTC hour 19:00 starts inside local 00:30–01:30? No:
        // buckets run 18:30–19:30 UTC (00:00–01:00 local), so the UTC hour starting 19:00 belongs to the first one.
        Add(Tool.Claude, new DateTimeOffset(2026, 11, 17, 19, 0, 0, TimeSpan.Zero), 5);
        Add(Tool.Claude, new DateTimeOffset(2026, 11, 17, 18, 0, 0, TimeSpan.Zero), 9); // starts before local midnight: yesterday

        var hours = series.For(Period.Today);

        Assert.Equal(("00:00–01:00", new DateTimeOffset(2026, 11, 17, 18, 30, 0, TimeSpan.Zero)), (hours[0].Label, hours[0].StartUtc));
        Assert.Equal(5, hours[0].Total.Tokens.Total);
        Assert.Equal(5, hours.Sum(h => h.Total.Tokens.Total));
    }
}
