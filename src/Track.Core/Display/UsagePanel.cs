using Track.Core.Ingest;
using Track.Core.Usage;

namespace Track.Core.Display;

/// <summary>Everything a usage panel shows (the tray popover's table and the island's Usage tab).</summary>
/// <param name="Label">"Today" / "This week" / "This month", or the drilled-down bar's label, e.g. "Tue 17 Nov".</param>
/// <param name="SelectedStartUtc">The drilled-down bar, or null for the whole period.</param>
public sealed record UsagePanelContent(
    Period Period,
    string Label,
    IReadOnlyList<ToolRowViewModel> Rows,
    ToolRowViewModel? TotalRow,
    ChartViewModel? Chart,
    string? ComparisonText,
    DateTimeOffset? SelectedStartUtc);

/// <summary>
/// Builds a usage panel the same way for the popover and the island, so both always show the same numbers:
/// rows sorted by tokens (highest first), a Total row when more than one tool is listed, the stacked chart and
/// the comparison. When a bar is selected, the rows show just that hour or day.
/// </summary>
public sealed class UsagePanelBuilder(UsageStore store, UsageAggregator usage, PeriodCalculator periods, TimeProvider time)
{
    private readonly UsageSeries _series = new(store, usage, periods, time);
    private readonly UsageComparison _comparison = new(usage, periods, time);

    public static string PeriodLabel(Period period) => period switch
    {
        Period.Today => "Today",
        Period.Week => "This week",
        Period.Month => "This month",
        _ => throw new ArgumentOutOfRangeException(nameof(period), period, "Unknown period."),
    };

    /// <param name="listed">Tools shown in the table (the visibility rule), in Claude, Codex, Copilot order.</param>
    /// <param name="selectedStartUtc">A bar the user clicked; ignored when it is no longer a past or current bar of this period (rollover).</param>
    public UsagePanelContent Build(Period period, IReadOnlyList<Tool> listed, DateTimeOffset? selectedStartUtc, bool showChart)
    {
        // The hourly/daily split is only worked out when something needs it (a visible chart or a drilled bar),
        // so the 5-second refresh stays cheap while the chart is hidden or the popover is closed.
        var buckets = showChart || selectedStartUtc is not null ? _series.For(period) : [];
        var selected = buckets.FirstOrDefault(b => b.StartUtc == selectedStartUtc && !b.IsFuture);
        var range = periods.Range(period);

        var totals = listed.Select(t => (Tool: t, Totals: selected?.PerTool[t] ?? usage.Totals(t, range))).ToList();
        var largest = Math.Max(1, totals.Select(x => x.Totals.Tokens.Total).DefaultIfEmpty(0).Max());

        // Highest usage first (user review, 2026-11-18); equal usage keeps the Claude, Codex, Copilot order.
        var rows = totals
            .OrderByDescending(x => x.Totals.Tokens.Total)
            .Select(x => Row(x.Tool, ToolNames.Display(x.Tool), x.Totals, (double)x.Totals.Tokens.Total / largest))
            .ToList();
        var total = listed.Count > 1 ? Row(null, "Total", totals.Aggregate(UsageTotals.Empty, (sum, x) => sum + x.Totals), 0) : null;

        return new UsagePanelContent(
            period,
            selected?.Label ?? PeriodLabel(period),
            rows,
            total,
            showChart ? ChartViewModel.Build(period, buckets, selected?.StartUtc, listed.ToHashSet()) : null,
            showChart ? _comparison.For(period).Text : null,
            selected?.StartUtc);
    }

    /// <summary>The selection after a click on bar <paramref name="index"/>: the same bar again or a future bar clears it.</summary>
    public static DateTimeOffset? Toggle(ChartViewModel chart, int index, DateTimeOffset? current)
    {
        var bar = chart.Bars[index];
        return bar.Future || bar.StartUtc == current ? null : bar.StartUtc;
    }

    private static ToolRowViewModel Row(Tool? tool, string name, UsageTotals totals, double bar) =>
        new(tool, name, tool?.ToString().ToLowerInvariant() ?? "", Format.Tokens(totals.Tokens.Total), Format.SubLine(totals), bar, totals);
}
