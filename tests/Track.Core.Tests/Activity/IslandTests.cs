using Microsoft.Extensions.Time.Testing;
using Track.Core.Activity;
using Track.Core.Ingest;
using Track.Core.Island;
using Track.Core.Parsing;
using Track.Core.Usage;

namespace Track.Core.Tests.Activity;

public class IslandTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(Now);
    private IslandSettings _settings = new();
    private readonly IslandStateMachine _island;

    public IslandTests() => _island = new IslandStateMachine(_time, () => _settings);

    private static AgentActivity Agent(Tool tool, string session = "s", double turnMinutesAgo = 12.5, double sessionMinutesAgo = 60, string? model = "claude-opus-5-5") =>
        new(tool, session, Now.AddMinutes(-turnMinutesAgo), Now.AddMinutes(-sessionMinutesAgo), model);

    private void Advance(double seconds) => _time.Advance(TimeSpan.FromSeconds(seconds));

    [Fact]
    public void Defaults_are_the_round_4_choices()
    {
        Assert.Equal(
            (true, IslandTimerFrom.Turn, IslandCompact.Tokens, IslandActivity.Wave, IslandMulti.Split, true, 5, 600, IslandExpand.Click, IslandExpanded.Agents, IslandScreen.Primary, true),
            (_settings.Enabled, _settings.TimerFrom, _settings.Compact, _settings.Activity, _settings.Multi, _settings.ShowDone, _settings.HideAfterMinutes, _settings.RevealDelayMs, _settings.Expand, _settings.Expanded, _settings.Screen, _settings.HideOverFullScreen));
    }

    [Fact]
    public void Hidden_until_work_starts_then_active_done_idle_and_hidden_again()
    {
        Assert.Equal(IslandState.Hidden, _island.State);
        Assert.False(_island.IsVisible);

        _island.Update([Agent(Tool.Claude)], fullScreen: false);
        Assert.Equal(IslandState.Active, _island.State);

        Advance(60);
        _island.Update([], fullScreen: false);
        Assert.Equal(IslandState.Done, _island.State);
        Assert.Equal(new FinishedTask(Tool.Claude, TimeSpan.FromMinutes(13.5)), _island.Finished);

        Advance(5);
        Assert.Equal(IslandState.Idle, _island.State);

        Advance((5 * 60) - 6);
        _island.Update([], fullScreen: false);
        Assert.Equal(IslandState.Idle, _island.State);
        Advance(1);
        Assert.Equal(IslandState.Hidden, _island.State);
    }

    [Fact]
    public void Without_the_done_note_an_ended_task_goes_straight_to_idle()
    {
        _settings = _settings with { ShowDone = false };
        _island.Update([Agent(Tool.Codex)], false);
        _island.Update([], false);

        Assert.Equal(IslandState.Idle, _island.State);
        Assert.Null(_island.Finished);
    }

    [Fact]
    public void The_top_edge_reveals_the_island_after_the_delay_and_hides_it_when_left()
    {
        _island.PointerAtTop(true);
        Advance(0.5);
        _island.Tick();
        Assert.Equal(IslandState.Hidden, _island.State); // 0.5 s: a passing touch does nothing

        _island.PointerAtTop(true); // still there: the dwell keeps counting from the first moment
        Advance(0.1);
        _island.Tick();
        Assert.Equal(IslandState.Revealed, _island.State);

        _island.PointerAtTop(false);
        Assert.Equal(IslandState.Hidden, _island.State);
    }

    [Fact]
    public void Moving_from_the_edge_onto_the_island_keeps_it_until_the_pointer_leaves_both()
    {
        _island.PointerAtTop(true);
        Advance(1);
        _island.Tick();
        _island.PointerOverIsland(true);
        _island.PointerAtTop(false);
        Assert.Equal(IslandState.Revealed, _island.State);

        _island.PointerOverIsland(false);
        Assert.Equal(IslandState.Hidden, _island.State);
    }

    [Fact]
    public void Leaving_the_island_while_still_at_the_top_keeps_it_revealed()
    {
        _island.PointerAtTop(true);
        Advance(1);
        _island.Tick();
        _island.PointerOverIsland(true);
        _island.PointerOverIsland(false);

        Assert.Equal(IslandState.Revealed, _island.State);
    }

    [Fact]
    public void Click_opens_and_closes_and_escape_closes_while_hover_alone_does_not_open()
    {
        _island.Click();
        Assert.Equal(IslandState.Hidden, _island.State); // nothing to click while hidden

        _island.Update([Agent(Tool.Claude)], false);
        _island.PointerOverIsland(true);
        Advance(1);
        _island.Tick();
        Assert.Equal(IslandState.Active, _island.State);

        _island.Click();
        Assert.Equal(IslandState.Open, _island.State);
        _island.PointerOverIsland(false);
        Assert.Equal(IslandState.Open, _island.State); // click mode: leaving does not close
        _island.Click();
        Assert.Equal(IslandState.Active, _island.State);

        _island.Click();
        _island.Escape();
        Assert.Equal(IslandState.Active, _island.State);
    }

    [Fact]
    public void Minimize_tucks_a_running_island_away_and_the_top_edge_peeks_it_back()
    {
        _island.Update([Agent(Tool.Claude)], false);
        _island.Click();
        Assert.True(ViewModel().Build().CanMinimize);

        _island.Minimize();
        Assert.Equal((IslandState.Hidden, true), (_island.State, _island.IsMinimized));
        _island.Update([Agent(Tool.Claude)], false);
        Assert.Equal(IslandState.Hidden, _island.State); // still running, still tucked away

        _island.PointerAtTop(true);
        Advance(1);
        _island.Tick();
        Assert.Equal(IslandState.Active, _island.State); // peek
        _island.PointerAtTop(false);
        Assert.Equal(IslandState.Hidden, _island.State);

        _island.PointerAtTop(true);
        Advance(1);
        _island.Tick();
        _island.Click(); // opening it again ends the minimized state
        Assert.Equal((IslandState.Open, false), (_island.State, _island.IsMinimized));
        _island.Click();
        _island.PointerAtTop(false);
        Assert.Equal(IslandState.Active, _island.State);
    }

    [Fact]
    public void Minimize_ends_when_nothing_runs_any_more()
    {
        _island.Update([Agent(Tool.Claude)], false);
        _island.Minimize();

        _island.Update([], false);

        Assert.Equal((IslandState.Done, false), (_island.State, _island.IsMinimized));
    }

    [Fact]
    public void Minimize_with_nothing_running_just_closes_and_there_is_no_button()
    {
        _island.PointerAtTop(true);
        Advance(1);
        _island.Tick();
        _island.Click();
        Assert.False(ViewModel().Build().CanMinimize);

        _island.Minimize();

        Assert.Equal((IslandState.Hidden, false), (_island.State, _island.IsMinimized));
    }

    [Fact]
    public void Hover_mode_opens_after_a_short_rest_and_closes_on_leave()
    {
        _settings = _settings with { Expand = IslandExpand.Hover };
        _island.Update([Agent(Tool.Claude)], false);
        _island.PointerOverIsland(true);
        Advance(0.1);
        _island.Tick();
        Assert.Equal(IslandState.Active, _island.State);

        Advance(0.1);
        _island.Tick();
        Assert.Equal(IslandState.Open, _island.State);

        _island.PointerOverIsland(false);
        Assert.Equal(IslandState.Active, _island.State);
    }

    [Fact]
    public void Hover_mode_counts_the_rest_from_the_first_moment_and_needs_the_pointer()
    {
        _settings = _settings with { Expand = IslandExpand.Hover };
        _island.Update([Agent(Tool.Claude)], false);
        _island.Tick(); // pointer not over the island
        Assert.Equal(IslandState.Active, _island.State);

        _island.PointerOverIsland(true);
        Advance(0.1);
        _island.PointerOverIsland(true); // still over: does not restart the delay
        Advance(0.1);
        _island.Tick();
        Assert.Equal(IslandState.Open, _island.State);
    }

    [Fact]
    public void Hover_mode_does_not_open_a_hidden_island()
    {
        _settings = _settings with { Expand = IslandExpand.Hover };
        _island.PointerOverIsland(true);
        Advance(1);
        _island.Tick();

        Assert.Equal(IslandState.Hidden, _island.State);
    }

    [Fact]
    public void Full_screen_apps_hide_it_and_close_it_unless_allowed()
    {
        _island.Update([Agent(Tool.Claude)], false);
        _island.Click();
        _island.Update([Agent(Tool.Claude)], fullScreen: true);
        Assert.Equal(IslandState.Hidden, _island.State);

        _settings = _settings with { HideOverFullScreen = false };
        Assert.Equal(IslandState.Active, _island.State);
    }

    [Fact]
    public void Island_off_is_never_visible()
    {
        _settings = _settings with { Enabled = false };
        _island.Update([Agent(Tool.Claude)], false);
        _island.Click();

        Assert.Equal(IslandState.Hidden, _island.State);
    }

    // ---- content ----

    private IslandViewModel ViewModel(UsageStore? store = null)
    {
        store ??= new UsageStore();
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Copenhagen");
        return new IslandViewModel(_island, store, new UsageAggregator(store), new PeriodCalculator(_time, zone), _time, () => _settings);
    }

    private static UsageStore StoreWith(params (Tool Tool, string Session, string Model, long Input, double HoursAgo)[] usage)
    {
        var store = new UsageStore();
        store.Add(usage.Select(u => new UsageRecord(u.Tool, u.Session, u.Model, Now.AddHours(-u.HoursAgo), u.Input, 0, 0, 0, 0, "k")));
        return store;
    }

    [Fact]
    public void One_agent_shows_logo_task_timer_and_session_tokens()
    {
        _island.Update([Agent(Tool.Claude)], false);
        var content = ViewModel(StoreWith((Tool.Claude, "s", "claude-opus-5-5", 1_800_000, 0.1))).Build();

        Assert.Equal((IslandState.Active, (Tool?)Tool.Claude, "12:30", "1.8M", IslandActivity.Wave), (content.State, content.MainTool, content.MainText, content.MainExtra, content.Activity));
        Assert.Equal([Tool.Claude], content.Logos);
        Assert.Empty(content.Bubble);
    }

    [Theory]
    [InlineData(IslandCompact.Timer, IslandTimerFrom.Turn, "12:30", null)]
    [InlineData(IslandCompact.Model, IslandTimerFrom.Session, "1:00:00", "claude-opus-5-5")]
    [InlineData(IslandCompact.Tokens, IslandTimerFrom.Turn, "12:30", "0")]
    public void Compact_options_change_the_extra_text_and_the_timer_start(IslandCompact compact, IslandTimerFrom from, string timer, string? extra)
    {
        _settings = _settings with { Compact = compact, TimerFrom = from };
        _island.Update([Agent(Tool.Claude)], false);

        var content = ViewModel().Build();

        Assert.Equal((timer, extra), (content.MainText, content.MainExtra));
    }

    [Fact]
    public void Two_agents_split_into_main_and_bubble()
    {
        _island.Update([Agent(Tool.Claude, "a"), Agent(Tool.Copilot, "b", 3)], false);

        var content = ViewModel().Build();

        Assert.Equal(Tool.Claude, content.MainTool);
        Assert.Equal([Tool.Copilot], content.Bubble);
        Assert.Null(content.Counter);
    }

    [Fact]
    public void Merged_shows_all_logos_and_a_count()
    {
        _settings = _settings with { Multi = IslandMulti.Merged };
        _island.Update([Agent(Tool.Claude, "a"), Agent(Tool.Claude, "a2"), Agent(Tool.Codex, "b")], false);

        var content = ViewModel().Build();

        Assert.Equal([Tool.Claude, Tool.Codex], content.Logos);
        Assert.Equal("+2", content.Counter);
    }

    [Fact]
    public void Cycle_rotates_every_four_seconds_with_a_position()
    {
        _settings = _settings with { Multi = IslandMulti.Cycle };
        _island.Update([Agent(Tool.Claude, "a"), Agent(Tool.Codex, "b")], false);
        var vm = ViewModel();

        var first = vm.Build();
        Advance(4);
        var second = vm.Build();

        Assert.NotEqual(first.MainTool, second.MainTool);
        Assert.Equal(["1/2", "2/2"], new[] { first.Counter, second.Counter }.Order());
    }

    [Fact]
    public void Done_and_idle_texts()
    {
        var vm = ViewModel(StoreWith((Tool.Codex, "x", "gpt-6.1-sol", 21_400_000, 1)));
        _island.Update([Agent(Tool.Claude, turnMinutesAgo: 12)], false);
        Advance(34);
        _island.Update([], false);

        var done = vm.Build();
        Assert.Equal(("Claude Code done", "12:34"), (done.MainText, done.MainExtra));

        Advance(5);
        var idle = vm.Build();
        Assert.Equal((IslandState.Idle, "Idle", "today 21.4M"), (idle.State, idle.MainText, idle.MainExtra));
        Assert.Null(idle.MainTool);
    }

    [Fact]
    public void Hidden_has_no_content()
    {
        var content = ViewModel().Build();

        Assert.Equal((IslandState.Hidden, ""), (content.State, content.MainText));
    }

    [Fact]
    public void Opened_island_lists_agents_with_both_timers_and_totals()
    {
        var store = StoreWith((Tool.Claude, "a", "claude-opus-5-5", 1_800_000, 0.1), (Tool.Codex, "b", "gpt-6.1-sol", 500_000, 30));
        _island.Update([Agent(Tool.Claude, "a"), Agent(Tool.Codex, "b", 2, 5, model: null)], false);
        _island.Click();

        var content = ViewModel(store).Build();

        Assert.Equal(IslandState.Open, content.State);
        Assert.Equal("2 agents working", content.Header);
        Assert.Equal("today 1.8M · $7.20", content.HeaderRight);
        Assert.Equal(
            [
                new IslandAgentRow(Tool.Claude, "Claude Code", "claude-opus-5-5 · 1.8M this session", "12:30", "session 1:00:00"),
                new IslandAgentRow(Tool.Codex, "Codex", "model unknown · 500k this session", "2:00", "session 5:00"),
            ],
            content.Rows);
        Assert.Equal("week 2.3M · month 2.3M", content.Footer);
        Assert.Empty(content.Table);
    }

    [Theory]
    [InlineData(0, "Nothing running")]
    [InlineData(1, "1 agent working")]
    public void Opened_header_counts_agents(int agents, string header)
    {
        _island.Update(agents == 0 ? [] : [Agent(Tool.Claude)], false);
        _island.Update(agents == 0 ? [] : [Agent(Tool.Claude)], false);
        _island.PointerAtTop(true);
        Advance(1);
        _island.Tick();
        _island.Click();

        Assert.Equal(header, ViewModel().Build().Header);
    }

    [Fact]
    public void Full_opened_island_adds_a_today_week_month_table()
    {
        _settings = _settings with { Expanded = IslandExpanded.Full };
        _island.Update([Agent(Tool.Claude)], false);
        _island.Click();

        var content = ViewModel(StoreWith((Tool.Copilot, "c", "gpt-6-luna", 3_000_000, 30))).Build();

        Assert.Equal(new IslandTableRow(Tool.Copilot, "Copilot", "0", "3.0M", "3.0M"), content.Table[2]);
        Assert.Null(content.Footer);
    }

    [Fact]
    public void An_agent_that_stops_to_ask_you_something_needs_you_instead_of_being_done()
    {
        var vm = ViewModel();
        _island.Update([Agent(Tool.Copilot, "p", turnMinutesAgo: 2)], false);
        Advance(5);
        _island.Update([], false, new HashSet<(Tool, string)> { (Tool.Copilot, "p") });

        Assert.Equal(new FinishedTask(Tool.Copilot, TimeSpan.FromSeconds(125), NeedsYou: true), _island.Finished);
        var content = vm.Build();
        Assert.Equal(("Copilot needs you", "2:05", true), (content.MainText, content.MainExtra, content.NeedsYou));

        // A different session waiting does not make this one "need you".
        Advance(10);
        _island.Update([Agent(Tool.Copilot, "p", turnMinutesAgo: 2)], false);
        _island.Update([], false, new HashSet<(Tool, string)> { (Tool.Copilot, "other") });
        Assert.Equal(("Copilot done", false), (vm.Build().MainText, vm.Build().NeedsYou));
    }
}
