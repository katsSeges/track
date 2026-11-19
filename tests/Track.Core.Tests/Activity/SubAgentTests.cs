using System.IO.Abstractions.TestingHelpers;
using Track.Core.Activity;
using Track.Core.Ingest;

namespace Track.Core.Tests.Activity;

/// <summary>Round 6: sub-agents from each tool's log, their state, nesting and run time. Every value is hand-computed.</summary>
public class SubAgentTests
{
    private const string T = "\"timestamp\":\"2026-11-19T10:00:00Z\"";
    private static readonly DateTimeOffset At = new(2026, 11, 19, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T0 = At;

    private readonly AgentActivityTracker _tracker = new();

    private static ActivityEvent Sub(ActivityKind kind, double seconds, SubAgent sub, Tool tool = Tool.Claude, string session = "s") =>
        new(tool, session, kind, T0.AddSeconds(seconds), Sub: sub);

    private void Apply(params ActivityEvent[] events) => events.ToList().ForEach(_tracker.Apply);

    private void Main(ActivityKind kind, double seconds, Tool tool = Tool.Claude, string session = "s") =>
        _tracker.Apply(new ActivityEvent(tool, session, kind, T0.AddSeconds(seconds)));

    private IReadOnlyList<AgentActivity> WorkingAt(double seconds, bool processAlive = true, Dictionary<(Tool, string), DateTimeOffset>? writes = null) =>
        _tracker.Working(T0.AddSeconds(seconds), writes ?? [], _ => processAlive);

    // ---------------- Claude Code ----------------

    [Fact]
    public void Claude_sub_agent_lines_are_the_sub_agents_and_never_start_or_end_the_parents_turn()
    {
        var p = new ClaudeActivityParser("agent-a1");
        string Line(string body) => "{" + body + "," + T + ",\"sessionId\":\"parent\",\"isSidechain\":true,\"agentId\":\"a1\"}";

        Assert.Equal(
            [new ActivityEvent(Tool.Claude, "parent", ActivityKind.SubAgentStart, At, Sub: new SubAgent("a1"))],
            p.Parse(Line("\"type\":\"user\",\"message\":{\"content\":\"Find the hit test\"}")));

        // One message's usage repeats on each content line: counted once. Model, and the Agent tool call it makes.
        var assistant = Line("\"type\":\"assistant\",\"message\":{\"id\":\"m1\",\"model\":\"claude-haiku-5-5\",\"stop_reason\":\"tool_use\"," +
                             "\"usage\":{\"input_tokens\":10,\"cache_creation_input_tokens\":200,\"cache_read_input_tokens\":3000,\"output_tokens\":40}," +
                             "\"content\":[{\"type\":\"tool_use\",\"id\":\"toolu_child\",\"name\":\"Agent\"},{\"type\":\"tool_use\",\"id\":\"x\",\"name\":\"Bash\"}]}");
        Assert.Equal(
            [
                new ActivityEvent(Tool.Claude, "parent", ActivityKind.SubAgentInfo, At, Sub: new SubAgent("a1", Model: "claude-haiku-5-5")),
                new ActivityEvent(Tool.Claude, "parent", ActivityKind.SubAgentTokens, At, Sub: new SubAgent("a1", Tokens: 3250)),
                new ActivityEvent(Tool.Claude, "parent", ActivityKind.SubAgentLaunch, At, Sub: new SubAgent("a1", LaunchId: "toolu_child")),
            ],
            p.Parse(assistant));
        Assert.Equal([ActivityKind.SubAgentInfo, ActivityKind.SubAgentLaunch], p.Parse(assistant).Select(e => e.Kind));

        // Without a message id each line counts; no usage and a synthetic model add nothing; content may be missing.
        Assert.Equal([ActivityKind.SubAgentTokens], p.Parse(Line("\"type\":\"assistant\",\"message\":{\"model\":\"<synthetic>\",\"usage\":{\"output_tokens\":5}}")).Select(e => e.Kind));
        Assert.Empty(p.Parse(Line("\"type\":\"assistant\",\"message\":{\"content\":\"text\"}")));

        // Tool results are not prompts; the end of its turn ends the sub-agent; an interrupt ends it too.
        Assert.Empty(p.Parse(Line("\"type\":\"user\",\"message\":{\"content\":[{\"type\":\"tool_result\"}]}")));
        Assert.Equal([ActivityKind.SubAgentEnd], p.Parse(Line("\"type\":\"assistant\",\"message\":{\"id\":\"m2\",\"stop_reason\":\"end_turn\"}")).Select(e => e.Kind));
        Assert.Equal([ActivityKind.SubAgentEnd], p.Parse(Line("\"type\":\"user\",\"message\":{\"content\":\"[Request interrupted by user]\"}")).Select(e => e.Kind));
        Assert.Empty(p.Parse(Line("\"type\":\"system\",\"subtype\":\"stop_hook_summary\"")));
    }

    [Fact]
    public void A_sidechain_line_without_an_agent_id_is_read_as_before()
    {
        var p = new ClaudeActivityParser("f");
        Assert.Equal(
            [ActivityKind.SessionStart, ActivityKind.TurnStart],
            p.Parse("{\"type\":\"user\"," + T + ",\"sessionId\":\"s\",\"isSidechain\":true,\"message\":{\"content\":\"go\"}}").Select(e => e.Kind));
    }

    // ---------------- Copilot ----------------

    [Fact]
    public void Copilot_sub_agents_start_finish_resume_use_tokens_and_nest_through_task_calls()
    {
        var p = new CopilotActivityParser("folder");
        string Line(string type, string agent, string data) => "{\"type\":\"" + type + "\"," + T + ",\"agentId\":\"" + agent + "\",\"data\":{" + data + "}}";
        ActivityEvent E(ActivityKind kind, SubAgent sub) => new(Tool.Copilot, "folder", kind, At, Sub: sub);

        // The main agent's own task call (no agentId) does not make anyone a parent.
        Assert.Empty(p.Parse("{\"type\":\"tool.execution_start\"," + T + ",\"data\":{\"toolName\":\"task\",\"toolCallId\":\"c0\"}}"));
        Assert.Equal(
            [E(ActivityKind.SubAgentStart, new SubAgent("qa", Name: "QA", Task: "Dedicated QA specialist", Model: "gpt-6.1-sol"))],
            p.Parse(Line("subagent.started", "qa", "\"toolCallId\":\"c0\",\"agentName\":\"qa-agent\",\"agentDisplayName\":\"QA\",\"agentDescription\":\"Dedicated QA specialist\",\"model\":\"gpt-6.1-sol\"")));

        // QA starts its own sub-agent through a task call: QA is its parent. No display name: the agent name.
        Assert.Empty(p.Parse(Line("tool.execution_start", "qa", "\"toolName\":\"task\",\"toolCallId\":\"c1\"")));
        Assert.Equal(
            [E(ActivityKind.SubAgentStart, new SubAgent("child", Name: "helper", ParentId: "qa"))],
            p.Parse(Line("subagent.started", "child", "\"toolCallId\":\"c1\",\"agentName\":\"helper\"")));
        Assert.Equal([E(ActivityKind.SubAgentStart, new SubAgent("x"))], p.Parse(Line("subagent.started", "x", "")));

        Assert.Equal([E(ActivityKind.SubAgentTokens, new SubAgent("qa", Tokens: 1200))], p.Parse(Line("session.usage_record", "qa", "\"usage\":{\"model\":\"gpt-6.1-sol\",\"inputTokens\":1000,\"outputTokens\":200,\"cacheReadTokens\":900}")));
        Assert.Equal([E(ActivityKind.SubAgentStart, new SubAgent("qa"))], p.Parse(Line("assistant.turn_start", "qa", "")));
        Assert.Equal([E(ActivityKind.SubAgentStepEnd, new SubAgent("qa"))], p.Parse(Line("assistant.turn_end", "qa", "")));
        Assert.Equal([E(ActivityKind.SubAgentEnd, new SubAgent("qa"))], p.Parse(Line("subagent.completed", "qa", "\"totalTokens\":1200")));

        // The main agent writes to a finished sub-agent: the main task goes on, and the sub-agent runs again.
        Assert.Equal(
            [new ActivityEvent(Tool.Copilot, "folder", ActivityKind.StepStart, At), E(ActivityKind.SubAgentStart, new SubAgent("qa"))],
            p.Parse(Line("user.message", "qa", "\"delivery\":\"idle\"")));

        // A sub-agent asking you something still waits for you; its other tools and lines are ignored.
        Assert.Equal([new ActivityEvent(Tool.Copilot, "folder", ActivityKind.WaitStart, At)], p.Parse(Line("tool.execution_start", "qa", "\"toolName\":\"ask_user\"")));
        Assert.Empty(p.Parse(Line("tool.execution_start", "qa", "\"toolName\":\"task\"")));
        Assert.Empty(p.Parse(Line("session.compaction_start", "qa", "")));
    }

    // ---------------- Codex ----------------

    [Fact]
    public void A_spawned_Codex_thread_reports_to_its_parent_thread_as_a_sub_agent()
    {
        var p = new CodexActivityParser("rollout-child");
        ActivityEvent E(ActivityKind kind, SubAgent sub) => new(Tool.Codex, "parent-thread", kind, At, Sub: sub);

        Assert.Equal(
            [E(ActivityKind.SubAgentInfo, new SubAgent("child-thread", Name: "Zeno", Task: "moss final qa", OwnSession: true))],
            p.Parse("{" + T + ",\"type\":\"session_meta\",\"payload\":{\"id\":\"child-thread\",\"source\":{\"subagent\":{\"thread_spawn\":{\"parent_thread_id\":\"parent-thread\",\"depth\":1,\"agent_path\":\"/root/moss_final_qa\",\"agent_nickname\":\"Zeno\"}}}}}"));
        Assert.Equal([E(ActivityKind.SubAgentInfo, new SubAgent("child-thread", Model: "gpt-6.1-sol"))], p.Parse("{" + T + ",\"type\":\"turn_context\",\"payload\":{\"model\":\"gpt-6.1-sol\"}}"));
        Assert.Equal([E(ActivityKind.SubAgentStart, new SubAgent("child-thread"))], p.Parse("{" + T + ",\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\"}}"));

        // Token totals are cumulative: deltas 500, 0 (nothing), 300; a smaller total restarts the count (200).
        string Tokens(long total) => "{" + T + ",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"info\":{\"total_token_usage\":{\"total_tokens\":" + total + "}}}}";
        Assert.Equal([E(ActivityKind.SubAgentTokens, new SubAgent("child-thread", Tokens: 500))], p.Parse(Tokens(500)));
        Assert.Empty(p.Parse(Tokens(500)));
        Assert.Equal(300, p.Parse(Tokens(800)).Single().Sub!.Tokens);
        Assert.Equal(200, p.Parse(Tokens(200)).Single().Sub!.Tokens);
        Assert.Empty(p.Parse("{" + T + ",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"info\":null}}"));

        Assert.Equal([E(ActivityKind.SubAgentEnd, new SubAgent("child-thread"))], p.Parse("{" + T + ",\"type\":\"event_msg\",\"payload\":{\"type\":\"task_complete\"}}"));
        Assert.Equal([ActivityKind.SubAgentEnd], p.Parse("{" + T + ",\"type\":\"event_msg\",\"payload\":{\"type\":\"turn_aborted\"}}").Select(e => e.Kind));
        Assert.Empty(p.Parse("{" + T + ",\"type\":\"event_msg\",\"payload\":{\"type\":\"agent_message\",\"task_started\":1}}"));
        Assert.Empty(p.Parse("{" + T + ",\"type\":\"response_item\",\"payload\":{\"type\":\"task_started\"}}"));
    }

    [Fact]
    public void A_Codex_sub_agent_without_a_path_has_no_task_and_a_main_thread_ignores_token_counts()
    {
        var child = new CodexActivityParser("f");
        Assert.Null(child.Parse("{" + T + ",\"type\":\"session_meta\",\"payload\":{\"id\":\"c\",\"source\":{\"subagent\":{\"thread_spawn\":{\"parent_thread_id\":\"p\"}}}}}").Single().Sub!.Task);

        var main = new CodexActivityParser("f");
        Assert.Equal([ActivityKind.SessionStart], main.Parse("{" + T + ",\"type\":\"session_meta\",\"payload\":{\"id\":\"m\",\"source\":\"cli\"}}").Select(e => e.Kind));
        Assert.Empty(main.Parse("{" + T + ",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"info\":{}}}"));
    }

    // ---------------- tracker ----------------

    [Fact]
    public void A_sub_agent_runs_from_its_start_uses_tokens_and_shows_done_with_its_run_time_until_the_agent_leaves()
    {
        Main(ActivityKind.TurnStart, 0);
        Apply(
            Sub(ActivityKind.SubAgentInfo, 5, new SubAgent("a1", Name: "Explore", Task: "Find the hit test", Model: "claude-haiku-5-5")),
            Sub(ActivityKind.SubAgentStart, 10, new SubAgent("a1")),
            Sub(ActivityKind.SubAgentTokens, 20, new SubAgent("a1", Tokens: 1000)),
            Sub(ActivityKind.SubAgentTokens, 30, new SubAgent("a1", Tokens: 250)));

        var running = Assert.Single(Assert.Single(WorkingAt(60)).Subs);
        Assert.Equal(new SubAgentActivity(Tool.Claude, "a1", "Explore", "Find the hit test", "claude-haiku-5-5", 1, T0.AddSeconds(10), null, 1250), running);
        Assert.True(running.Running);

        Apply(Sub(ActivityKind.SubAgentEnd, 255, new SubAgent("a1")), Sub(ActivityKind.SubAgentEnd, 300, new SubAgent("a1")));
        var done = Assert.Single(Assert.Single(WorkingAt(400)).Subs);
        Assert.Equal((T0.AddSeconds(255), false), (done.EndedUtc, done.Running)); // the first end counts

        // The agent's turn ends: it leaves, and its finished sub-agents with it.
        Main(ActivityKind.TurnEnd, 500);
        Assert.Empty(WorkingAt(520));
        Main(ActivityKind.TurnStart, 600);
        Assert.Null(Assert.Single(WorkingAt(610)).SubAgents);
    }

    [Fact]
    public void A_new_task_clears_the_last_tasks_finished_sub_agents_but_keeps_running_ones()
    {
        Main(ActivityKind.TurnStart, 0);
        Apply(Sub(ActivityKind.SubAgentStart, 1, new SubAgent("old")), Sub(ActivityKind.SubAgentEnd, 2, new SubAgent("old")), Sub(ActivityKind.SubAgentStart, 3, new SubAgent("bg")));
        Main(ActivityKind.TurnStart, 10);

        Assert.Equal(["bg"], Assert.Single(WorkingAt(20)).Subs.Select(s => s.Id));
    }

    [Fact]
    public void Background_sub_agents_keep_their_agent_with_its_timer_stopped_at_its_own_turn_end()
    {
        Main(ActivityKind.TurnStart, 0);
        Apply(Sub(ActivityKind.SubAgentStart, 30, new SubAgent("bg", Name: "general-purpose")));
        Main(ActivityKind.TurnEnd, 90);

        var agent = Assert.Single(WorkingAt(300));
        Assert.Equal((T0, (DateTimeOffset?)T0.AddSeconds(90), true), (agent.TurnStartedUtc, agent.TurnEndedUtc, agent.Subs[0].Running));

        // The sub-agent's own writes do not keep the agent "still being written" once it is done.
        Apply(Sub(ActivityKind.SubAgentEnd, 400, new SubAgent("bg")));
        Assert.Empty(WorkingAt(410, writes: new() { [(Tool.Claude, "s")] = T0.AddSeconds(30) }));
    }

    [Fact]
    public void A_session_whose_turn_was_never_seen_takes_its_first_sub_agents_start()
    {
        Apply(Sub(ActivityKind.SubAgentStart, 40, new SubAgent("late")), Sub(ActivityKind.SubAgentStart, 20, new SubAgent("early")));

        var agent = Assert.Single(WorkingAt(60));
        Assert.Equal((T0.AddSeconds(20), (DateTimeOffset?)T0.AddSeconds(20)), (agent.TurnStartedUtc, agent.TurnEndedUtc));
        Assert.Equal(["early", "late"], agent.Subs.Select(s => s.Id));
    }

    [Fact]
    public void A_step_end_finishes_a_resumed_sub_agent_after_the_grace_and_more_work_restarts_its_timer()
    {
        Main(ActivityKind.TurnStart, 0);
        Apply(Sub(ActivityKind.SubAgentStart, 10, new SubAgent("qa")), Sub(ActivityKind.SubAgentStepEnd, 100, new SubAgent("qa")));
        Assert.True(Assert.Single(WorkingAt(110)).Subs[0].Running); // within the grace

        // A new step within the grace: the same run goes on.
        Apply(Sub(ActivityKind.SubAgentStart, 105, new SubAgent("qa")));
        Assert.Equal(T0.AddSeconds(10), Assert.Single(WorkingAt(120)).Subs[0].StartedUtc);

        Apply(Sub(ActivityKind.SubAgentStepEnd, 200, new SubAgent("qa")));
        Assert.Equal(T0.AddSeconds(200), Assert.Single(WorkingAt(215)).Subs[0].EndedUtc);

        // Later work (the main agent writes to it): a new run, its timer from there.
        Apply(Sub(ActivityKind.SubAgentStart, 300, new SubAgent("qa")));
        Assert.Equal((T0.AddSeconds(300), true), (Assert.Single(WorkingAt(310)).Subs[0].StartedUtc, Assert.Single(WorkingAt(310)).Subs[0].Running));

        // A finished one given more work restarts too; a step end after the end changes nothing.
        Apply(Sub(ActivityKind.SubAgentEnd, 320, new SubAgent("qa")), Sub(ActivityKind.SubAgentStepEnd, 321, new SubAgent("qa")), Sub(ActivityKind.SubAgentStart, 330, new SubAgent("qa")));
        Assert.Equal((T0.AddSeconds(330), (DateTimeOffset?)null), (Assert.Single(WorkingAt(340)).Subs[0].StartedUtc, Assert.Single(WorkingAt(340)).Subs[0].EndedUtc));
    }

    [Fact]
    public void A_silent_sub_agent_counts_as_ended_at_its_last_line()
    {
        Main(ActivityKind.TurnStart, 0);
        Apply(Sub(ActivityKind.SubAgentStart, 10, new SubAgent("x")), Sub(ActivityKind.SubAgentTokens, 60, new SubAgent("x", Tokens: 5)));

        // The agent itself keeps writing its log, so it stays; only the sub-agent went silent.
        var at = 60 + AgentActivityTracker.StaleAfter.TotalSeconds;
        Dictionary<(Tool, string), DateTimeOffset> Writes(double seconds) => new() { [(Tool.Claude, "s")] = T0.AddSeconds(seconds - 5) };
        Assert.True(Assert.Single(WorkingAt(at - 1, processAlive: false, Writes(at - 1))).Subs[0].Running);
        Assert.Equal(T0.AddSeconds(60), Assert.Single(WorkingAt(at, processAlive: false, Writes(at))).Subs[0].EndedUtc);
        Assert.True(Assert.Single(WorkingAt(at, processAlive: true, Writes(at))).Subs[0].Running);
        var dead = 60 + AgentActivityTracker.DeadAfter.TotalSeconds;
        Assert.Equal(T0.AddSeconds(60), Assert.Single(WorkingAt(dead, processAlive: true, Writes(dead))).Subs[0].EndedUtc);
    }

    [Fact]
    public void Facts_alone_do_not_show_a_sub_agent_until_it_starts()
    {
        Main(ActivityKind.TurnStart, 0);
        Apply(Sub(ActivityKind.SubAgentInfo, 5, new SubAgent("c", Name: "Zeno", OwnSession: true)), Sub(ActivityKind.SubAgentTokens, 6, new SubAgent("c", Tokens: 9)));

        Assert.Null(Assert.Single(WorkingAt(10)).SubAgents);
    }

    [Fact]
    public void Nested_sub_agents_sit_right_under_their_parent_one_level_deeper()
    {
        Main(ActivityKind.TurnStart, 0);
        Apply(
            // Claude: a1 launches toolu_b through its Agent call; b's meta names that call.
            Sub(ActivityKind.SubAgentStart, 10, new SubAgent("a1")),
            Sub(ActivityKind.SubAgentLaunch, 15, new SubAgent("a1", LaunchId: "toolu_b")),
            Sub(ActivityKind.SubAgentStart, 20, new SubAgent("b")),
            Sub(ActivityKind.SubAgentInfo, 20, new SubAgent("b", LaunchId: "toolu_b")),
            // Copilot style: a named parent; and one whose parent is unknown here shows at the top level.
            Sub(ActivityKind.SubAgentStart, 25, new SubAgent("c", ParentId: "b")),
            Sub(ActivityKind.SubAgentStart, 12, new SubAgent("a2")),
            Sub(ActivityKind.SubAgentStart, 30, new SubAgent("orphan", ParentId: "gone")),
            // A main-agent launch (no launcher recorded) stays at the top.
            Sub(ActivityKind.SubAgentStart, 35, new SubAgent("d")),
            Sub(ActivityKind.SubAgentInfo, 35, new SubAgent("d", LaunchId: "toolu_main")));

        Assert.Equal(
            [("a1", 1), ("b", 2), ("c", 3), ("a2", 1), ("orphan", 1), ("d", 1)],
            Assert.Single(WorkingAt(40)).Subs.Select(s => (s.Id, s.Depth)));
    }

    [Fact]
    public void A_Codex_sub_agents_own_sub_agents_are_attached_under_it()
    {
        Main(ActivityKind.TurnStart, 0, Tool.Codex, "parent");
        Apply(
            Sub(ActivityKind.SubAgentInfo, 5, new SubAgent("child", Name: "Zeno", OwnSession: true), Tool.Codex, "parent"),
            Sub(ActivityKind.SubAgentStart, 6, new SubAgent("child"), Tool.Codex, "parent"),
            // The grandchild reports to its parent thread: the child's id.
            Sub(ActivityKind.SubAgentInfo, 7, new SubAgent("grandchild", Name: "Ada", OwnSession: true), Tool.Codex, "child"),
            Sub(ActivityKind.SubAgentStart, 8, new SubAgent("grandchild"), Tool.Codex, "child"));

        var agent = Assert.Single(WorkingAt(20));
        Assert.Equal([("child", 1, true), ("grandchild", 2, true)], agent.Subs.Select(s => (s.Id, s.Depth, s.OwnSession)));
        Assert.Equal("parent", agent.SessionId); // the child thread is not an agent of its own
    }

    // ---------------- reader ----------------

    private const string ParentLine = "{\"type\":\"user\",\"timestamp\":\"2026-11-19T09:59:00Z\",\"sessionId\":\"sess\",\"message\":{\"content\":\"go\"}}\n";
    private const string SubLine = "{\"type\":\"user\",\"timestamp\":\"2026-11-19T09:59:30Z\",\"sessionId\":\"sess\",\"isSidechain\":true,\"agentId\":\"a1\",\"message\":{\"content\":\"look\"}}\n";

    [Fact]
    public void The_reader_names_Claude_sub_agents_from_their_meta_file_and_keeps_their_writes_apart()
    {
        var fs = new MockFileSystem();
        var now = new DateTimeOffset(2026, 11, 19, 10, 0, 0, TimeSpan.Zero);
        fs.AddFile(@"C:\home\.claude\projects\p\sess.jsonl", new MockFileData(ParentLine) { LastWriteTime = now.AddSeconds(-60).UtcDateTime });
        fs.AddFile(@"C:\home\.claude\projects\p\sess\subagents\agent-a1.jsonl", new MockFileData(SubLine) { LastWriteTime = now.AddSeconds(-5).UtcDateTime });
        var reader = new ActivityReader(fs, new LogScanner(fs, LogSources.Default(@"C:\home")), new AgentActivityTracker());

        // No meta file yet: the sub-agent runs without a name, and its write is not the parent's.
        var writes = reader.Refresh(now);
        Assert.Equal(now.AddSeconds(-60), writes[(Tool.Claude, "sess")]);
        Assert.Null(Assert.Single(Assert.Single(reader.Tracker.Working(now, writes, _ => true)).Subs).Name);

        fs.AddFile(@"C:\home\.claude\projects\p\sess\subagents\agent-a1.meta.json", new MockFileData(
            "{\"agentType\":\"Explore\",\"description\":\"Find the hit test\",\"pickedEffortModel\":\"claude-haiku-5-5\",\"toolUseId\":\"toolu_1\",\"spawnDepth\":1,\"finished\":false}"));
        writes = reader.Refresh(now);
        var sub = Assert.Single(Assert.Single(reader.Tracker.Working(now, writes, _ => true)).Subs);
        Assert.Equal(("Explore", "Find the hit test", "claude-haiku-5-5"), (sub.Name, sub.Task, sub.Model));
    }

    [Fact]
    public void Only_agent_files_in_a_subagents_folder_have_meta_files_and_a_locked_meta_file_is_tried_again()
    {
        // An "agent-" log elsewhere is a normal session log.
        var fs = new MockFileSystem();
        var now = new DateTimeOffset(2026, 11, 19, 10, 0, 0, TimeSpan.Zero);
        fs.AddFile(@"C:\home\.claude\projects\p\agent-x.jsonl", new MockFileData(ParentLine) { LastWriteTime = now.UtcDateTime });
        fs.AddFile(@"C:\home\.claude\projects\p\agent-x.meta.json", new MockFileData("{\"agentType\":\"Explore\"}"));
        var plain = new ActivityReader(fs, new LogScanner(fs, LogSources.Default(@"C:\home")), new AgentActivityTracker());
        Assert.Null(Assert.Single(plain.Tracker.Working(now, plain.Refresh(now), _ => true)).SubAgents);

        // The meta file is being written (locked): no name this time, read on a later refresh.
        var fs2 = new MockFileSystem();
        fs2.AddFile(@"C:\h\.claude\projects\p\sess\subagents\agent-a1.jsonl", new MockFileData(SubLine) { LastWriteTime = now.UtcDateTime });
        var meta = new MockFileData("{\"agentType\":\"Explore\"}") { AllowedFileShare = FileShare.None };
        fs2.AddFile(@"C:\h\.claude\projects\p\sess\subagents\agent-a1.meta.json", meta);
        var reader = new ActivityReader(fs2, new LogScanner(fs2, LogSources.Default(@"C:\h")), new AgentActivityTracker());
        Assert.Null(Assert.Single(Assert.Single(reader.Tracker.Working(now, reader.Refresh(now), _ => true)).Subs).Name);
        meta.AllowedFileShare = FileShare.ReadWrite;
        Assert.Equal("Explore", Assert.Single(Assert.Single(reader.Tracker.Working(now, reader.Refresh(now), _ => true)).Subs).Name);
    }

    // ---------------- tray ----------------

    [Fact]
    public void The_tray_counts_running_sub_agents_of_working_sessions()
    {
        var live = new Dictionary<Tool, Track.Core.Live.LiveStatus> { [Tool.Claude] = new(Tool.Claude, false, 0, 0, true, null, null) };
        SubAgentActivity S(string id, bool running) => new(Tool.Claude, id, null, null, null, 1, T0, running ? null : T0, 0);
        var agent = new AgentActivity(Tool.Claude, "s", T0, T0, null, SubAgents: [S("a", true), S("b", true), S("c", false)]);

        var shared = Track.Core.Live.LiveStatusDetector.WithAgents(live, [agent, new AgentActivity(Tool.Claude, "s2", T0, T0, null)]);

        Assert.Equal((true, 2, 2), (shared[Tool.Claude].Working, shared[Tool.Claude].ActiveSessions, shared[Tool.Claude].SubAgents));
    }

    [Fact]
    public void A_Codex_path_without_a_name_gives_no_task_and_a_repeated_start_keeps_the_run()
    {
        var child = new CodexActivityParser("f");
        Assert.Null(child.Parse("{" + T + ",\"type\":\"session_meta\",\"payload\":{\"id\":\"c\",\"source\":{\"subagent\":{\"thread_spawn\":{\"parent_thread_id\":\"p\",\"agent_path\":\"/\"}}}}}").Single().Sub!.Task);

        Main(ActivityKind.TurnStart, 0);
        Apply(Sub(ActivityKind.SubAgentStart, 10, new SubAgent("qa")), Sub(ActivityKind.SubAgentStart, 50, new SubAgent("qa")));
        Assert.Equal(T0.AddSeconds(10), Assert.Single(WorkingAt(60)).Subs[0].StartedUtc);
    }

    [Fact]
    public void A_Claude_sub_agent_line_with_only_an_agent_id_is_the_sub_agents()
    {
        // Its first line (api-request-shape) has an agentId but no isSidechain flag.
        var p = new ClaudeActivityParser("agent-a1");
        Assert.Empty(p.Parse("{\"type\":\"api-request-shape\",\"sessionId\":\"parent\",\"agentId\":\"a1\"," + T + "}"));
        Assert.Equal([ActivityKind.SubAgentStart], p.Parse("{\"type\":\"user\",\"sessionId\":\"parent\",\"agentId\":\"a1\"," + T + ",\"message\":{\"content\":\"go\"}}").Select(e => e.Kind));
    }

    [Fact]
    public void A_forked_Codex_sub_agent_skips_the_copy_of_its_parents_history()
    {
        var p = new CodexActivityParser("rollout-child");
        string Line(string type, string payload) => "{" + T + ",\"type\":\"" + type + "\",\"payload\":" + payload + "}";

        Assert.Single(p.Parse(Line("session_meta", "{\"id\":\"child\",\"source\":{\"subagent\":{\"thread_spawn\":{\"parent_thread_id\":\"parent\",\"agent_nickname\":\"Zeno\"}}}}")));
        // The copy: the parent's header, its running turn and its counters — none of it is the sub-agent's.
        Assert.Empty(p.Parse(Line("session_meta", "{\"id\":\"parent\"}")));
        Assert.Empty(p.Parse(Line("event_msg", "{\"type\":\"task_started\",\"turn_id\":\"parent-turn\"}")));
        Assert.Empty(p.Parse(Line("event_msg", "{\"type\":\"token_count\",\"info\":{\"total_token_usage\":{\"total_tokens\":5000000}}}")));
        Assert.Empty(p.Parse(Line("event_msg", "{\"type\":\"thread_settings_applied\"}")));

        // Its own work, under its own id.
        var start = Assert.Single(p.Parse(Line("event_msg", "{\"type\":\"task_started\",\"turn_id\":\"child-turn\"}")));
        Assert.Equal(("parent", ActivityKind.SubAgentStart, "child"), (start.SessionId, start.Kind, start.Sub!.Id));
        Assert.Equal(27528, p.Parse(Line("event_msg", "{\"type\":\"token_count\",\"info\":{\"total_token_usage\":{\"total_tokens\":27528}}}")).Single().Sub!.Tokens);
    }

    [Fact]
    public void A_Claude_sub_agent_ends_when_its_meta_file_says_finished_and_again_after_more_work()
    {
        var fs = new MockFileSystem();
        var now = new DateTimeOffset(2026, 11, 19, 10, 0, 0, TimeSpan.Zero);
        const string log = @"C:\home\.claude\projects\p\sess\subagents\agent-a1.jsonl";
        const string metaPath = @"C:\home\.claude\projects\p\sess\subagents\agent-a1.meta.json";
        fs.AddFile(@"C:\home\.claude\projects\p\sess.jsonl", new MockFileData(ParentLine) { LastWriteTime = now.AddSeconds(-60).UtcDateTime });
        fs.AddFile(log, new MockFileData(SubLine) { LastWriteTime = now.AddSeconds(-20).UtcDateTime });
        fs.AddFile(metaPath, new MockFileData("{\"agentType\":\"Explore\",\"finished\":true}") { LastWriteTime = now.AddSeconds(-19).UtcDateTime });
        var reader = new ActivityReader(fs, new LogScanner(fs, LogSources.Default(@"C:\home")), new AgentActivityTracker());
        SubAgentActivity Sub() => Assert.Single(Assert.Single(reader.Tracker.Working(now, reader.Refresh(now), _ => true)).Subs);

        // Finished: it ended at its log's last write.
        Assert.Equal(now.AddSeconds(-20), Sub().EndedUtc);

        // Given more work: a new prompt in its log, the meta file says not finished (rewritten) — running again.
        fs.File.AppendAllText(log, SubLine.Replace("09:59:30", "09:59:50"));
        fs.File.SetLastWriteTimeUtc(log, now.AddSeconds(-5).UtcDateTime);
        fs.File.WriteAllText(metaPath, "{\"agentType\":\"Explore\",\"finished\":false}");
        fs.File.SetLastWriteTimeUtc(metaPath, now.AddSeconds(-4).UtcDateTime);
        Assert.True(Sub().Running);

        // An unchanged meta file is not read again (no second "finished" from old news).
        Assert.True(Sub().Running);
    }
}
