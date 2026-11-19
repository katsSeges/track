using Microsoft.Extensions.Time.Testing;
using Track.Core.Ingest;
using Track.Core.Parsing;
using Track.Core.Settings;
using Track.Core.Usage;

namespace Track.Core.Tests.Usage;

public class ProviderVisibilityPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    private readonly UsageStore _store = new();
    private readonly FakeTimeProvider _time = new(Now);
    private readonly UsageAggregator _usage;
    private readonly PeriodCalculator _periods;

    public ProviderVisibilityPolicyTests()
    {
        _usage = new UsageAggregator(_store);
        _periods = new PeriodCalculator(_time, Utc);
    }

    [Fact]
    public void Off_hides_even_a_running_provider_and_on_forces_an_empty_provider_visible()
    {
        Assert.Equal(
            new ToolVisibility(false, false, VisibilityReason.Disabled),
            ProviderVisibilityPolicy.Decide(Tool.Gemini, ProviderMode.Off, true, _usage, _periods, new VisibilitySettings()));
        Assert.Equal(
            new ToolVisibility(true, true, VisibilityReason.AlwaysOn),
            ProviderVisibilityPolicy.Decide(Tool.Gemini, ProviderMode.On, false, _usage, _periods, new VisibilitySettings()));
    }

    [Fact]
    public void Auto_shows_new_providers_with_recent_usage_or_a_running_process_only()
    {
        var visibility = new VisibilitySettings();

        Assert.Equal(
            new ToolVisibility(false, false, VisibilityReason.NoUsage),
            ProviderVisibilityPolicy.Decide(Tool.Gemini, ProviderMode.Auto, false, _usage, _periods, visibility));
        Assert.Equal(
            new ToolVisibility(true, true, VisibilityReason.NoUsageButRunning),
            ProviderVisibilityPolicy.Decide(Tool.Gemini, ProviderMode.Auto, true, _usage, _periods, visibility));

        _store.Add([new UsageRecord(Tool.Gemini, "s", "gemini-2.5-pro", Now, 1, 0, 0, 0, 0, "k")]);
        Assert.Equal(
            new ToolVisibility(true, true, VisibilityReason.Used),
            ProviderVisibilityPolicy.Decide(Tool.Gemini, ProviderMode.Auto, false, _usage, _periods, visibility));
    }

    [Fact]
    public void Auto_preserves_the_legacy_usage_threshold_policy()
    {
        _store.Add([new UsageRecord(Tool.Claude, "s", "claude-opus-5-5", Now, 1, 0, 0, 0, 0, "k")]);

        Assert.Equal(
            new ToolVisibility(false, true, VisibilityReason.BelowLimits),
            ProviderVisibilityPolicy.Decide(Tool.Claude, ProviderMode.Auto, false, _usage, _periods, new VisibilitySettings()));
    }

    [Fact]
    public void Unknown_mode_and_tool_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ProviderVisibilityPolicy.Decide(Tool.Gemini, (ProviderMode)99, false, _usage, _periods, new VisibilitySettings()));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ProviderVisibilityPolicy.Decide((Tool)99, ProviderMode.Auto, false, _usage, _periods, new VisibilitySettings()));
    }
}
