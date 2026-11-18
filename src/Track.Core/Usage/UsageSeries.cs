using System.Globalization;
using Track.Core.Ingest;

namespace Track.Core.Usage;

/// <summary>One bar of a usage chart: a local hour (Today) or a local day (Week, Month).</summary>
/// <param name="Label">Full label for tooltips and the drilled-down table, e.g. <c>14:00–15:00</c> or <c>Tue 17 Nov</c>.</param>
/// <param name="ShortLabel">Axis label, e.g. <c>14</c>, <c>T</c> or <c>17</c>.</param>
public sealed record SeriesBucket(
    int Index,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    string Label,
    string ShortLabel,
    bool IsFuture,
    bool IsCurrent,
    IReadOnlyDictionary<Tool, UsageTotals> PerTool,
    UsageTotals Total);

/// <summary>
/// Splits a period into chart buckets in the user's local time: Today into local hours (23 or 25 on
/// clock-change days), Week into Monday–Sunday, Month into every day of the month. Each stored UTC hour
/// is counted in exactly one bucket, the one its start falls in, so the buckets always add up to the
/// period's total.
/// </summary>
public sealed class UsageSeries(UsageStore store, UsageAggregator usage, PeriodCalculator periods, TimeProvider time)
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public IReadOnlyList<SeriesBucket> For(Period period)
    {
        var spans = period == Period.Today ? Hours(periods.Range(period)) : Days(periods.Range(period), period);
        var perTool = spans.Select(_ => Enum.GetValues<Tool>().ToDictionary(t => t, _ => UsageTotals.Empty)).ToList();

        foreach (var (key, tokens) in store.Buckets)
        {
            var index = spans.FindIndex(s => key.HourUtc >= s.Start && key.HourUtc < s.End);
            if (index >= 0)
            {
                perTool[index][key.Tool] += usage.Price(key.Model, tokens);
            }
        }

        var now = time.GetUtcNow();
        return spans.Select((s, i) => new SeriesBucket(
                i,
                s.Start,
                s.End,
                s.Label,
                s.Short,
                IsFuture: s.Start > now,
                IsCurrent: s.Start <= now && now < s.End,
                perTool[i],
                perTool[i].Values.Aggregate(UsageTotals.Empty, (a, b) => a + b)))
            .ToList();
    }

    /// <summary>Consecutive UTC hours from the start of the local day; a repeated local hour gets "(2nd)".</summary>
    private List<Span> Hours(TimeRange day)
    {
        var spans = new List<Span>();
        var seen = new HashSet<int>();
        for (var start = day.StartUtc; start < day.EndUtc; start = start.AddHours(1))
        {
            var end = start.AddHours(1) < day.EndUtc ? start.AddHours(1) : day.EndUtc;
            // The end label is the start plus the bucket's length, so the repeated autumn hour reads
            // "02:00–03:00" twice (the second with "(2nd)") instead of "02:00–02:00".
            var from = periods.ToLocal(start);
            var to = from.Add(end - start);
            var repeat = seen.Add(from.Hour) ? "" : " (2nd)";
            var toText = to.TimeOfDay == TimeSpan.Zero ? "24:00" : to.ToString("HH:mm", Invariant); // only the day's last hour ends at midnight
            spans.Add(new Span(start, end, $"{from.ToString("HH:mm", Invariant)}–{toText}{repeat}", from.ToString("HH", Invariant)));
        }

        return spans;
    }

    private List<Span> Days(TimeRange range, Period period)
    {
        var spans = new List<Span>();
        for (var local = periods.ToLocal(range.StartUtc).Date; periods.ToUtc(local) < range.EndUtc; local = local.AddDays(1))
        {
            spans.Add(new Span(
                periods.ToUtc(local),
                periods.ToUtc(local.AddDays(1)),
                local.ToString("ddd d MMM", Invariant),
                period == Period.Week ? local.ToString("ddd", Invariant)[..1] : local.Day.ToString(Invariant)));
        }

        return spans;
    }

    private sealed record Span(DateTimeOffset Start, DateTimeOffset End, string Label, string Short);
}
