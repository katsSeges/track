using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Time.Testing;
using Track.Core.Activity;
using Track.Core.Ingest;
using Track.Core.Live;
using Track.Core.Usage;

namespace Track.Core.Tests.Ingest;

/// <summary>The Phase 9 changes that keep the 5-second live checks cheap.</summary>
public class PerformanceGuardsTests
{
    private const string Home = @"C:\home";
    private const string Log = @"C:\home\.claude\projects\p\s.jsonl";
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Scan_cache_walks_folders_once_a_minute_and_restats_known_files_in_between()
    {
        var time = new FakeTimeProvider(Now);
        var fs = new MockFileSystem();
        fs.MockTime(() => time.GetUtcNow().UtcDateTime);
        fs.AddFile(Log, new MockFileData("a"));
        var cache = new LogScanCache(fs, new LogScanner(fs, LogSources.Default(Home)), time);

        Assert.Single(cache.Recent());
        time.Advance(TimeSpan.FromSeconds(10));
        fs.File.AppendAllText(Log, "b");
        fs.AddFile(@"C:\home\.claude\projects\p\new.jsonl", new MockFileData("c"));

        var between = cache.Recent();
        Assert.Equal((1, Now.AddSeconds(10)), (cache.FullScans, Assert.Single(between).LastWriteUtc)); // write time refreshed, new file not yet seen

        cache.MarkDirty();
        Assert.Equal(2, cache.Recent().Count);
        Assert.Equal(2, cache.FullScans);

        time.Advance(LogScanCache.FullScanEvery);
        cache.Recent();
        Assert.Equal(3, cache.FullScans);
    }

    [Fact]
    public void Scan_cache_drops_files_deleted_or_older_than_a_day_between_scans()
    {
        var time = new FakeTimeProvider(Now);
        var fs = new MockFileSystem();
        fs.MockTime(() => time.GetUtcNow().UtcDateTime);
        fs.AddFile(Log, new MockFileData("a"));
        fs.AddFile(@"C:\home\.claude\projects\p\other.jsonl", new MockFileData("a"));
        var cache = new LogScanCache(fs, new LogScanner(fs, LogSources.Default(Home)), time);
        cache.Recent();

        fs.File.Delete(Log);

        Assert.Equal(@"C:\home\.claude\projects\p\other.jsonl", Assert.Single(cache.Recent()).Path);
    }

    [Fact]
    public void Process_names_are_read_once_per_snapshot()
    {
        var time = new FakeTimeProvider(Now);
        var reads = 0;
        var list = new SystemProcessList(time, () =>
        {
            reads++;
            return ["Claude", "explorer"];
        });

        Assert.True(list.AnyRunning(["claude"]));
        Assert.False(list.AnyRunning(["codex", "copilot"]));
        Assert.Equal(1, reads);

        time.Advance(SystemProcessList.SnapshotAge);
        list.AnyRunning(["claude"]);
        Assert.Equal(2, reads);
    }

    [Fact]
    public void Live_detector_and_activity_reader_use_a_given_file_list()
    {
        var fs = new MockFileSystem();
        fs.AddFile(Log, new MockFileData("{\"type\":\"user\",\"timestamp\":\"2026-11-18T11:59:30Z\",\"sessionId\":\"s\",\"message\":{\"content\":\"go\"}}\n"));
        var scanner = new LogScanner(new MockFileSystem(), LogSources.Default(Home)); // would find nothing
        var given = new List<LogFile> { new(Tool.Claude, Log, "s", Now.AddSeconds(-30)), new(Tool.Codex, @"C:\gone.jsonl", "x", Now.AddDays(-2)) };

        var live = new LiveStatusDetector(fs, scanner, @"C:\home\.copilot\session-state", new SystemProcessList(), new UsageAggregator(new UsageStore()), new FakeTimeProvider(Now)).Detect(given);
        var reader = new ActivityReader(fs, scanner, new AgentActivityTracker());
        var writes = reader.Refresh(Now, given.Take(1).ToList());

        Assert.True(live[Tool.Claude].Working);
        Assert.Null(live[Tool.Codex].LastActivityUtc); // older than a day: ignored
        Assert.Single(reader.Tracker.Working(Now, writes, _ => true));
    }

    [Theory]
    [InlineData("{\"type\":\"progress\",\"timestamp\":\"2026-11-18T10:00:00Z\",\"data\":\"x\"}")]
    [InlineData("{\"type\":\"user\",\"timestamp\":\"2026-11-18T10:00:00Z\",\"message\":{\"content\":[{\"type\":\"tool_result\",\"content\":\"done\"}]}}")]
    [InlineData("{\"type\":\"assistant\",\"timestamp\":\"2026-11-18T10:00:00Z\",\"message\":{\"model\":\"claude-opus-5-5\",\"stop_reason\":\"tool_use\"}}")]
    public void Claude_parser_skips_lines_that_cannot_change_anything_once_the_model_is_known(string line)
    {
        var parser = new ClaudeActivityParser("s");
        parser.Parse("{\"type\":\"assistant\",\"timestamp\":\"2026-11-18T09:00:00Z\",\"message\":{\"model\":\"claude-opus-5-5\",\"stop_reason\":\"tool_use\"}}");

        Assert.Empty(parser.Parse(line));
    }

    [Fact]
    public void Claude_parser_still_reads_the_model_until_it_has_one()
    {
        var parser = new ClaudeActivityParser("s");
        parser.Parse("{\"type\":\"progress\",\"timestamp\":\"2026-11-18T09:00:00Z\"}");

        Assert.Equal(ActivityKind.ModelSeen, Assert.Single(parser.Parse("{\"type\":\"assistant\",\"timestamp\":\"2026-11-18T10:00:00Z\",\"message\":{\"model\":\"claude-opus-5-5\",\"stop_reason\":\"tool_use\"}}")).Kind);
    }

    [Fact]
    public void Lines_that_pass_the_filter_are_still_checked_properly()
    {
        // A first line is always parsed (it starts the session), even a tool result.
        Assert.Equal([ActivityKind.SessionStart], new ClaudeActivityParser("s")
            .Parse("{\"type\":\"user\",\"timestamp\":\"2026-11-18T10:00:00Z\",\"message\":{\"content\":[{\"type\":\"tool_result\"}]}}")
            .Select(e => e.Kind));

        // Marker words inside other text do not count.
        var claude = new ClaudeActivityParser("s");
        claude.Parse("{\"type\":\"progress\",\"timestamp\":\"2026-11-18T09:00:00Z\"}");
        Assert.Empty(claude.Parse("{\"type\":\"system\",\"timestamp\":\"2026-11-18T10:00:00Z\",\"subtype\":\"note\",\"text\":\"stop_hook_summary\"}"));
        Assert.Empty(new CodexActivityParser("r").Parse("{\"timestamp\":\"2026-11-18T10:00:00Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"agent_message\",\"message\":\"task_complete\"}}"));

        // Marker lines without a timestamp give nothing.
        Assert.Empty(new CodexActivityParser("r").Parse("{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\"}}"));
        Assert.Empty(new CopilotActivityParser("c").Parse("{\"type\":\"user.message\"}"));
    }

    [Fact]
    public void Codex_and_copilot_parsers_skip_lines_without_a_marker()
    {
        Assert.Empty(new CodexActivityParser("r").Parse("{\"timestamp\":\"2026-11-18T10:00:00Z\",\"type\":\"response_item\",\"payload\":{\"type\":\"function_call\"}}"));
        Assert.Empty(new CopilotActivityParser("c").Parse("{\"type\":\"tool.execution_start\",\"timestamp\":\"2026-11-18T10:00:00Z\"}"));
    }
}
