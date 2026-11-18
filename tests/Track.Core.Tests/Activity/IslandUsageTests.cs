using Microsoft.Extensions.Time.Testing;
using Track.Core.Activity;
using Track.Core.Display;
using Track.Core.Ingest;
using Track.Core.Island;
using Track.Core.Parsing;
using Track.Core.Settings;
using Track.Core.Usage;

namespace Track.Core.Tests.Activity;

/// <summary>Round 5: the opened island's Now | Usage tabs.</summary>
public class IslandUsageTests
{
    private static readonly TimeZoneInfo Copenhagen = TimeZoneInfo.FindSystemTimeZoneById("Europe/Copenhagen");
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero); // 13:00 local

    private readonly FakeTimeProvider _time = new(Now);
    private readonly UsageStore _store = new();
    private readonly IslandStateMachine _machine;
    private IslandSettings _settings = new();

    public IslandUsageTests()
    {
        _machine = new IslandStateMachine(_time, () => _settings);
        _store.Add([
            new UsageRecord(Tool.Claude, "a", "claude-opus-5-5", Now.AddHours(-3), 1_000_000, 2_000_000, 18_000_000, 400_000, 0, "1"),
            new UsageRecord(Tool.Codex, "b", "gpt-6.1-sol", new DateTimeOffset(2026, 11, 16, 10, 0, 0, TimeSpan.Zero), 500_000, 0, 4_500_000, 100_000, 0, "2"),
            new UsageRecord(Tool.Copilot, "c", "claude-opus-5.5", Now.AddHours(-1), 999_499, 0, 0, 0, 0, "3"),
        ]);
    }

    private IslandViewModel Vm(Func<IReadOnlyList<Tool>>? listed = null) =>
        new(_machine, _store, new UsageAggregator(_store), new PeriodCalculator(_time, Copenhagen), _time, () => _settings, listed);

    private void Open()
    {
        _machine.Update([new AgentActivity(Tool.Claude, "a", Now.AddMinutes(-5), Now.AddHours(-1), "claude-opus-5-5")], false);
        _machine.Click();
    }

    [Fact]
    public void The_island_opens_on_the_now_tab_by_default_without_usage()
    {
        Open();
        var content = Vm().Build();

        Assert.Equal((IslandState.Open, IslandTab.Now), (content.State, content.Tab));
        Assert.Null(content.Usage);
        Assert.Single(content.Rows); // the running agent
        Assert.Equal(IslandTab.Now, new IslandSettings().OpenTab);
    }

    [Fact]
    public void The_usage_tab_shows_exactly_what_the_popover_shows()
    {
        Open();
        var island = Vm();
        island.SelectTab(IslandTab.Usage);
        var popover = new PopoverViewModel(_store, new UsageAggregator(_store), new PeriodCalculator(_time, Copenhagen), _time, new TrackSettings());
        popover.UsageRefreshed();

        foreach (var period in Enum.GetValues<Period>())
        {
            island.SelectPeriod(period);
            popover.SelectedPeriod = period;
            var usage = island.Build().Usage!;

            Assert.Equal(popover.Rows, usage.Rows);
            Assert.Equal(popover.TotalRow, usage.TotalRow);
            Assert.Equal(popover.PeriodLabel, usage.Label);
            Assert.Equal(popover.Chart!.Bars.Select(b => b.Tooltip), usage.Chart!.Bars.Select(b => b.Tooltip));
            Assert.Equal((popover.Chart.TopText, popover.ComparisonText), (usage.Chart.TopText, usage.ComparisonText));
            Assert.Equal(period, island.Period);
        }
    }

    [Fact]
    public void Switching_tabs_raises_a_change_once_and_does_not_close_the_island()
    {
        Open();
        var island = Vm();
        var changes = new List<IslandTab>();
        island.TabChanged += (_, t) => changes.Add(t);

        island.SelectTab(IslandTab.Usage);
        island.SelectTab(IslandTab.Usage);
        Assert.Equal((IslandState.Open, IslandTab.Usage), (island.Build().State, island.Build().Tab));

        island.SelectTab(IslandTab.Now);
        Assert.Equal([IslandTab.Usage, IslandTab.Now], changes);
        Assert.Null(island.Build().Usage);
    }

    [Fact]
    public void Switching_without_a_listener_works_too()
    {
        var island = Vm();
        island.SelectTab(IslandTab.Usage);

        Assert.Equal(IslandTab.Usage, island.Tab);
    }

    [Fact]
    public void The_saved_tab_is_used_after_a_restart()
    {
        _settings = _settings with { OpenTab = IslandTab.Usage };
        Open();

        var content = Vm().Build();

        Assert.Equal(IslandTab.Usage, content.Tab);
        Assert.Equal("Today", content.Usage!.Label);
    }

    [Fact]
    public void Clicking_a_bar_drills_in_and_switching_period_or_tab_clears_it()
    {
        Open();
        var island = Vm();
        island.SelectBucket(10); // Now tab: no chart, nothing happens
        island.SelectTab(IslandTab.Usage);
        island.Build();

        island.SelectBucket(10);
        Assert.Equal(("10:00–11:00", "21.4M"), (island.Build().Usage!.Label, island.Build().Usage!.Rows[0].TokenText));
        island.SelectBucket(10);
        Assert.Equal("Today", island.Build().Usage!.Label);

        island.SelectBucket(12);
        island.SelectPeriod(Period.Week);
        Assert.Equal("This week", island.Build().Usage!.Label);

        island.SelectBucket(0);
        Assert.Equal("Mon 16 Nov", island.Build().Usage!.Label);
        island.SelectTab(IslandTab.Now);
        island.SelectTab(IslandTab.Usage);
        Assert.Equal("This week", island.Build().Usage!.Label);
    }

    [Fact]
    public void The_list_of_tools_follows_the_popovers_visibility()
    {
        Open();
        var island = Vm(() => [Tool.Claude]);
        island.SelectTab(IslandTab.Usage);

        var usage = island.Build().Usage!;

        Assert.Equal(["Claude Code"], usage.Rows.Select(r => r.Name));
        Assert.Null(usage.TotalRow);
    }

    [Fact]
    public void Minimizing_from_the_usage_tab_and_reopening_keeps_the_tab()
    {
        Open();
        var island = Vm();
        island.SelectTab(IslandTab.Usage);

        _machine.Minimize();
        Assert.Equal(IslandState.Hidden, island.Build().State);

        _machine.PointerAtTop(true);
        _time.Advance(TimeSpan.FromSeconds(1));
        _machine.Tick();
        _machine.Click();

        Assert.Equal((IslandState.Open, IslandTab.Usage), (island.Build().State, island.Build().Tab));
    }

    [Fact]
    public void At_midnight_the_open_usage_tab_moves_to_the_new_day()
    {
        _time.SetUtcNow(new DateTimeOffset(2026, 11, 18, 22, 59, 59, TimeSpan.Zero)); // 23:59:59 local
        Open();
        var island = Vm();
        island.SelectTab(IslandTab.Usage);
        island.Build();
        island.SelectBucket(10);
        Assert.Equal("10:00–11:00", island.Build().Usage!.Label);

        _time.Advance(TimeSpan.FromSeconds(1));
        var usage = island.Build().Usage!;

        Assert.Equal(("Today", "0", "00:00–01:00\nno usage"), (usage.Label, usage.TotalRow!.TokenText, usage.Chart!.Bars[0].Tooltip));
    }
}
