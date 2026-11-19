using Microsoft.Extensions.Time.Testing;
using Track.Core.Activity;
using Track.Core.Ingest;
using Track.Core.Island;
using Track.Core.Parsing;
using Track.Core.Usage;

namespace Track.Core.Tests.Activity;

/// <summary>Round 6 in the island: sub-agent rows, a stopped timer for background work, the compact dots, session tokens.</summary>
public class IslandSubAgentTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);
    private readonly FakeTimeProvider _time = new(Now);
    private readonly IslandSettings _settings = new();
    private readonly IslandStateMachine _island;
    private readonly UsageStore _store = new();

    public IslandSubAgentTests() => _island = new IslandStateMachine(_time, () => _settings);

    private IslandViewModel ViewModel() =>
        new(_island, _store, new UsageAggregator(_store), new PeriodCalculator(_time, TimeZoneInfo.FindSystemTimeZoneById("Europe/Copenhagen")), _time, () => _settings);

    private static SubAgentActivity Sub(string id, string? name, string? task, string? model, long tokens, int depth, double startedSecondsAgo, double? endedSecondsAgo = null, bool own = false, Tool tool = Tool.Claude) =>
        new(tool, id, name, task, model, depth, Now.AddSeconds(-startedSecondsAgo), endedSecondsAgo is { } e ? Now.AddSeconds(-e) : null, tokens, own);

    private void Use(Tool tool, string session, long input) =>
        _store.Add([new UsageRecord(tool, session, "m", Now.AddMinutes(-5), input, 0, 0, 0, 0, Guid.NewGuid().ToString())]);

    [Fact]
    public void Each_agent_lists_its_sub_agents_with_name_task_model_tokens_and_run_time()
    {
        Use(Tool.Claude, "s", 2_000_000);
        var agent = new AgentActivity(Tool.Claude, "s", Now.AddSeconds(-754), Now.AddHours(-1), "claude-opus-5-5", SubAgents:
        [
            Sub("a1", "Explore", "Find where chart clicks are hit-tested", "claude-haiku-5-5", 640_000, 1, 72),
            Sub("a2", "general-purpose", "Update Playwright tests", "claude-opus-5-5", 0, 2, 31),
            Sub("a3", null, null, null, 1_200, 1, 300, endedSecondsAgo: 55),
        ]);
        _island.Update([agent], false);
        _island.Click();

        var row = Assert.Single(ViewModel().Build().Rows);
        Assert.Equal(("12:34", "claude-opus-5-5 · 2.0M this session"), (row.TaskTimer, row.Detail));
        Assert.Equal(
            [
                new IslandSubAgentRow(Tool.Claude, "Explore", "Find where chart clicks are hit-tested", "claude-haiku-5-5 · 640k tokens", "1:12", true, 1),
                new IslandSubAgentRow(Tool.Claude, "general-purpose", "Update Playwright tests", "claude-opus-5-5", "0:31", true, 2),
                new IslandSubAgentRow(Tool.Claude, "sub-agent", null, "1k tokens", "done 4:05", false, 1),
            ],
            row.Subs);
    }

    [Fact]
    public void An_agent_without_sub_agents_has_no_rows_and_no_dots()
    {
        _island.Update([new AgentActivity(Tool.Copilot, "p", Now.AddSeconds(-30), Now.AddHours(-1), "gpt-6.1-sol")], false);
        var compact = ViewModel().Build();
        Assert.Equal(0, compact.SubAgentDots);

        _island.Click();
        Assert.Null(Assert.Single(ViewModel().Build().Rows).SubAgents);
        Assert.Empty(Assert.Single(ViewModel().Build().Rows).Subs);
    }

    [Fact]
    public void Background_work_stops_the_agents_timer_and_the_compact_pill_shows_a_dot_per_running_sub_agent_up_to_four()
    {
        var subs = Enumerable.Range(1, 6).Select(i => Sub("x" + i, "QA", null, null, 0, 1, 60 * i, endedSecondsAgo: i == 6 ? 10 : null, tool: Tool.Copilot)).ToList();
        var agent = new AgentActivity(Tool.Copilot, "p", Now.AddMinutes(-20), Now.AddHours(-1), "gpt-6.1-sol", TurnEndedUtc: Now.AddMinutes(-15), SubAgents: subs);
        _island.Update([agent], false);

        var compact = ViewModel().Build();
        Assert.Equal(("5:00", IslandViewModel.MaxDots), (compact.MainText, compact.SubAgentDots));

        _island.Click();
        Assert.Equal("5:00", Assert.Single(ViewModel().Build().Rows).TaskTimer);

        // When it leaves, its "done" shows the time up to its own turn end.
        _island.Escape();
        _island.Update([], false);
        Assert.Equal(TimeSpan.FromMinutes(5), _island.Finished!.Duration);
    }

    [Fact]
    public void A_Codex_agents_session_tokens_include_its_sub_agents_own_sessions()
    {
        Use(Tool.Codex, "parent", 1_000_000);
        Use(Tool.Codex, "child", 250_000);
        var agent = new AgentActivity(Tool.Codex, "parent", Now.AddSeconds(-90), Now.AddHours(-1), "gpt-6.1-sol", SubAgents:
        [
            Sub("child", "Zeno", "moss final qa", "gpt-6.1-sol", 250_000, 1, 40, own: true, tool: Tool.Codex),
            Sub("missing", "Ada", null, null, 0, 1, 20, own: true, tool: Tool.Codex),
        ]);
        _island.Update([agent], false);
        Assert.Equal("1.3M", ViewModel().Build().MainExtra);
    }
}
