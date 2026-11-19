using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Track.Core.Activity;
using Track.Core.Ingest;

namespace Track.Core.Tests.Activity;

public class AgentActivityTrackerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 18, 10, 0, 0, TimeSpan.Zero);
    private static readonly Dictionary<(Tool, string), DateTimeOffset> NoWrites = [];

    private readonly AgentActivityTracker _tracker = new();

    private void Apply(ActivityKind kind, TimeSpan after, Tool tool = Tool.Claude, string session = "s", string? model = null) =>
        _tracker.Apply(new ActivityEvent(tool, session, kind, T0 + after, model));

    private IReadOnlyList<AgentActivity> WorkingAt(TimeSpan after, bool processAlive = true, Dictionary<(Tool, string), DateTimeOffset>? writes = null) =>
        _tracker.Working(T0 + after, writes ?? NoWrites, _ => processAlive);

    [Fact]
    public void A_prompt_starts_work_and_the_end_stops_it()
    {
        Apply(ActivityKind.SessionStart, TimeSpan.Zero);
        Apply(ActivityKind.ModelSeen, TimeSpan.FromSeconds(1), model: "claude-opus-5-5");
        Apply(ActivityKind.TurnStart, TimeSpan.FromMinutes(1));

        Assert.Equal(new AgentActivity(Tool.Claude, "s", T0.AddMinutes(1), T0, "claude-opus-5-5"), Assert.Single(WorkingAt(TimeSpan.FromMinutes(2))));

        Apply(ActivityKind.TurnEnd, TimeSpan.FromMinutes(3));
        Assert.Empty(WorkingAt(TimeSpan.FromMinutes(3)));
    }

    [Theory]
    [InlineData(ActivityKind.TurnStart)]
    [InlineData(ActivityKind.StepStart)]
    [InlineData(ActivityKind.WaitEnd)]
    [InlineData(ActivityKind.TurnEnd)]
    [InlineData(ActivityKind.WaitStart)]
    [InlineData(ActivityKind.StepEnd)]
    public void A_line_from_before_the_current_task_neither_moves_its_timer_back_nor_ends_it(ActivityKind kind)
    {
        // Real case: prompt at 11:22:26, then the compaction summary stamped 09:53:10 (the conversation's first time).
        Apply(ActivityKind.TurnStart, TimeSpan.FromMinutes(90));
        Apply(kind, TimeSpan.FromMinutes(1));

        var a = Assert.Single(WorkingAt(TimeSpan.FromMinutes(91)));
        Assert.Equal(T0.AddMinutes(90), a.TurnStartedUtc);
        Assert.Equal(T0.AddMinutes(1), a.SessionStartedUtc); // the session did exist back then
    }

    [Fact]
    public void An_older_line_still_reports_the_model()
    {
        Apply(ActivityKind.TurnStart, TimeSpan.FromMinutes(5));
        Apply(ActivityKind.ModelSeen, TimeSpan.FromMinutes(1), model: "claude-opus-5-5");

        Assert.Equal("claude-opus-5-5", Assert.Single(WorkingAt(TimeSpan.FromMinutes(6))).Model);
    }

    [Fact]
    public void Session_start_is_the_earliest_event_seen()
    {
        Apply(ActivityKind.TurnStart, TimeSpan.FromMinutes(5));
        Apply(ActivityKind.SessionStart, TimeSpan.FromMinutes(1));

        Assert.Equal(T0.AddMinutes(1), WorkingAt(TimeSpan.FromMinutes(6))[0].SessionStartedUtc);
    }

    [Fact]
    public void Copilot_steps_keep_working_and_end_after_a_quiet_grace_period()
    {
        Apply(ActivityKind.StepStart, TimeSpan.Zero, Tool.Copilot);           // no prompt: the step starts the timer
        Apply(ActivityKind.StepEnd, TimeSpan.FromSeconds(10), Tool.Copilot);
        Apply(ActivityKind.StepStart, TimeSpan.FromSeconds(12), Tool.Copilot); // another step: same task
        Apply(ActivityKind.StepEnd, TimeSpan.FromSeconds(20), Tool.Copilot);

        Assert.Equal(T0, Assert.Single(WorkingAt(TimeSpan.FromSeconds(34))).TurnStartedUtc);
        Assert.Empty(WorkingAt(TimeSpan.FromSeconds(35)));
    }

    [Fact]
    public void A_step_end_without_work_and_a_session_end_are_handled()
    {
        Apply(ActivityKind.StepEnd, TimeSpan.Zero, Tool.Copilot);
        Assert.Empty(WorkingAt(TimeSpan.FromSeconds(1)));

        Apply(ActivityKind.TurnStart, TimeSpan.FromSeconds(2), Tool.Copilot);
        Apply(ActivityKind.SessionEnd, TimeSpan.FromSeconds(3), Tool.Copilot);
        Assert.Empty(WorkingAt(TimeSpan.FromSeconds(4)));
    }

    [Fact]
    public void A_silent_session_counts_as_ended_when_its_process_is_gone_or_after_30_minutes()
    {
        Apply(ActivityKind.TurnStart, TimeSpan.Zero);
        var writes = new Dictionary<(Tool, string), DateTimeOffset> { [(Tool.Claude, "s")] = T0 };

        Assert.Single(WorkingAt(TimeSpan.FromMinutes(9), processAlive: false, writes));
        Assert.Empty(WorkingAt(TimeSpan.FromMinutes(10), processAlive: false, writes));
        Assert.Single(WorkingAt(TimeSpan.FromMinutes(29), processAlive: true, writes));
        Assert.Empty(WorkingAt(TimeSpan.FromMinutes(30), processAlive: true, writes));
    }

    [Fact]
    public void Sessions_are_listed_longest_running_first_and_forgotten_after_a_day()
    {
        Apply(ActivityKind.TurnStart, TimeSpan.FromMinutes(5), Tool.Codex, "b");
        Apply(ActivityKind.TurnStart, TimeSpan.FromMinutes(1), Tool.Claude, "a");
        Apply(ActivityKind.TurnStart, TimeSpan.FromMinutes(1), Tool.Copilot, "c");
        var fresh = new Dictionary<(Tool, string), DateTimeOffset>
        {
            [(Tool.Claude, "a")] = T0.AddDays(2), [(Tool.Codex, "b")] = T0.AddDays(2), [(Tool.Copilot, "c")] = T0.AddDays(2),
        };

        Assert.Equal(["a", "c", "b"], WorkingAt(TimeSpan.FromMinutes(6), writes: fresh).Select(a => a.SessionId));
        Assert.Empty(WorkingAt(TimeSpan.FromDays(1.5), writes: fresh));
    }

    [Fact]
    public void The_reader_feeds_new_lines_and_reports_writes_per_session()
    {
        var fs = new MockFileSystem();
        var now = new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);
        const string codex = @"C:\home\.codex\sessions\2026\11\18\rollout-1.jsonl";
        fs.AddFile(codex, new MockFileData(
            "{\"timestamp\":\"2026-11-18T11:58:00Z\",\"type\":\"session_meta\",\"payload\":{\"id\":\"thread-1\"}}\n" +
            "{\"timestamp\":\"2026-11-18T11:59:00Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\"}}\n")
        { LastWriteTime = now.AddSeconds(-30).UtcDateTime });
        fs.AddFile(@"C:\home\.claude\projects\p\old.jsonl", new MockFileData("") { LastWriteTime = now.AddDays(-3).UtcDateTime });
        var reader = new ActivityReader(fs, new LogScanner(fs, LogSources.Default(@"C:\home")), new AgentActivityTracker(), chunkSize: 16);

        var writes = reader.Refresh(now);

        Assert.Equal(now.AddSeconds(-30), writes[(Tool.Codex, "thread-1")]);
        Assert.Equal("thread-1", Assert.Single(reader.Tracker.Working(now, writes, _ => true)).SessionId);

        fs.File.AppendAllText(codex, "{\"timestamp\":\"2026-11-18T12:00:00Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"task_complete\"}}\n");
        fs.File.SetLastWriteTimeUtc(codex, now.UtcDateTime); // written when the task completed (the mock would stamp the real clock)
        Assert.Empty(reader.Tracker.Working(now, reader.Refresh(now), _ => true));
    }

    [Fact]
    public void A_session_spread_over_several_files_uses_the_newest_write()
    {
        // Claude subagent logs share the parent's sessionId.
        var fs = new MockFileSystem();
        var now = new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);
        const string line = "{\"type\":\"user\",\"timestamp\":\"2026-11-18T11:59:00Z\",\"sessionId\":\"s\",\"message\":{\"content\":\"go\"}}\n";
        fs.AddFile(@"C:\home\.claude\projects\p\s.jsonl", new MockFileData(line) { LastWriteTime = now.AddSeconds(-50).UtcDateTime });
        fs.AddFile(@"C:\home\.claude\projects\p\s\subagents\a.jsonl", new MockFileData(line) { LastWriteTime = now.AddSeconds(-5).UtcDateTime });
        fs.AddFile(@"C:\home\.claude\projects\p\s\subagents\b.jsonl", new MockFileData(line) { LastWriteTime = now.AddSeconds(-90).UtcDateTime });

        var writes = new ActivityReader(fs, new LogScanner(fs, LogSources.Default(@"C:\home")), new AgentActivityTracker()).Refresh(now);

        Assert.Equal(now.AddSeconds(-5), writes[(Tool.Claude, "s")]);
    }

    [Fact]
    public void The_reader_starts_over_on_a_shrunk_file_and_forgets_deleted_ones()
    {
        var fs = new MockFileSystem();
        var now = new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);
        const string path = @"C:\home\.copilot\session-state\c1\events.jsonl";
        fs.AddFile(path, new MockFileData("{\"type\":\"user.message\",\"timestamp\":\"2026-11-18T11:59:00Z\"}\n{\"type\":\"session.task_complete\",\"timestamp\":\"2026-11-18T11:59:30Z\"}\n"));
        var reader = new ActivityReader(fs, new LogScanner(fs, LogSources.Default(@"C:\home")), new AgentActivityTracker());
        fs.MockTime(() => now.UtcDateTime);
        reader.Refresh(now);

        fs.AddFile(path, new MockFileData("{\"type\":\"user.message\",\"timestamp\":\"2026-11-18T11:59:50Z\"}\n"));
        Assert.Single(reader.Tracker.Working(now, reader.Refresh(now), _ => true));

        fs.File.Delete(path);
        Assert.Empty(reader.Refresh(now));
    }

    [Fact]
    public void Logs_without_work_markers_are_not_opened()
    {
        var now = new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);
        var fs = Substitute.For<IFileSystem>();
        var recent = new List<LogFile>
        {
            new(Tool.Cline, @"C:\home\cline\task\ui_messages.json", "task", now, LogReadMode.JsonDocument),
            new(Tool.OpenCode, @"C:\home\opencode\opencode.db", "opencode", now, LogReadMode.OpenCodeDatabase),
            new(Tool.Gemini, @"C:\home\.gemini\tmp\p\chats\s.jsonl", "s", now, LogReadMode.GeminiJsonLines),
        };

        Assert.Empty(new ActivityReader(fs, new LogScanner(fs, []), new AgentActivityTracker()).Refresh(now, recent));
        fs.File.DidNotReceiveWithAnyArgs().Open(default!, default, default, default);
    }

    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    public void A_locked_file_keeps_what_was_known(Type exception)
    {
        var now = new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);
        var scanner = new LogScanner(new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [@"C:\home\.claude\projects\p\s.jsonl"] = new("") { LastWriteTime = now.UtcDateTime },
        }), LogSources.Default(@"C:\home"));
        var fs = Substitute.For<IFileSystem>();
        fs.File.Open(Arg.Any<string>(), Arg.Any<FileMode>(), Arg.Any<FileAccess>(), Arg.Any<FileShare>()).Throws((Exception)Activator.CreateInstance(exception)!);

        Assert.Empty(new ActivityReader(fs, scanner, new AgentActivityTracker()).Refresh(now));
    }

    [Fact]
    public void Unexpected_reader_errors_are_not_hidden()
    {
        var now = new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);
        var scanner = new LogScanner(new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [@"C:\home\.claude\projects\p\s.jsonl"] = new("") { LastWriteTime = now.UtcDateTime },
        }), LogSources.Default(@"C:\home"));
        var fs = Substitute.For<IFileSystem>();
        fs.File.Open(Arg.Any<string>(), Arg.Any<FileMode>(), Arg.Any<FileAccess>(), Arg.Any<FileShare>()).Throws(new InvalidOperationException());

        Assert.Throws<InvalidOperationException>(() => new ActivityReader(fs, scanner, new AgentActivityTracker()).Refresh(now));
    }

    [Fact]
    public void A_stop_hook_sending_claude_back_to_work_keeps_it_working_like_the_tray_sees_it()
    {
        // The real lines from a Claude Code log (2026-11-18): the agent ends its turn, a Stop hook sends it back to
        // work with a meta prompt and a stop_hook_summary, and it carries on with tool calls for minutes.
        var fs = new MockFileSystem();
        const string path = @"C:\home\.claude\projects\p\s.jsonl";
        var lines = string.Join("\n",
            """{"type":"user","timestamp":"2026-11-18T12:00:00Z","sessionId":"s","message":{"content":"build it"}}""",
            """{"type":"assistant","timestamp":"2026-11-18T12:05:00Z","sessionId":"s","message":{"model":"claude-opus-5-5","stop_reason":"end_turn","content":[{"type":"text","text":"done"}]}}""",
            """{"type":"user","timestamp":"2026-11-18T12:05:01Z","sessionId":"s","isMeta":true,"message":{"content":"Stop hook feedback:\n[finish the plan]"}}""",
            """{"type":"system","subtype":"stop_hook_summary","timestamp":"2026-11-18T12:05:01Z","sessionId":"s"}""",
            """{"type":"assistant","timestamp":"2026-11-18T12:05:30Z","sessionId":"s","message":{"model":"claude-opus-5-5","stop_reason":"tool_use","content":[{"type":"tool_use"}]}}""") + "\n";
        var end = new DateTimeOffset(2026, 11, 18, 12, 5, 1, TimeSpan.Zero);
        fs.AddFile(path, new MockFileData(lines) { LastWriteTime = end.AddSeconds(29).UtcDateTime });
        var reader = new ActivityReader(fs, new LogScanner(fs, LogSources.Default(@"C:\home")), new AgentActivityTracker());

        var now = end.AddSeconds(40);
        var working = Assert.Single(reader.Tracker.Working(now, reader.Refresh(now), _ => true));

        // The task timer restarts where the hook sent it back to work; the session still started at the first prompt.
        Assert.Equal(new AgentActivity(Tool.Claude, "s", end, end.AddMinutes(-5).AddSeconds(-1), "claude-opus-5-5"), working);

        // Two minutes without a write (the tray's active window): no longer working, on the tray or the island.
        now = end.AddSeconds(29) + Track.Core.Live.LiveStatusDetector.ActiveWindow + TimeSpan.FromSeconds(1);
        Assert.Empty(reader.Tracker.Working(now, reader.Refresh(now), _ => true));
    }

    [Fact]
    public void Lines_written_right_after_an_end_belong_to_the_ended_turn()
    {
        Apply(ActivityKind.TurnStart, TimeSpan.Zero);
        Apply(ActivityKind.TurnEnd, TimeSpan.FromMinutes(1));

        Assert.Empty(WorkingAt(TimeSpan.FromMinutes(1.1), writes: new() { [(Tool.Claude, "s")] = T0.AddMinutes(1) + AgentActivityTracker.EndSettle }));
        Assert.Single(WorkingAt(TimeSpan.FromMinutes(1.1), writes: new() { [(Tool.Claude, "s")] = T0.AddMinutes(1) + AgentActivityTracker.EndSettle + TimeSpan.FromSeconds(1) }));
    }

    [Fact]
    public void A_session_written_now_whose_prompt_was_never_seen_is_working_from_its_start()
    {
        Apply(ActivityKind.SessionStart, TimeSpan.Zero, Tool.Codex, "c");

        Assert.Empty(WorkingAt(TimeSpan.FromMinutes(1)));
        Assert.Equal(
            new AgentActivity(Tool.Codex, "c", T0, T0, null),
            Assert.Single(WorkingAt(TimeSpan.FromMinutes(1), writes: new() { [(Tool.Codex, "c")] = T0.AddSeconds(50) })));
        Assert.Empty(WorkingAt(TimeSpan.FromMinutes(3), writes: new() { [(Tool.Codex, "c")] = T0.AddSeconds(50) }));
    }

    [Fact]
    public void Copilot_writing_well_after_a_step_end_is_still_on_the_same_task()
    {
        Apply(ActivityKind.TurnStart, TimeSpan.Zero, Tool.Copilot, "p");
        Apply(ActivityKind.StepEnd, TimeSpan.FromMinutes(1), Tool.Copilot, "p");

        Assert.Empty(WorkingAt(TimeSpan.FromMinutes(2), writes: new() { [(Tool.Copilot, "p")] = T0.AddMinutes(1).AddSeconds(5) }));
        Assert.Equal(T0, Assert.Single(WorkingAt(TimeSpan.FromMinutes(2), writes: new() { [(Tool.Copilot, "p")] = T0.AddMinutes(1).AddSeconds(30) })).TurnStartedUtc);
    }

    [Fact]
    public void Waiting_for_the_users_answer_is_not_working_and_the_answer_restarts_the_timer()
    {
        Apply(ActivityKind.TurnStart, TimeSpan.Zero, Tool.Copilot, "p");
        Apply(ActivityKind.WaitStart, TimeSpan.FromMinutes(1), Tool.Copilot, "p");
        var writes = new Dictionary<(Tool, string), DateTimeOffset> { [(Tool.Copilot, "p")] = T0.AddMinutes(3) };

        // Waiting: not working (even though the log was written), and listed as waiting.
        Assert.Empty(WorkingAt(TimeSpan.FromMinutes(3.5), writes: writes));
        Assert.Equal([(Tool.Copilot, "p")], _tracker.Waiting());

        // Five hours later the user answers: working again, the task timer starts at the answer.
        Apply(ActivityKind.WaitEnd, TimeSpan.FromHours(5), Tool.Copilot, "p");
        Assert.Equal(T0.AddHours(5), Assert.Single(WorkingAt(TimeSpan.FromHours(5) + TimeSpan.FromMinutes(1))).TurnStartedUtc);
        Assert.Empty(_tracker.Waiting());

        // Waiting ends with the task too.
        Apply(ActivityKind.WaitStart, TimeSpan.FromHours(6), Tool.Copilot, "p");
        Apply(ActivityKind.TurnEnd, TimeSpan.FromHours(7), Tool.Copilot, "p");
        Assert.Empty(_tracker.Waiting());
    }
}
