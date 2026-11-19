using Track.Core.Ingest;
using Track.Core.Pricing;

namespace Track.Core.Usage;

/// <summary>
/// Tokens and estimated cost for some slice of usage. Tokens without a known price are counted in
/// <see cref="Tokens"/> but add nothing to <see cref="KnownCost"/>.
/// </summary>
public readonly record struct UsageTotals(TokenCounts Tokens, decimal KnownCost, long UnpricedTokens)
{
    public static readonly UsageTotals Empty = default;

    /// <summary>Estimated cost, or null when there are tokens but none of them has a known price.</summary>
    public decimal? Cost => Tokens.Total > 0 && UnpricedTokens == Tokens.Total ? null : KnownCost;

    /// <summary>True when part (not all) of the tokens has no known price, so <see cref="Cost"/> is a lower bound.</summary>
    public bool IsPartial => UnpricedTokens > 0 && UnpricedTokens < Tokens.Total;

    /// <summary>"in": input tokens, including those written to the cache.</summary>
    public long InTokens => Tokens.Input + Tokens.CacheWrite;

    /// <summary>"out": output tokens, reasoning included.</summary>
    public long OutTokens => Tokens.Output;

    /// <summary>"cache": tokens served from the cache.</summary>
    public long CacheHits => Tokens.CacheRead;

    public static UsageTotals operator +(UsageTotals a, UsageTotals b) =>
        new(a.Tokens + b.Tokens, a.KnownCost + b.KnownCost, a.UnpricedTokens + b.UnpricedTokens);
}

/// <summary>Usage of one model inside a tool and period.</summary>
public sealed record ModelUsage(string Model, UsageTotals Totals);

/// <summary>Sums the store's hourly buckets for a tool and time range, priced with the current price table.</summary>
public sealed class UsageAggregator(UsageStore store)
{
    /// <summary>Prices in use; replace after a price refresh.</summary>
    public PriceTable Prices { get; set; } = PriceTable.Bundled;

    /// <summary>Totals for one tool, or for all tools when <paramref name="tool"/> is null.</summary>
    public UsageTotals Totals(Tool? tool, TimeRange range) =>
        Select(tool, range).Aggregate(UsageTotals.Empty, (sum, b) => sum + Price(b.Key.Model, b.Value));

    /// <summary>Per-model totals for one tool, largest first.</summary>
    public IReadOnlyList<ModelUsage> ByModel(Tool tool, TimeRange range) =>
        Select(tool, range)
            .GroupBy(b => b.Key.Model, StringComparer.Ordinal)
            .Select(g => new ModelUsage(g.Key, g.Aggregate(UsageTotals.Empty, (sum, b) => sum + Price(b.Key.Model, b.Value))))
            .OrderByDescending(m => m.Totals.Tokens.Total)
            .ThenBy(m => m.Model, StringComparer.Ordinal)
            .ToList();

    /// <summary>The model used most in the latest hour with usage for this tool, or null when there is none.</summary>
    public string? LatestModel(Tool tool) =>
        store.Buckets
            .Where(b => b.Key.Tool == tool)
            .OrderByDescending(b => b.Key.HourUtc)
            .ThenByDescending(b => b.Value.Total)
            .Select(b => b.Key.Model)
            .FirstOrDefault();

    /// <summary>When this tool was last used (its latest session activity in the store), or null.</summary>
    public DateTimeOffset? LastUsed(Tool tool) =>
        store.Sessions.Where(s => s.Key.Tool == tool).Select(s => (DateTimeOffset?)s.Value.LastUtc).Max();

    private IEnumerable<KeyValuePair<UsageBucketKey, TokenCounts>> Select(Tool? tool, TimeRange range) =>
        store.Buckets.Where(b => (tool is null || b.Key.Tool == tool) && range.Contains(b.Key.HourUtc));

    /// <summary>One bucket's tokens with their estimated price (unpriced tokens tracked separately).</summary>
    public UsageTotals Price(string model, TokenCounts tokens) => Prices.Cost(model, tokens) is { } cost
        ? new UsageTotals(tokens, cost, 0)
        : new UsageTotals(tokens, 0, tokens.Total);
}
