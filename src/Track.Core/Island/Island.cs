using Track.Core.Activity;
using Track.Core.Display;
using Track.Core.Ingest;
using Track.Core.Usage;
using Track.Core.Providers;

namespace Track.Core.Island;

public enum IslandTimerFrom
{
    Turn,
    Session,
}

public enum IslandCompact
{
    Timer,
    Tokens,
    Model,
}

public enum IslandActivity
{
    Wave,
    Dot,
    None,
}

public enum IslandMulti
{
    Split,
    Merged,
    Cycle,
}

public enum IslandExpand
{
    Hover,
    Click,
}

public enum IslandExpanded
{
    Agents,
    Full,
}

public enum IslandScreen
{
    Primary,
    Mouse,
    All,
}

/// <summary>Round 4 choices (design/track-mockup-v4.html), chosen 2026-11-18.</summary>
public sealed record IslandSettings
{
    public bool Enabled { get; set; } = true;

    public IslandTimerFrom TimerFrom { get; set; } = IslandTimerFrom.Turn;

    public IslandCompact Compact { get; set; } = IslandCompact.Tokens;

    public IslandActivity Activity { get; set; } = IslandActivity.Wave;

    public IslandMulti Multi { get; set; } = IslandMulti.Split;

    public bool ShowDone { get; set; } = true;

    public int HideAfterMinutes { get; set; } = 5;

    public int RevealDelayMs { get; set; } = 600;

    public IslandExpand Expand { get; set; } = IslandExpand.Click;

    public IslandExpanded Expanded { get; set; } = IslandExpanded.Agents;

    public IslandScreen Screen { get; set; } = IslandScreen.Primary;

    public bool HideOverFullScreen { get; set; } = true;

    /// <summary>The opened island's tab (round 5): Now (running agents) or Usage. Remembered across opens and restarts.</summary>
    public IslandTab OpenTab { get; set; } = IslandTab.Now;
}

public enum IslandTab
{
    Now,
    Usage,
}

public enum IslandState
{
    Hidden,
    Idle,
    Active,
    Done,
    Revealed,
    Open,
}

/// <summary>A task that just finished, shown as "Claude Code done" for <see cref="IslandStateMachine.DoneFor"/>.</summary>
/// <param name="NeedsYou">It stopped to ask the user something ("Copilot needs you"), rather than finishing.</param>
public sealed record FinishedTask(Tool Tool, TimeSpan Duration, bool NeedsYou = false);

/// <summary>
/// When the island shows and in which form. Feed it the working agents (every few seconds), the pointer
/// (every 100 ms) and clicks; read <see cref="State"/>. Pure logic over a <see cref="TimeProvider"/>.
/// </summary>
public sealed class IslandStateMachine(TimeProvider time, Func<IslandSettings> settings)
{
    public static readonly TimeSpan DoneFor = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan HoverOpenDelay = TimeSpan.FromMilliseconds(180);

    private IReadOnlyList<AgentActivity> _working = [];
    private bool _fullScreen;
    private DateTimeOffset _lastActive = DateTimeOffset.MinValue;
    private DateTimeOffset _doneUntil = DateTimeOffset.MinValue;
    private DateTimeOffset? _topSince;
    private DateTimeOffset? _overSince;
    private bool _revealed;
    private bool _over;
    private bool _open;
    private bool _minimized;

    public IReadOnlyList<AgentActivity> Working => _working;

    public FinishedTask? Finished { get; private set; }

    public IslandState State
    {
        get
        {
            var s = settings();
            var now = time.GetUtcNow();
            if (!s.Enabled || (_fullScreen && s.HideOverFullScreen))
            {
                return IslandState.Hidden;
            }

            if (_open)
            {
                return IslandState.Open;
            }

            if (_working.Count > 0)
            {
                // Running agents keep the island up; only the minimize button tucks it away, and the
                // top edge then peeks it back (user review, 2026-11-18).
                return !_minimized || _revealed ? IslandState.Active : IslandState.Hidden;
            }

            if (now < _doneUntil)
            {
                return IslandState.Done;
            }

            if (now - _lastActive < TimeSpan.FromMinutes(s.HideAfterMinutes))
            {
                return IslandState.Idle;
            }

            return _revealed ? IslandState.Revealed : IslandState.Hidden;
        }
    }

    public bool IsVisible => State != IslandState.Hidden;

    /// <summary>True after the minimize button, until the user opens it again or all work ends.</summary>
    public bool IsMinimized => _minimized;

    /// <summary>New working agents and full-screen state (every live refresh).</summary>
    /// <param name="waiting">Sessions that stopped to wait for the user's answer (shown as "needs you", not "done").</param>
    public void Update(IReadOnlyList<AgentActivity> working, bool fullScreen, IReadOnlySet<(Tool Tool, string SessionId)>? waiting = null)
    {
        var now = time.GetUtcNow();
        var stillWorking = working.Select(w => (w.Tool, w.SessionId)).ToHashSet();
        var finished = _working.FirstOrDefault(w => !stillWorking.Contains((w.Tool, w.SessionId)));
        if (finished is not null && settings().ShowDone)
        {
            Finished = new FinishedTask(finished.Tool, (finished.TurnEndedUtc ?? now) - finished.TurnStartedUtc, waiting?.Contains((finished.Tool, finished.SessionId)) == true);
            _doneUntil = now + DoneFor;
        }

        if (working.Count > 0 || _working.Count > 0)
        {
            _lastActive = now;
        }

        if (working.Count == 0)
        {
            _minimized = false; // nothing running: back to the normal done / idle / hide flow
        }

        _working = working;
        _fullScreen = fullScreen;
        if (fullScreen)
        {
            _open = false;
        }
    }

    /// <summary>The pointer is (or is not) on the very top edge of the screen.</summary>
    public void PointerAtTop(bool atTop)
    {
        if (atTop)
        {
            _topSince ??= time.GetUtcNow();
            return;
        }

        _topSince = null;
        _revealed &= _over;
    }

    /// <summary>The pointer is (or is not) over the island itself.</summary>
    public void PointerOverIsland(bool over)
    {
        _over = over;
        if (over)
        {
            _overSince ??= time.GetUtcNow();
            return;
        }

        _overSince = null;
        _open &= settings().Expand == IslandExpand.Click;
        _revealed &= _topSince is not null;
    }

    /// <summary>A click on the island opens or closes it.</summary>
    public void Click()
    {
        if (IsVisible)
        {
            _open = !_open;
            _minimized &= !_open;
        }
    }

    /// <summary>The minimize button in the opened island: tuck it away while agents keep running.</summary>
    public void Minimize()
    {
        _open = false;
        _revealed = false;
        _minimized = _working.Count > 0;
    }

    public void Escape() => _open = false;

    /// <summary>Applies the reveal and hover delays; call every 100 ms.</summary>
    public void Tick()
    {
        var now = time.GetUtcNow();
        var s = settings();
        if (_topSince is { } top && now - top >= TimeSpan.FromMilliseconds(s.RevealDelayMs))
        {
            _revealed = true;
        }

        if (s.Expand == IslandExpand.Hover && _overSince is { } over && now - over >= HoverOpenDelay && IsVisible)
        {
            _open = true;
        }
    }
}

/// <summary>One agent row of the opened island, with its sub-agents (round 6) under it.</summary>
public sealed record IslandAgentRow(Tool Tool, string Name, string Detail, string TaskTimer, string SessionTimer, IReadOnlyList<IslandSubAgentRow>? SubAgents = null)
{
    public IReadOnlyList<IslandSubAgentRow> Subs => SubAgents ?? [];
}

/// <summary>
/// A sub-agent row under its agent (round 6): name, task, <c>model · 1.2M tokens</c> and its run time, or
/// <c>done 4:05</c> once finished (shown dimmed until its agent leaves). <paramref name="Depth"/> 1 sits under the
/// agent, 2 under a sub-agent, and so on.
/// </summary>
public sealed record IslandSubAgentRow(Tool Tool, string Name, string? Task, string? Meta, string Timer, bool Running, int Depth);

/// <summary>A tool line in the opened island's "full" table: today, week and month tokens.</summary>
public sealed record IslandTableRow(Tool Tool, string Name, string Today, string Week, string Month);

/// <summary>Everything the island window shows. Fields not used by <see cref="State"/> are empty.</summary>
public sealed record IslandContent(
    IslandState State,
    IslandActivity Activity,
    Tool? MainTool,
    string MainText,
    string? MainExtra,
    IReadOnlyList<Tool> Logos,
    IReadOnlyList<Tool> Bubble,
    string? Counter,
    string? Header,
    string? HeaderRight,
    IReadOnlyList<IslandAgentRow> Rows,
    IReadOnlyList<IslandTableRow> Table,
    string? Footer,
    bool CanMinimize = false,
    IslandTab Tab = IslandTab.Now,
    UsagePanelContent? Usage = null,
    bool NeedsYou = false,
    int SubAgentDots = 0);

/// <summary>Builds <see cref="IslandContent"/> from the state machine, usage and settings.</summary>
/// <param name="listed">Tools the usage table lists (the popover's visibility rule), so both show the same rows; all tools when null.</param>
public sealed class IslandViewModel(
    IslandStateMachine machine,
    UsageStore store,
    UsageAggregator usage,
    PeriodCalculator periods,
    TimeProvider time,
    Func<IslandSettings> settings,
    Func<IReadOnlyList<Tool>>? listed = null)
{
    public static readonly TimeSpan CycleEvery = TimeSpan.FromSeconds(4);

    private readonly UsagePanelBuilder _panel = new(store, usage, periods, time);
    private IslandTab? _tab;
    private DateTimeOffset? _selectedStartUtc;
    private ChartViewModel? _chart;

    /// <summary>Raised when the user switches the opened island's tab; the app saves it as <see cref="IslandSettings.OpenTab"/>.</summary>
    public event EventHandler<IslandTab>? TabChanged;

    public IslandTab Tab => _tab ?? settings().OpenTab;

    /// <summary>The Usage tab's own Today / Week / Month choice (independent of the popover).</summary>
    public Period Period { get; private set; } = Period.Today;

    public void SelectTab(IslandTab tab)
    {
        if (tab != Tab)
        {
            _tab = tab;
            _selectedStartUtc = null;
            TabChanged?.Invoke(this, tab);
        }
    }

    public void SelectPeriod(Period period)
    {
        Period = period;
        _selectedStartUtc = null;
    }

    /// <summary>Click on a bar of the Usage tab's chart: show only that hour or day; the same bar again goes back.</summary>
    public void SelectBucket(int index)
    {
        if (_chart is { } chart)
        {
            _selectedStartUtc = UsagePanelBuilder.Toggle(chart, index, _selectedStartUtc);
        }
    }

    public IslandContent Build()
    {
        var s = settings();
        var now = time.GetUtcNow();
        var state = machine.State;
        var working = machine.Working;
        var empty = new IslandContent(state, s.Activity, null, "", null, [], [], null, null, null, [], [], null);

        switch (state)
        {
            case IslandState.Open:
                return Open(empty, working, now, s);
            case IslandState.Active:
                return Compact(empty, working, now, s);
            case IslandState.Done:
                var done = machine.Finished!;
                return empty with
                {
                    MainTool = done.Tool,
                    MainText = $"{ToolNames.Display(done.Tool)} {(done.NeedsYou ? "needs you" : "done")}",
                    MainExtra = Format.Timer(done.Duration),
                    Logos = [done.Tool],
                    NeedsYou = done.NeedsYou,
                };
            case IslandState.Idle:
            case IslandState.Revealed:
                return empty with { MainText = "Idle", MainExtra = "today " + Format.Tokens(usage.Totals(null, periods.Range(Period.Today)).Tokens.Total) };
            default:
                return empty;
        }
    }

    private IslandContent Compact(IslandContent empty, IReadOnlyList<AgentActivity> working, DateTimeOffset now, IslandSettings s)
    {
        if (working.Count > 1 && s.Multi == IslandMulti.Cycle)
        {
            var index = (int)(now.ToUnixTimeSeconds() / (long)CycleEvery.TotalSeconds % working.Count);
            return Main(empty, working[index], now, s) with { Counter = $"{index + 1}/{working.Count}" };
        }

        var main = Main(empty, working[0], now, s);
        if (working.Count == 1)
        {
            return main;
        }

        return s.Multi == IslandMulti.Merged
            ? main with { Logos = working.Select(w => w.Tool).Distinct().ToList(), Counter = $"+{working.Count - 1}" }
            : main with { Bubble = working.Skip(1).Select(w => w.Tool).ToList() };
    }

    private IslandContent Main(IslandContent empty, AgentActivity agent, DateTimeOffset now, IslandSettings s) => empty with
    {
        MainTool = agent.Tool,
        Logos = [agent.Tool],
        MainText = Format.Timer(s.TimerFrom == IslandTimerFrom.Turn ? TaskTime(agent, now) : now - agent.SessionStartedUtc),
        SubAgentDots = Math.Min(MaxDots, agent.Subs.Count(x => x.Running)),
        MainExtra = s.Compact switch
        {
            IslandCompact.Tokens => Format.Tokens(SessionTokens(agent)),
            IslandCompact.Model => agent.Model,
            _ => null,
        },
    };

    private IslandContent Open(IslandContent empty, IReadOnlyList<AgentActivity> working, DateTimeOffset now, IslandSettings s)
    {
        var today = usage.Totals(null, periods.Range(Period.Today));
        var rows = working.Select(w => new IslandAgentRow(
            w.Tool,
            ToolNames.Display(w.Tool),
            $"{w.Model ?? "model unknown"} · {Format.Tokens(SessionTokens(w))} this session",
            Format.Timer(TaskTime(w, now)),
            "session " + Format.Timer(now - w.SessionStartedUtc),
            SubRows(w, now))).ToList();

        var opened = empty with
        {
            Header = working.Count switch
            {
                0 => "Nothing running",
                1 => "1 agent working",
                var n => $"{n} agents working",
            },
            HeaderRight = $"today {Format.Tokens(today.Tokens.Total)} · {Format.Cost(today)}",
            Rows = rows,
            CanMinimize = working.Count > 0,
            Tab = Tab,
            Usage = UsageTab(),
        };

        if (s.Expanded == IslandExpanded.Full)
        {
            string Tokens(Tool tool, Period p) => Format.Tokens(usage.Totals(tool, periods.Range(p)).Tokens.Total);
            return opened with
            {
                Table = (listed?.Invoke() ?? ProviderCatalog.LegacyOrder)
                    .Select(t => new IslandTableRow(t, ToolNames.Display(t), Tokens(t, Period.Today), Tokens(t, Period.Week), Tokens(t, Period.Month)))
                    .ToList(),
            };
        }

        var week = usage.Totals(null, periods.Range(Period.Week)).Tokens.Total;
        var month = usage.Totals(null, periods.Range(Period.Month)).Tokens.Total;
        return opened with { Footer = $"week {Format.Tokens(week)} · month {Format.Tokens(month)}" };
    }

    /// <summary>The Usage tab, built like the popover's table (the chart is always shown here; Hide is for the tray).</summary>
    private UsagePanelContent? UsageTab()
    {
        if (Tab != IslandTab.Usage)
        {
            _chart = null;
            return null;
        }

        var tools = listed?.Invoke() ?? ProviderCatalog.LegacyOrder;
        var content = _panel.Build(Period, tools, _selectedStartUtc, showChart: true);
        _selectedStartUtc = content.SelectedStartUtc;
        _chart = content.Chart;
        return content;
    }

    /// <summary>The compact pill shows one dot per running sub-agent, at most this many.</summary>
    public const int MaxDots = 4;

    /// <summary>The agent's task time; it stops when its own turn ended while sub-agents still run.</summary>
    private static TimeSpan TaskTime(AgentActivity agent, DateTimeOffset now) => (agent.TurnEndedUtc ?? now) - agent.TurnStartedUtc;

    private static IReadOnlyList<IslandSubAgentRow>? SubRows(AgentActivity agent, DateTimeOffset now)
    {
        if (agent.Subs.Count == 0)
        {
            return null;
        }

        return agent.Subs.Select(x =>
        {
            var meta = string.Join(" · ", new[] { x.Model, x.Tokens > 0 ? $"{Format.Tokens(x.Tokens)} tokens" : null }.OfType<string>());
            return new IslandSubAgentRow(
                x.Tool,
                x.Name ?? "sub-agent",
                x.Task,
                meta.Length > 0 ? meta : null,
                x.EndedUtc is { } end ? "done " + Format.Timer(end - x.StartedUtc) : Format.Timer(now - x.StartedUtc),
                x.Running,
                x.Depth);
        }).ToList();
    }

    /// <summary>The whole session's tokens: its own log, plus sub-agents with their own session (Codex), whose usage is kept apart.</summary>
    private long SessionTokens(AgentActivity agent) =>
        OwnSessionTokens(agent) + agent.Subs.Where(x => x.OwnSession).Sum(x => store.Sessions.TryGetValue(new SessionKey(agent.Tool, x.Id), out var sub) ? sub.Tokens.Total : 0);

    private long OwnSessionTokens(AgentActivity agent) =>
        store.Sessions.TryGetValue(new SessionKey(agent.Tool, agent.SessionId), out var session) ? session.Tokens.Total : 0;
}
