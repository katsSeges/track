using Microsoft.Extensions.Time.Testing;
using Track.Core.Usage;

namespace Track.Core.Tests.Usage;

public class PeriodCalculatorTests
{
    private static readonly TimeZoneInfo Copenhagen = TimeZoneInfo.FindSystemTimeZoneById("Europe/Copenhagen");

    private static PeriodCalculator At(int year, int month, int day, int hour, int minute = 0, TimeZoneInfo? zone = null)
    {
        zone ??= Copenhagen;
        var local = new DateTime(year, month, day, hour, minute, 0);
        return new PeriodCalculator(new FakeTimeProvider(new DateTimeOffset(local, zone.GetUtcOffset(local))), zone);
    }

    private static DateTimeOffset Utc(int year, int month, int day, int hour) => new(year, month, day, hour, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Today_week_and_month_start_at_local_midnight_monday_and_the_first()
    {
        var p = At(2026, 11, 18, 19); // Wednesday, Copenhagen is UTC+1 in November

        Assert.Equal(new TimeRange(Utc(2026, 11, 17, 23), Utc(2026, 11, 18, 23)), p.Range(Period.Today));
        Assert.Equal(new TimeRange(Utc(2026, 11, 15, 23), Utc(2026, 11, 22, 23)), p.Range(Period.Week));
        Assert.Equal(new TimeRange(Utc(2026, 10, 31, 23), Utc(2026, 11, 30, 23)), p.Range(Period.Month));
    }

    [Fact]
    public void Sunday_2359_is_still_last_week_and_monday_0000_starts_a_new_one()
    {
        Assert.Equal(Utc(2026, 11, 8, 23), At(2026, 11, 15, 23, 59).Range(Period.Week).StartUtc);
        Assert.Equal(Utc(2026, 11, 15, 23), At(2026, 11, 16, 0).Range(Period.Week).StartUtc);
    }

    [Fact]
    public void A_week_starting_on_sunday_is_handled()
    {
        // Sunday: DayOfWeek 0 → Monday six days earlier.
        Assert.Equal(Utc(2026, 11, 8, 23), At(2026, 11, 15, 12).Range(Period.Week).StartUtc);
    }

    [Fact]
    public void The_month_rolls_over_at_local_midnight_on_the_first()
    {
        Assert.Equal(Utc(2026, 10, 31, 23), At(2026, 11, 30, 23, 59).Range(Period.Month).StartUtc);
        Assert.Equal(Utc(2026, 11, 30, 23), At(2026, 12, 1, 0).Range(Period.Month).StartUtc);
    }

    [Fact]
    public void Days_with_a_clock_change_are_23_or_25_hours_long()
    {
        var spring = At(2026, 3, 29, 12).Range(Period.Today); // clocks go forward 02:00 → 03:00
        var autumn = At(2026, 10, 25, 12).Range(Period.Today); // clocks go back 03:00 → 02:00

        Assert.Equal(TimeSpan.FromHours(23), spring.EndUtc - spring.StartUtc);
        Assert.Equal(TimeSpan.FromHours(25), autumn.EndUtc - autumn.StartUtc);
    }

    [Fact]
    public void A_midnight_skipped_or_repeated_by_a_clock_change_is_handled()
    {
        // A zone (UTC-3) whose clocks jump 00:00 → 01:00 on 4 Oct and fall back 01:00 → 00:00 on 15 Feb.
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            DateTime.MinValue.Date,
            DateTime.MaxValue.Date,
            TimeSpan.FromHours(1),
            TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 0, 0, 0), 10, 4),
            TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 1, 0, 0), 2, 15));
        var zone = TimeZoneInfo.CreateCustomTimeZone("Test/Midnight", TimeSpan.FromHours(-3), "Midnight", "Std", "Dst", [rule]);
        var p = At(2026, 6, 1, 12, zone: zone);

        // 00:00 on 4 Oct does not exist: the day starts at 01:00 local (UTC-2) = 03:00 UTC.
        Assert.Equal(Utc(2026, 10, 4, 3), p.ToUtc(new DateTime(2026, 10, 4, 0, 0, 0)));
        // 00:00 on 15 Feb happens twice: the first one (UTC-2) = 02:00 UTC.
        Assert.Equal(Utc(2026, 2, 15, 2), p.ToUtc(new DateTime(2026, 2, 15, 0, 0, 0)));
    }

    [Fact]
    public void Tray_rule_windows()
    {
        var p = At(2026, 11, 18, 19);

        Assert.Equal(new TimeRange(Utc(2026, 11, 8, 23), Utc(2026, 11, 15, 23)), p.Range(WeekWindow.LastFullWeek));
        Assert.Equal(p.Range(Period.Week), p.Range(WeekWindow.ThisWeek));
        Assert.Equal(new TimeRange(Utc(2026, 11, 11, 18), DateTimeOffset.MaxValue), p.Range(WeekWindow.Last7Days));
        Assert.Equal(new TimeRange(Utc(2026, 9, 30, 22), Utc(2026, 10, 31, 23)), p.Range(MonthWindow.LastFullMonth));
        Assert.Equal(p.Range(Period.Month), p.Range(MonthWindow.ThisMonth));
        Assert.Equal(new TimeRange(Utc(2026, 10, 19, 18), DateTimeOffset.MaxValue), p.Range(MonthWindow.Last30Days));
        Assert.Equal(new TimeRange(Utc(2026, 8, 20, 18), DateTimeOffset.MaxValue), p.LastDays(90));
    }

    [Fact]
    public void Unknown_choices_are_rejected()
    {
        var p = At(2026, 11, 18, 19);

        Assert.Throws<ArgumentOutOfRangeException>(() => p.Range((Period)9));
        Assert.Throws<ArgumentOutOfRangeException>(() => p.Range((WeekWindow)9));
        Assert.Throws<ArgumentOutOfRangeException>(() => p.Range((MonthWindow)9));
    }

    [Fact]
    public void Ranges_include_their_start_and_exclude_their_end()
    {
        var r = new TimeRange(Utc(2026, 1, 1, 0), Utc(2026, 1, 2, 0));

        Assert.True(r.Contains(Utc(2026, 1, 1, 0)));
        Assert.False(r.Contains(Utc(2026, 1, 2, 0)));
        Assert.False(r.Contains(Utc(2025, 12, 31, 23)));
    }
}
