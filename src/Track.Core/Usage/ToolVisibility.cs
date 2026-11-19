using Track.Core.Providers;
using Track.Core.Settings;

namespace Track.Core.Usage;

public enum TrayRule
{
    /// <summary>Leave a tool out of the tray only when it is below both the week and the month limit.</summary>
    BelowBoth,

    /// <summary>Leave a tool out of the tray when it is below either limit.</summary>
    BelowEither,
}

/// <summary>Round 3 choices: which tools earn a spot in the tray icon and the popover.</summary>
public sealed record VisibilitySettings
{
    public long TrayLimitWeek { get; set; } = 5_000_000;

    public long TrayLimitMonth { get; set; } = 10_000_000;

    public TrayRule TrayRule { get; set; } = TrayRule.BelowBoth;

    public WeekWindow WeekWindow { get; set; } = WeekWindow.LastFullWeek;

    public MonthWindow MonthWindow { get; set; } = MonthWindow.LastFullMonth;

    public bool ShowWhenRunning { get; set; } = true;

    /// <summary>
    /// Also count the current week and month, taking whichever is larger. Without this, a tool used
    /// heavily only this week is "below the limits" until next Monday (found with real data, 2026-11-18).
    /// </summary>
    public bool IncludeCurrentPeriod { get; set; } = true;

    public int RemoveAfterNoUsageDays { get; set; } = 30;
}

public enum VisibilityReason
{
    /// <summary>Used enough: in the tray and the popover.</summary>
    Used,

    /// <summary>Below the limits but running now, so shown in the tray anyway.</summary>
    BelowLimitsButRunning,

    /// <summary>Below the limits: popover only.</summary>
    BelowLimits,

    /// <summary>No usage in the window but running now: shown everywhere.</summary>
    NoUsageButRunning,

    /// <summary>No usage in the window and not running: hidden everywhere.</summary>
    NoUsage,

    /// <summary>The provider is explicitly disabled.</summary>
    Disabled,

    /// <summary>The provider is explicitly enabled.</summary>
    AlwaysOn,
}

public sealed record ToolVisibility(bool InTray, bool InPopover, VisibilityReason Reason);

/// <summary>The round 3 rule, as a pure function.</summary>
public static class ToolVisibilityPolicy
{
    public static ToolVisibility Decide(long weekTokens, long monthTokens, bool hasRecentUsage, bool running, VisibilitySettings settings)
    {
        if (!hasRecentUsage)
        {
            return running
                ? new ToolVisibility(true, true, VisibilityReason.NoUsageButRunning)
                : new ToolVisibility(false, false, VisibilityReason.NoUsage);
        }

        var belowWeek = weekTokens < settings.TrayLimitWeek;
        var belowMonth = monthTokens < settings.TrayLimitMonth;
        var below = settings.TrayRule == TrayRule.BelowBoth ? belowWeek && belowMonth : belowWeek || belowMonth;
        if (!below)
        {
            return new ToolVisibility(true, true, VisibilityReason.Used);
        }

        return running && settings.ShowWhenRunning
            ? new ToolVisibility(true, true, VisibilityReason.BelowLimitsButRunning)
            : new ToolVisibility(false, true, VisibilityReason.BelowLimits);
    }

    /// <summary>Applies the rule to the stored usage of one tool.</summary>
    public static ToolVisibility Decide(Tool tool, bool running, UsageAggregator usage, PeriodCalculator periods, VisibilitySettings settings)
    {
        long Tokens(TimeRange range) => usage.Totals(tool, range).Tokens.Total;

        var week = Tokens(periods.Range(settings.WeekWindow));
        var month = Tokens(periods.Range(settings.MonthWindow));
        if (settings.IncludeCurrentPeriod)
        {
            week = Math.Max(week, Tokens(periods.Range(Period.Week)));
            month = Math.Max(month, Tokens(periods.Range(Period.Month)));
        }

        return Decide(week, month, Tokens(periods.LastDays(settings.RemoveAfterNoUsageDays)) > 0, running, settings);
    }
}

/// <summary>Applies explicit provider modes while preserving the original visibility rule for legacy providers.</summary>
public static class ProviderVisibilityPolicy
{
    public static ToolVisibility Decide(
        Tool tool,
        ProviderMode mode,
        bool running,
        UsageAggregator usage,
        PeriodCalculator periods,
        VisibilitySettings visibility)
    {
        if (mode == ProviderMode.Off)
        {
            return new ToolVisibility(false, false, VisibilityReason.Disabled);
        }

        if (mode == ProviderMode.On)
        {
            return new ToolVisibility(true, true, VisibilityReason.AlwaysOn);
        }

        if (mode != ProviderMode.Auto)
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown provider mode.");
        }

        var hasRecentUsage = usage.Totals(tool, periods.LastDays(visibility.RemoveAfterNoUsageDays)).Tokens.Total > 0;
        if (ProviderCatalog.Get(tool).UsesLegacyVisibility)
        {
            return ToolVisibilityPolicy.Decide(tool, running, usage, periods, visibility);
        }

        return hasRecentUsage
            ? new ToolVisibility(true, true, VisibilityReason.Used)
            : running
                ? new ToolVisibility(true, true, VisibilityReason.NoUsageButRunning)
                : new ToolVisibility(false, false, VisibilityReason.NoUsage);
    }
}
