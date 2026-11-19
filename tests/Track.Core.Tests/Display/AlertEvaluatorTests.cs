using Track.Core.Display;
using Track.Core.Ingest;
using Track.Core.Settings;
using Track.Core.Usage;

namespace Track.Core.Tests.Display;

public sealed class AlertEvaluatorTests
{
    private readonly AlertEvaluator _evaluator = new();
    private static readonly DateOnly Day = new(2026, 11, 20);

    [Fact]
    public void Disabled_budgets_do_not_evaluate_any_threshold()
    {
        var alerts = _evaluator.Evaluate(
            new BudgetSettings { Total = new BudgetThreshold(1, 1) },
            Day,
            new Dictionary<Tool, UsageTotals>(),
            new UsageTotals(new TokenCounts(1, 0, 0, 0, 0, 1), 2, 0),
            new HashSet<string>(),
            notificationsEnabled: true);

        Assert.Empty(alerts);
    }

    [Fact]
    public void Spend_and_token_thresholds_fire_once_for_total_and_provider()
    {
        var settings = new BudgetSettings
        {
            Enabled = true,
            Period = BudgetPeriod.Month,
            Total = new BudgetThreshold(2, 100),
            Providers = new Dictionary<Tool, BudgetThreshold> { [Tool.Gemini] = new(1, 40) },
        };
        var totals = new Dictionary<Tool, UsageTotals>
        {
            [Tool.Gemini] = new(new TokenCounts(20, 0, 0, 20, 0, 1), 1, 0),
        };

        var alerts = _evaluator.Evaluate(settings, Day, totals, new UsageTotals(new TokenCounts(60, 0, 0, 60, 0, 2), 3, 0), new HashSet<string>(), notificationsEnabled: true);

        Assert.Equal(4, alerts.Count);
        Assert.Contains(alerts, alert => alert.Tool is null && alert.Metric == BudgetMetric.Spend && alert.Message.Contains("this month", StringComparison.Ordinal));
        Assert.Contains(alerts, alert => alert.Tool == Tool.Gemini && alert.Metric == BudgetMetric.Tokens);
        Assert.Empty(_evaluator.Evaluate(settings, Day, totals, new UsageTotals(new TokenCounts(60, 0, 0, 60, 0, 2), 3, 0), alerts.Select(alert => alert.Key).ToHashSet(), notificationsEnabled: true));
    }

    [Fact]
    public void Thresholds_fire_at_the_exact_value_and_ignore_unpriced_known_cost()
    {
        var settings = new BudgetSettings
        {
            Enabled = true,
            Total = new BudgetThreshold(5, 50),
        };

        var alerts = _evaluator.Evaluate(
            settings,
            Day,
            new Dictionary<Tool, UsageTotals>(),
            new UsageTotals(new TokenCounts(25, 0, 0, 25, 0, 1), 5, 25),
            new HashSet<string>(),
            notificationsEnabled: true);

        Assert.Equal(2, alerts.Count);
    }

    [Fact]
    public void Fired_key_includes_period_provider_metric_and_threshold()
    {
        var settings = new BudgetSettings { Enabled = true, Total = new BudgetThreshold(null, 10) };
        var totals = new UsageTotals(new TokenCounts(10, 0, 0, 0, 0, 1), 0, 10);
        var alert = Assert.Single(_evaluator.Evaluate(settings, Day, new Dictionary<Tool, UsageTotals>(), totals, new HashSet<string>(), notificationsEnabled: true));

        Assert.Equal("Day|2026-11-20|total|Tokens|10", alert.Key);
    }

    [Fact]
    public void Global_notifications_off_suppresses_budget_alerts()
    {
        var settings = new BudgetSettings { Enabled = true, Total = new BudgetThreshold(null, 10) };
        var totals = new UsageTotals(new TokenCounts(10, 0, 0, 0, 0, 1), 0, 10);

        var alerts = _evaluator.Evaluate(settings, Day, new Dictionary<Tool, UsageTotals>(), totals, new HashSet<string>(), notificationsEnabled: false);

        Assert.Empty(alerts);
    }

    [Fact]
    public void Thresholds_below_current_usage_do_not_fire()
    {
        var settings = new BudgetSettings { Enabled = true, Total = new BudgetThreshold(10, 100) };
        var totals = new UsageTotals(new TokenCounts(50, 0, 0, 49, 0, 1), 9, 0);

        var alerts = _evaluator.Evaluate(settings, Day, new Dictionary<Tool, UsageTotals>(), totals, new HashSet<string>(), notificationsEnabled: true);

        Assert.Empty(alerts);
    }

    [Fact]
    public void Nonpositive_loaded_thresholds_do_not_fire()
    {
        var settings = new BudgetSettings { Enabled = true, Total = new BudgetThreshold(0, 0) };
        var totals = new UsageTotals(new TokenCounts(10, 0, 0, 10, 0, 1), 10, 0);

        var alerts = _evaluator.Evaluate(settings, Day, new Dictionary<Tool, UsageTotals>(), totals, new HashSet<string>(), notificationsEnabled: true);

        Assert.Empty(alerts);
    }

    [Fact]
    public void Empty_thresholds_do_not_fire_for_usage()
    {
        var settings = new BudgetSettings { Enabled = true };
        var totals = new UsageTotals(new TokenCounts(10, 0, 0, 10, 0, 1), 10, 0);

        var alerts = _evaluator.Evaluate(settings, Day, new Dictionary<Tool, UsageTotals>(), totals, new HashSet<string>(), notificationsEnabled: true);

        Assert.Empty(alerts);
    }
}
