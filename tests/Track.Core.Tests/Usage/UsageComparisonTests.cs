using Microsoft.Extensions.Time.Testing;
using Track.Core.Ingest;
using Track.Core.Parsing;
using Track.Core.Usage;

namespace Track.Core.Tests.Usage;

public class UsageComparisonTests
{
    private static readonly TimeZoneInfo Copenhagen = TimeZoneInfo.FindSystemTimeZoneById("Europe/Copenhagen");

    private readonly UsageStore _store = new();

    private static DateTimeOffset Local(int y, int mo, int d, int h, int mi = 0) =>
        new(new DateTime(y, mo, d, h, mi, 0), Copenhagen.GetUtcOffset(new DateTime(y, mo, d, h, mi, 0)));

    private UsageComparison At(DateTimeOffset now) =>
        new(new UsageAggregator(_store), new PeriodCalculator(new FakeTimeProvider(now), Copenhagen), new FakeTimeProvider(now));

    private void Add(DateTimeOffset at, long tokens, Tool tool = Tool.Claude) =>
        _store.Add([new UsageRecord(tool, "s", "m", at, tokens, 0, 0, 0, 0, Guid.NewGuid().ToString())]);

    [Fact]
    public void Today_is_compared_with_yesterday_until_the_same_time()
    {
        Add(Local(2026, 11, 17, 10), 60);
        Add(Local(2026, 11, 17, 20, 30), 40);      // yesterday before 20:53: counts
        Add(Local(2026, 11, 17, 21, 0), 999);      // yesterday after 20:53: not yet comparable
        Add(Local(2026, 11, 18, 9), 100);
        Add(Local(2026, 11, 18, 20, 10), 18, Tool.Copilot);

        var c = At(Local(2026, 11, 18, 20, 53)).For(Period.Today);

        Assert.Equal(new Comparison(118, 100, 18, "+18 % vs yesterday at this time"), c);
    }

    [Fact]
    public void The_week_is_compared_with_last_week_until_the_same_weekday_and_time()
    {
        Add(Local(2026, 11, 10, 12), 200);     // last Tuesday
        Add(Local(2026, 11, 12, 9), 5000);     // last Thursday: after "Wednesday 20:53"
        Add(Local(2026, 11, 17, 12), 186);     // this Tuesday

        var c = At(Local(2026, 11, 18, 20, 53)).For(Period.Week);

        Assert.Equal(new Comparison(186, 200, -7, "−7 % vs last week at this time"), c);
    }

    [Fact]
    public void Equal_usage_says_same()
    {
        Add(Local(2026, 10, 3, 12), 500);
        Add(Local(2026, 11, 3, 12), 500);

        Assert.Equal("same as last month at this time", At(Local(2026, 11, 18, 20)).For(Period.Month).Text);
    }

    [Theory]
    [InlineData(201, 200, 1, "+1 % vs yesterday at this time")]                 // +0.5 % rounds away from zero
    [InlineData(199, 200, -1, "−1 % vs yesterday at this time")]           // −0.5 % too
    [InlineData(0, 300, -100, "−100 % vs yesterday at this time")]
    [InlineData(5, 0, null, "new today")]
    [InlineData(0, 0, null, null)]
    public void Edge_values(long today, long yesterday, int? percent, string? text)
    {
        if (yesterday > 0)
        {
            Add(Local(2026, 11, 17, 8), yesterday);
        }

        if (today > 0)
        {
            Add(Local(2026, 11, 18, 8), today);
        }

        Assert.Equal(new Comparison(today, yesterday, percent, text), At(Local(2026, 11, 18, 12)).For(Period.Today));
    }

    [Fact]
    public void New_week_and_new_month_texts()
    {
        Add(Local(2026, 11, 18, 8), 1);

        Assert.Equal("new this week", At(Local(2026, 11, 18, 12)).For(Period.Week).Text);
        Assert.Equal("new this month", At(Local(2026, 11, 18, 12)).For(Period.Month).Text);
    }

    [Fact]
    public void Monday_at_midnight_has_nothing_to_compare_yet()
    {
        Add(Local(2026, 11, 9, 0), 777); // last Monday 00:00 hour: after "0 minutes into the week"

        Assert.Null(At(Local(2026, 11, 16, 0)).For(Period.Week).Text);
    }

    [Theory]
    [InlineData(2026, 28)] // 31 March vs a 28-day February: all of February
    [InlineData(2028, 29)] // leap year: all 29 days
    public void A_longer_month_is_compared_with_all_of_a_shorter_one(int year, int februaryDays)
    {
        Add(Local(year, 2, februaryDays, 23), 100);  // last hour of February counts
        Add(Local(year, 3, 1, 0), 50);              // March belongs to the current side

        var c = At(Local(year, 3, 31, 12)).For(Period.Month);

        Assert.Equal((50L, 100L, -50), (c.Current, c.Previous, c.Percent!.Value));
    }

    [Fact]
    public void A_clock_change_day_is_compared_by_wall_clock_time()
    {
        Add(Local(2026, 3, 28, 11, 30), 40);  // Saturday before 12:00: counts
        Add(Local(2026, 3, 28, 12, 30), 999); // Saturday after 12:00: not yet
        Add(Local(2026, 3, 29, 10), 40);      // Sunday (23-hour day)

        var c = At(Local(2026, 3, 29, 12)).For(Period.Today);

        Assert.Equal((40L, 40L, "same as yesterday at this time"), (c.Current, c.Previous, c.Text));
    }
}
