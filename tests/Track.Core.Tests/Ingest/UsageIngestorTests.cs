using System.IO.Abstractions.TestingHelpers;
using System.Text;
using Microsoft.Extensions.Time.Testing;
using Track.Core.Ingest;

namespace Track.Core.Tests.Ingest;

public class UsageIngestorTests
{
    private const string Home = @"C:\home";
    private const string SnapshotPath = @"C:\appdata\Track\snapshot.json";
    private const string ClaudeLog = @"C:\home\.claude\projects\p\s1.jsonl";
    private const string CodexLog = @"C:\home\.codex\sessions\2026\11\18\rollout-1.jsonl";
    private const string CopilotLog = @"C:\home\.copilot\session-state\c1\events.jsonl";

    private readonly MockFileSystem _fs = new();
    private readonly FakeTimeProvider _time = new(Lines.Now);
    private IncrementalLogReader _reader = null!;
    private UsageIngestor _ingestor = null!;

    public UsageIngestorTests()
    {
        _fs.MockTime(() => _time.GetUtcNow().UtcDateTime);
        Start(withSnapshot: false);
    }

    /// <summary>Simulates (re)starting the app: fresh reader and store.</summary>
    private void Start(bool withSnapshot = true)
    {
        _reader = new IncrementalLogReader(_fs);
        _ingestor = new UsageIngestor(
            new LogScanner(_fs, LogSources.Default(Home)),
            _reader,
            new UsageStore(),
            _time,
            withSnapshot ? new SnapshotStore(_fs, SnapshotPath) : null);
    }

    private long Total() => _ingestor.Store.Buckets.Values.Sum(v => v.Total);

    [Fact]
    public void Refresh_collects_new_usage_from_every_tool_once()
    {
        _fs.AddFile(ClaudeLog, new MockFileData(Lines.Claude("m1") + "\n" + Lines.Claude("m1") + "\n"));
        _fs.AddFile(CopilotLog, new MockFileData(Lines.Copilot("e1") + "\n"));

        Assert.Equal(2, _ingestor.Refresh());
        Assert.Equal(0, _ingestor.Refresh());

        _fs.File.AppendAllText(CopilotLog, Lines.Copilot("e2") + "\n");

        Assert.Equal(1, _ingestor.Refresh());
        Assert.Equal(30, Total());
    }

    [Fact]
    public void Usage_older_than_the_retention_window_is_dropped()
    {
        _fs.AddFile(ClaudeLog, new MockFileData(
            Lines.Claude("old", timestamp: "2026-08-01T00:00:00Z") + "\n" + Lines.Claude("new", 7) + "\n"));

        _ingestor.Refresh();

        Assert.Equal(7, Total());
    }

    [Fact]
    public void Usage_ages_out_as_time_passes()
    {
        _fs.AddFile(ClaudeLog, new MockFileData(Lines.Claude("m1") + "\n"));
        _ingestor.Refresh();

        _time.Advance(TimeSpan.FromDays(91));
        _fs.File.AppendAllText(ClaudeLog, Lines.Claude("m2", 3, timestamp: "2027-02-17T00:00:00Z") + "\n");
        _ingestor.Refresh();

        Assert.Equal(3, Total());
        Assert.Single(_ingestor.Store.Sessions);
    }

    [Fact]
    public void Deleted_files_stop_being_tracked_but_their_usage_stays()
    {
        _fs.AddFile(ClaudeLog, new MockFileData(Lines.Claude("m1") + "\n"));
        _ingestor.Refresh();

        _fs.File.Delete(ClaudeLog);
        _ingestor.Refresh();

        Assert.Empty(_reader.TrackedPaths);
        Assert.Equal(10, Total());
    }

    [Fact]
    public void A_second_refresh_of_50_MB_of_unchanged_logs_reads_nothing()
    {
        var filler = new StringBuilder();
        while (filler.Length < 1 << 20)
        {
            filler.Append("{\"type\":\"user\",\"message\":{\"content\":\"some prompt text that is not usage\"}}\n");
        }

        var oneMegabyte = filler.ToString();
        for (var i = 0; i < 50; i++)
        {
            _fs.AddFile($@"C:\home\.claude\projects\p\big-{i}.jsonl", new MockFileData(oneMegabyte));
        }

        _ingestor.Refresh();
        var afterFirst = _reader.BytesRead;
        _ingestor.Refresh();

        Assert.True(afterFirst >= 50L << 20, $"first refresh read {afterFirst} bytes");
        Assert.Equal(afterFirst, _reader.BytesRead);
    }

    [Fact]
    public void A_restart_continues_from_the_snapshot_and_reads_only_file_heads()
    {
        Start();
        _fs.AddFile(ClaudeLog, new MockFileData(Lines.Claude("m1") + "\n"));
        _fs.AddFile(CopilotLog, new MockFileData(Lines.Copilot("e1") + "\n"));
        _ingestor.Refresh();
        Assert.False(_ingestor.RestoredFromSnapshot);

        Start();
        Assert.Equal(0, _ingestor.Refresh());

        Assert.True(_ingestor.RestoredFromSnapshot);
        Assert.Equal(20, Total());
        Assert.Equal(2, _ingestor.Store.Sessions.Count);
        Assert.True(_reader.BytesRead <= 2 * IncrementalLogReader.HeadLength, $"read {_reader.BytesRead} bytes");
    }

    [Fact]
    public void Usage_written_while_stopped_is_counted_once_after_the_restart()
    {
        Start();
        _fs.AddFile(ClaudeLog, new MockFileData(Lines.Claude("m1") + "\n"));
        _fs.AddFile(CodexLog, new MockFileData(Lines.Codex(1000) + "\n"));
        _ingestor.Refresh();

        // While Track is stopped: the same Claude response gets another content-block line, Codex grows.
        _fs.File.AppendAllText(ClaudeLog, Lines.Claude("m1") + "\n" + Lines.Claude("m2", 5) + "\n");
        _fs.File.AppendAllText(CodexLog, Lines.Codex(1500) + "\n");
        Start();

        Assert.Equal(2, _ingestor.Refresh());
        Assert.Equal(10 + 5 + 1000 + 500, Total());
    }

    [Fact]
    public void Snapshots_are_saved_at_most_once_a_minute_and_only_after_new_usage()
    {
        Start();
        var snapshots = new SnapshotStore(_fs, SnapshotPath);
        _fs.AddFile(ClaudeLog, new MockFileData(Lines.Claude("m1") + "\n"));
        _ingestor.Refresh();
        var firstSave = snapshots.Load()!.SavedUtc;

        _time.Advance(TimeSpan.FromSeconds(30));
        _fs.File.AppendAllText(ClaudeLog, Lines.Claude("m2") + "\n");
        _ingestor.Refresh();
        Assert.Equal(firstSave, snapshots.Load()!.SavedUtc);

        _time.Advance(TimeSpan.FromMinutes(5));
        _ingestor.Refresh();
        Assert.Equal(firstSave, snapshots.Load()!.SavedUtc);

        _fs.File.AppendAllText(ClaudeLog, Lines.Claude("m3") + "\n");
        _ingestor.Refresh();
        Assert.Equal(_time.GetUtcNow(), snapshots.Load()!.SavedUtc);
    }

    [Fact]
    public void A_restored_snapshot_counts_as_a_recent_save()
    {
        Start();
        _fs.AddFile(ClaudeLog, new MockFileData(Lines.Claude("m1") + "\n"));
        _ingestor.Refresh();
        var saved = new SnapshotStore(_fs, SnapshotPath).Load()!.SavedUtc;

        _time.Advance(TimeSpan.FromSeconds(10));
        _fs.File.AppendAllText(ClaudeLog, Lines.Claude("m2") + "\n");
        Start();
        _ingestor.Refresh();

        Assert.Equal(saved, new SnapshotStore(_fs, SnapshotPath).Load()!.SavedUtc);
    }

    [Fact]
    public void A_corrupt_snapshot_means_reading_everything_again()
    {
        _fs.AddFile(SnapshotPath, new MockFileData("{broken"));
        _fs.AddFile(ClaudeLog, new MockFileData(Lines.Claude("m1") + "\n"));
        Start();

        Assert.Equal(1, _ingestor.Refresh());
        Assert.False(_ingestor.RestoredFromSnapshot);
        Assert.Equal(SnapshotStore.CurrentVersion, new SnapshotStore(_fs, SnapshotPath).Load()!.Version);
    }

    [Fact]
    public void Saving_on_exit_works_with_a_snapshot_store_and_is_a_no_op_without()
    {
        Assert.False(_ingestor.SaveSnapshot());

        Start();
        Assert.True(_ingestor.SaveSnapshot());
        Assert.NotNull(new SnapshotStore(_fs, SnapshotPath).Load());
    }
}
