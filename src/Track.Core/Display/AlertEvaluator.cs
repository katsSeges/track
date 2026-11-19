using System.Globalization;
using Track.Core.Settings;
using Track.Core.Usage;

namespace Track.Core.Display;

public enum BudgetMetric
{
    Spend,
    Tokens,
}

public sealed record BudgetAlert(string Key, string Message, Tool? Tool, BudgetMetric Metric);

/// <summary>Evaluates day or month budget thresholds without mutating persisted fired-state.</summary>
public sealed class AlertEvaluator
{
    /// <summary>
    /// The fired keys to save after <paramref name="alerts"/> fire: the new ones, plus earlier ones from this or the
    /// previous month. Older keys can never match again (their period is over), so the file does not grow forever.
    /// </summary>
    public static IReadOnlySet<string> Fired(IReadOnlySet<string> firedKeys, IEnumerable<BudgetAlert> alerts, DateOnly periodStart)
    {
        var oldest = new DateOnly(periodStart.Year, periodStart.Month, 1).AddMonths(-1);
        var kept = firedKeys.Where(key => key.Split('|') is [_, var date, ..]
            && DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)
            && start >= oldest);
        return kept.Concat(alerts.Select(alert => alert.Key)).ToHashSet(StringComparer.Ordinal);
    }

    public IReadOnlyList<BudgetAlert> Evaluate(
        BudgetSettings settings,
        DateOnly periodStart,
        IReadOnlyDictionary<Tool, UsageTotals> providers,
        UsageTotals total,
        IReadOnlySet<string> firedKeys,
        bool notificationsEnabled)
    {
        if (!notificationsEnabled || !settings.Enabled)
        {
            return [];
        }

        var result = new List<BudgetAlert>();
        EvaluateThreshold(null, settings.Total, total);
        foreach (var (tool, threshold) in settings.Providers.OrderBy(pair => pair.Key))
        {
            EvaluateThreshold(tool, threshold, providers.GetValueOrDefault(tool));
        }

        return result;

        void EvaluateThreshold(Tool? tool, BudgetThreshold threshold, UsageTotals usage)
        {
            if (threshold.Spend is > 0 and { } spend && usage.KnownCost >= spend)
            {
                // Prices are in US dollars ("C" with the invariant culture would print "¤").
                Add(tool, BudgetMetric.Spend, spend, $"known spend reached ${spend.ToString("0.##", CultureInfo.InvariantCulture)}");
            }

            if (threshold.Tokens is > 0 and { } tokens && usage.Tokens.Total >= tokens)
            {
                Add(tool, BudgetMetric.Tokens, tokens, $"tokens reached {tokens.ToString("N0", CultureInfo.InvariantCulture)}");
            }
        }

        void Add(Tool? tool, BudgetMetric metric, object threshold, string description)
        {
            var owner = tool?.ToString() ?? "total";
            var thresholdText = threshold is decimal spend ? spend.ToString(CultureInfo.InvariantCulture) : threshold.ToString();
            var key = $"{settings.Period}|{periodStart:yyyy-MM-dd}|{owner}|{metric}|{thresholdText}";
            if (firedKeys.Contains(key))
            {
                return;
            }

            var name = tool is { } provider ? Providers.ProviderCatalog.Get(provider).Name : "Total";
            var periodName = settings.Period == BudgetPeriod.Day ? "today" : "this month";
            result.Add(new BudgetAlert(key, $"{name} {description} {periodName}.", tool, metric));
        }
    }
}
