using System.Globalization;
using Track.Core.Usage;

namespace Track.Core.Display;

/// <summary>
/// How numbers read in the popover, tooltip and island. Always culture-invariant: prices are US
/// dollars with a dot (the Windows culture may be Danish, which would print "kr.").
/// </summary>
public static class Format
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary><c>0</c>, <c>512</c>, <c>920k</c>, <c>21.4M</c>, <c>1.5B</c>. Never shows "1000k" or "1000.0M".</summary>
    public static string Tokens(long tokens)
    {
        if (tokens < 1_000)
        {
            return tokens.ToString(Invariant);
        }

        var thousands = Math.Round(tokens / 1_000d, MidpointRounding.AwayFromZero);
        if (thousands < 1_000)
        {
            return thousands.ToString("0", Invariant) + "k";
        }

        var millions = Math.Round(tokens / 1_000_000d, 1, MidpointRounding.AwayFromZero);
        return millions < 1_000
            ? millions.ToString("0.0", Invariant) + "M"
            : (tokens / 1_000_000_000d).ToString("0.0", Invariant) + "B";
    }

    /// <summary><c>$14.82</c>, <c>≈$14.82</c> when part of the usage has no price, <c>$—</c> when none has.</summary>
    public static string Cost(UsageTotals totals) => totals.Cost switch
    {
        null => "$—",
        { } cost => (totals.IsPartial ? "≈$" : "$") + cost.ToString("N2", Invariant),
    };

    /// <summary><c>in 2.4M · out 920k · cache 18.1M</c> (in = input + cache writes, cache = cache hits).</summary>
    public static string Split(UsageTotals totals) =>
        $"in {Tokens(totals.InTokens)} · out {Tokens(totals.OutTokens)} · cache {Tokens(totals.CacheHits)}";

    /// <summary>The small line under a row: <c>$14.82 · in 2.4M · out 920k · cache 18.1M</c>.</summary>
    public static string SubLine(UsageTotals totals) => $"{Cost(totals)} · {Split(totals)}";

    /// <summary><c>just now</c>, <c>12s ago</c>, <c>41 min ago</c>, <c>3 h ago</c>, <c>2 d ago</c>.</summary>
    public static string Ago(TimeSpan elapsed) => elapsed.TotalSeconds switch
    {
        < 5 => "just now",
        < 60 => $"{(int)elapsed.TotalSeconds}s ago",
        < 3_600 => $"{(int)elapsed.TotalMinutes} min ago",
        < 86_400 => $"{(int)elapsed.TotalHours} h ago",
        _ => $"{(int)elapsed.TotalDays} d ago",
    };

    /// <summary>Timer text: <c>0:07</c>, <c>12:34</c>, <c>1:02:10</c>.</summary>
    public static string Timer(TimeSpan elapsed)
    {
        var t = elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
            : $"{t.Minutes}:{t.Seconds:00}";
    }
}
