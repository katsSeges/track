using System.Globalization;

namespace Track.Core.Usage;

/// <summary>Usage so far in a period next to the previous period up to the same point.</summary>
/// <param name="Percent">Rounded change in percent, or null when the previous period had no usage.</param>
/// <param name="Text">What the chart shows, e.g. <c>+18 % vs last week at this time</c>; null when both are 0.</param>
public sealed record Comparison(long Current, long Previous, int? Percent, string? Text);

/// <summary>
/// Compares a period so far with the previous one up to the same local wall-clock point: Today with
/// yesterday until this time of day, the week with last week until the same weekday and time, the month
/// with last month until the same day and time (cut at last month's end when it is shorter). Both sides
/// count whole stored hours that start before their cut-off, so they are measured the same way.
/// </summary>
public sealed class UsageComparison(UsageAggregator usage, PeriodCalculator periods, TimeProvider time)
{
    public Comparison For(Period period)
    {
        var now = time.GetUtcNow();
        var range = periods.Range(period);
        var startLocal = periods.ToLocal(range.StartUtc);
        var elapsed = periods.ToLocal(now) - startLocal; // wall-clock time since the period began

        var previousStartLocal = period switch
        {
            Period.Today => startLocal.AddDays(-1),
            Period.Week => startLocal.AddDays(-7),
            _ => startLocal.AddMonths(-1),
        };
        var previousCutLocal = previousStartLocal + elapsed;
        if (previousCutLocal > startLocal)
        {
            previousCutLocal = startLocal; // e.g. 31 March compared with a 28-day February: all of February
        }

        var current = Tokens(range.StartUtc, now);
        var previous = Tokens(periods.ToUtc(previousStartLocal), periods.ToUtc(previousCutLocal));
        var percent = previous == 0 ? (int?)null : (int)Math.Round((current - previous) * 100m / previous, MidpointRounding.AwayFromZero);
        return new Comparison(current, previous, percent, Text(period, current, previous, percent));
    }

    private long Tokens(DateTimeOffset fromUtc, DateTimeOffset toUtc) =>
        usage.Totals(null, new TimeRange(fromUtc, toUtc)).Tokens.Total;

    private static string? Text(Period period, long current, long previous, int? percent)
    {
        var (before, now) = period switch
        {
            Period.Today => ("yesterday", "today"),
            Period.Week => ("last week", "this week"),
            _ => ("last month", "this month"),
        };

        return (current, previous, percent) switch
        {
            (0, 0, _) => null,
            (_, 0, _) => $"new {now}",
            (_, _, 0) => $"same as {before} at this time",
            (_, _, > 0) => $"+{percent.Value.ToString(CultureInfo.InvariantCulture)} % vs {before} at this time",
            _ => $"−{Math.Abs(percent!.Value).ToString(CultureInfo.InvariantCulture)} % vs {before} at this time",
        };
    }
}
