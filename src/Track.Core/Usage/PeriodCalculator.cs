namespace Track.Core.Usage;

/// <summary>A half-open span of time [<see cref="StartUtc"/>, <see cref="EndUtc"/>), in UTC.</summary>
public readonly record struct TimeRange(DateTimeOffset StartUtc, DateTimeOffset EndUtc)
{
    public bool Contains(DateTimeOffset instant) => instant >= StartUtc && instant < EndUtc;
}

/// <summary>The tabs in the popover.</summary>
public enum Period
{
    Today,
    Week,
    Month,
}

/// <summary>What "last week" means for the tray rule.</summary>
public enum WeekWindow
{
    LastFullWeek,
    Last7Days,
    ThisWeek,
}

/// <summary>What "last month" means for the tray rule.</summary>
public enum MonthWindow
{
    LastFullMonth,
    Last30Days,
    ThisMonth,
}

/// <summary>
/// Calendar periods in the user's time zone, returned as UTC ranges. Days start at local 00:00,
/// weeks on Monday 00:00, months on the 1st at 00:00. Daylight-saving changes are respected:
/// a day can be 23 or 25 hours long, and a midnight that does not exist starts at the first valid moment.
/// </summary>
public sealed class PeriodCalculator(TimeProvider time, TimeZoneInfo zone)
{
    public TimeRange Range(Period period)
    {
        var today = LocalToday();
        return period switch
        {
            Period.Today => Between(today, today.AddDays(1)),
            Period.Week => Between(Monday(today), Monday(today).AddDays(7)),
            Period.Month => Between(FirstOfMonth(today), FirstOfMonth(today).AddMonths(1)),
            _ => throw new ArgumentOutOfRangeException(nameof(period), period, "Unknown period."),
        };
    }

    public TimeRange Range(WeekWindow window)
    {
        var monday = Monday(LocalToday());
        return window switch
        {
            WeekWindow.LastFullWeek => Between(monday.AddDays(-7), monday),
            WeekWindow.Last7Days => LastDays(7),
            WeekWindow.ThisWeek => Range(Period.Week),
            _ => throw new ArgumentOutOfRangeException(nameof(window), window, "Unknown window."),
        };
    }

    public TimeRange Range(MonthWindow window)
    {
        var first = FirstOfMonth(LocalToday());
        return window switch
        {
            MonthWindow.LastFullMonth => Between(first.AddMonths(-1), first),
            MonthWindow.Last30Days => LastDays(30),
            MonthWindow.ThisMonth => Range(Period.Month),
            _ => throw new ArgumentOutOfRangeException(nameof(window), window, "Unknown window."),
        };
    }

    /// <summary>The last <paramref name="days"/> × 24 hours up to now (and anything later).</summary>
    public TimeRange LastDays(int days) => new(time.GetUtcNow().ToUniversalTime().AddDays(-days), DateTimeOffset.MaxValue);

    /// <summary>The UTC instant of a local wall-clock time in the user's zone.</summary>
    public DateTimeOffset ToUtc(DateTime local)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local))
        {
            local = local.AddHours(1); // skipped by a clock change: use the first moment that exists
        }

        var offset = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Max() : zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }

    /// <summary>The local wall-clock time of a UTC instant.</summary>
    public DateTime ToLocal(DateTimeOffset utc) => TimeZoneInfo.ConvertTime(utc, zone).DateTime;

    private DateTime LocalToday() => TimeZoneInfo.ConvertTime(time.GetUtcNow(), zone).Date;

    private static DateTime Monday(DateTime day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    private static DateTime FirstOfMonth(DateTime day) => new(day.Year, day.Month, 1);

    private TimeRange Between(DateTime localStart, DateTime localEnd) => new(ToUtc(localStart), ToUtc(localEnd));
}
