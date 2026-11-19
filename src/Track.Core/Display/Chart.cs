using Track.Core.Usage;
using Track.Core.Providers;

namespace Track.Core.Display;

/// <summary>One tool's part of a bar, as fractions of the chart height from the bottom (0) to the top (1).</summary>
public sealed record ChartSegment(Tool Tool, double From, double To);

/// <summary>One bar: an hour (Today) or a day (Week, Month).</summary>
/// <param name="Tooltip">Lines shown on hover, joined with new lines.</param>
public sealed record ChartBar(
    int Index,
    DateTimeOffset StartUtc,
    IReadOnlyList<ChartSegment> Segments,
    bool Current,
    bool Future,
    bool Selected,
    string Tooltip);

/// <summary>An axis label, placed at <paramref name="Position"/> in bar units (bar i spans i..i+1).</summary>
public sealed record AxisLabel(double Position, string Text);

/// <summary>A small stacked usage chart (round 5): tools stacked by size, largest at the bottom.</summary>
public sealed record ChartViewModel(string Title, IReadOnlyList<ChartBar> Bars, string TopText, IReadOnlyList<AxisLabel> Axis)
{
    public const double MinimumBarPixels = 1.5;

    private static readonly Tool[] Order = ProviderCatalog.Order.ToArray();

    public static ChartViewModel Build(Period period, IReadOnlyList<SeriesBucket> buckets, DateTimeOffset? selectedStartUtc)
    {
        var top = NiceMax(buckets.Select(b => b.Total.Tokens.Total).DefaultIfEmpty(0).Max());
        var bars = buckets.Select(b => new ChartBar(
            b.Index,
            b.StartUtc,
            Segments(b, top),
            b.IsCurrent,
            b.IsFuture,
            b.StartUtc == selectedStartUtc,
            Tooltip(b))).ToList();

        return new ChartViewModel(TitleFor(period), bars, top == 0 ? "no usage yet" : "top " + Format.Tokens(top), AxisLabelsFor(period, buckets));
    }

    /// <summary>The chart's title; also shown on its own while the chart is minimized.</summary>
    public static string TitleFor(Period period) => period switch
    {
        Period.Today => "Today by hour",
        Period.Week => "This week by day",
        _ => "This month by day",
    };

    /// <summary>The smallest of 1, 2, 2.5, 5 × 10ⁿ at or above <paramref name="max"/> (0 stays 0).</summary>
    public static long NiceMax(long max)
    {
        if (max <= 0)
        {
            return 0;
        }

        var power = 1L;
        while (power * 10 <= max)
        {
            power *= 10;
        }

        // 2.5 × 1 rounds down to 2, which is fine: 2 is also a valid step and only chosen when it fits.
        return new[] { power, 2 * power, 5 * power / 2, 5 * power, 10 * power }.Where(c => c >= max).Min();
    }

    /// <summary>Pixel heights of a bar's segments: true to scale, but never thinner than <see cref="MinimumBarPixels"/>, so a tiny use stays visible.</summary>
    public static IReadOnlyList<double> SegmentPixels(ChartBar bar, double chartPixels) =>
        bar.Segments.Select(s => Math.Max(MinimumBarPixels, (s.To - s.From) * chartPixels)).ToList();

    private static IReadOnlyList<ChartSegment> Segments(SeriesBucket bucket, long top)
    {
        var tools = Order.Where(t => bucket.PerTool[t].Tokens.Total > 0).OrderByDescending(t => bucket.PerTool[t].Tokens.Total).ToList();
        var segments = new List<ChartSegment>(tools.Count);
        var from = 0.0;
        for (var i = 0; i < tools.Count; i++)
        {
            var to = from + ((double)bucket.PerTool[tools[i]].Tokens.Total / top);
            segments.Add(new ChartSegment(tools[i], from, to));
            from = to;
        }

        return segments;
    }

    private static string Tooltip(SeriesBucket b)
    {
        if (b.IsFuture)
        {
            return $"{b.Label}\nnot yet";
        }

        if (b.Total.Tokens.Total == 0)
        {
            return $"{b.Label}\nno usage";
        }

        var lines = Order
            .Where(t => b.PerTool[t].Tokens.Total > 0)
            .OrderByDescending(t => b.PerTool[t].Tokens.Total)
            .Select(t => $"● {ToolNames.Display(t)} {Format.Tokens(b.PerTool[t].Tokens.Total)}");
        return string.Join("\n", lines.Prepend($"{b.Label} · {Format.Tokens(b.Total.Tokens.Total)} tokens · {Format.Cost(b.Total)}"));
    }

    private static IReadOnlyList<AxisLabel> AxisLabelsFor(Period period, IReadOnlyList<SeriesBucket> buckets)
    {
        string[]? shown = period switch
        {
            Period.Today => ["00", "06", "12", "18"],
            Period.Week => null, // every day: M T W T F S S
            _ => ["1", "8", "15", "22", "29"],
        };

        var labels = buckets
            .Where(b => shown is null || shown.Contains(b.ShortLabel))
            .Select(b => new AxisLabel(b.Index + 0.5, b.ShortLabel))
            .ToList();
        if (period == Period.Today)
        {
            labels.Add(new AxisLabel(buckets.Count, "24"));
        }

        return labels;
    }
}
