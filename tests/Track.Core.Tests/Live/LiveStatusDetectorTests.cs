using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Track.Core.Ingest;
using Track.Core.Live;
using Track.Core.Parsing;
using Track.Core.Usage;

namespace Track.Core.Tests.Live;

public class LiveStatusDetectorTests
{
    private const string Home = @"C:\home";
    private const string CopilotRoot = @"C:\home\.copilot\session-state";
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    private readonly MockFileSystem _fs = new();
    private readonly IProcessList _processes = Substitute.For<IProcessList>();
    private readonly UsageStore _store = new();

    private void Log(string path, TimeSpan age) =>
        _fs.AddFile(path, new MockFileData("") { LastWriteTime = (Now - age).UtcDateTime });

    private IReadOnlyDictionary<Tool, LiveStatus> Detect(IFileSystem? holdFs = null) =>
        new LiveStatusDetector(
            holdFs ?? _fs,
            new LogScanner(_fs, LogSources.Default(Home)),
            CopilotRoot,
            _processes,
            new UsageAggregator(_store),
            new FakeTimeProvider(Now)).Detect();

    [Fact]
    public void A_tool_is_working_when_its_log_was_written_in_the_last_two_minutes()
    {
        Log(@"C:\home\.claude\projects\p\a.jsonl", TimeSpan.FromMinutes(2));
        Log(@"C:\home\.claude\projects\p\b.jsonl", TimeSpan.FromSeconds(5));
        Log(@"C:\home\.claude\projects\p\c.jsonl", TimeSpan.FromMinutes(2).Add(TimeSpan.FromSeconds(1)));
        Log(@"C:\home\.codex\sessions\2026\11\18\rollout-1.jsonl", TimeSpan.FromMinutes(40));

        var live = Detect();

        Assert.Equal((true, 2, Now.AddSeconds(-5)), (live[Tool.Claude].Working, live[Tool.Claude].ActiveSessions, live[Tool.Claude].LastActivityUtc));
        Assert.Equal((false, 0, Now.AddMinutes(-40)), (live[Tool.Codex].Working, live[Tool.Codex].ActiveSessions, live[Tool.Codex].LastActivityUtc));
        Assert.Equal((false, (DateTimeOffset?)null), (live[Tool.Copilot].Working, live[Tool.Copilot].LastActivityUtc));
    }

    [Fact]
    public void Activity_older_than_a_day_is_not_reported()
    {
        Log(@"C:\home\.claude\projects\p\a.jsonl", TimeSpan.FromDays(2));

        Assert.Null(Detect()[Tool.Claude].LastActivityUtc);
    }

    [Fact]
    public void Process_and_latest_model_are_reported_per_tool()
    {
        _processes.AnyRunning(Arg.Is<IReadOnlyCollection<string>>(n => n.Contains("codex"))).Returns(true);
        _store.Add([new UsageRecord(Tool.Codex, "s", "gpt-5.6-luna", Now, 1, 0, 0, 0, 0, "k")]);

        var live = Detect();

        Assert.True(live[Tool.Codex].ProcessRunning);
        Assert.False(live[Tool.Claude].ProcessRunning);
        Assert.Equal("gpt-5.6-luna", live[Tool.Codex].LastModel);
        Assert.Null(live[Tool.Claude].LastModel);
        Assert.Equal(["copilot-runtime", "copilotapp_proxy", "copilot"], LiveStatusDetector.ProcessNames[Tool.Copilot]);
    }

    [Fact]
    public void Copilot_open_sessions_come_from_hold_files_of_live_processes()
    {
        _fs.AddFile(@"C:\home\.copilot\session-state\s1\inuse.100.hold", new MockFileData(""));
        _fs.AddFile(@"C:\home\.copilot\session-state\s1\inuse.101.hold", new MockFileData(""));
        _fs.AddFile(@"C:\home\.copilot\session-state\s2\inuse.200.hold", new MockFileData(""));
        _fs.AddFile(@"C:\home\.copilot\session-state\s3\inuse.300.hold", new MockFileData(""));
        _fs.AddFile(@"C:\home\.copilot\session-state\s4\inuse.abc.hold", new MockFileData(""));
        _fs.AddFile(@"C:\home\.copilot\session-state\s5\inuse.99999999999.hold", new MockFileData(""));
        _processes.IsAlive(Arg.Is<int>(pid => pid != 300)).Returns(true);

        var live = Detect();

        Assert.Equal(2, live[Tool.Copilot].OpenSessions); // s1 (two holds) and s2; s3's process is gone
        Assert.Equal(0, live[Tool.Claude].OpenSessions);
    }

    [Fact]
    public void No_copilot_folder_means_no_open_sessions()
    {
        Assert.Equal(0, Detect()[Tool.Copilot].OpenSessions);
    }

    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    public void An_unreadable_copilot_folder_means_no_open_sessions(Type exception)
    {
        var fs = Substitute.For<IFileSystem>();
        fs.Directory.Exists(CopilotRoot).Returns(true);
        fs.Directory.EnumerateDirectories(CopilotRoot).Throws((Exception)Activator.CreateInstance(exception)!);

        Assert.Equal(0, Detect(fs)[Tool.Copilot].OpenSessions);
    }

    [Fact]
    public void Unexpected_errors_are_not_hidden()
    {
        var fs = Substitute.For<IFileSystem>();
        fs.Directory.Exists(CopilotRoot).Returns(true);
        fs.Directory.EnumerateDirectories(CopilotRoot).Throws(new InvalidOperationException());

        Assert.Throws<InvalidOperationException>(() => Detect(fs));
    }

    [Fact]
    public void The_system_process_list_sees_this_test_process()
    {
        var processes = new SystemProcessList();
        using var self = System.Diagnostics.Process.GetCurrentProcess();

        Assert.True(processes.AnyRunning([self.ProcessName]));
        Assert.False(processes.AnyRunning(["no-such-process-track-test"]));
        Assert.True(processes.IsAlive(Environment.ProcessId));
        Assert.False(processes.IsAlive(int.MaxValue));
    }

    [Fact]
    public void The_tray_takes_working_and_session_count_from_the_islands_agents()
    {
        var at = new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);
        var live = new Dictionary<Tool, LiveStatus>
        {
            [Tool.Claude] = new(Tool.Claude, false, 0, 0, true, "claude-opus-5-5", at),
            [Tool.Codex] = new(Tool.Codex, true, 1, 0, true, null, at),
            [Tool.Copilot] = new(Tool.Copilot, false, 0, 2, false, null, null),
        };
        Track.Core.Activity.AgentActivity Agent(Tool tool, string id) => new(tool, id, at, at, null);

        var shared = LiveStatusDetector.WithAgents(live, [Agent(Tool.Claude, "a"), Agent(Tool.Claude, "b")]);

        Assert.Equal(live[Tool.Claude] with { Working = true, ActiveSessions = 2 }, shared[Tool.Claude]);
        Assert.Equal(live[Tool.Codex] with { Working = false, ActiveSessions = 0 }, shared[Tool.Codex]);
        Assert.Equal(live[Tool.Copilot], shared[Tool.Copilot]);
    }
}
