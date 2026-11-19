using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Track.Core.Ingest;
using Track.Core.Parsing;

namespace Track.Core.Tests.Ingest;

public class SnapshotStoreTests
{
    private const string Path = @"C:\appdata\Track\snapshot.json";

    private static Snapshot Sample() => new(
        SnapshotStore.CurrentVersion,
        Lines.Now,
        [new Snapshot.Bucket(Tool.Codex, "gpt-5.6-luna", Lines.Now, new TokenCounts(1, 2, 3, 4, 5, 6))],
        [new Snapshot.Session(Tool.Claude, "s1", new SessionUsage(new TokenCounts(7, 0, 0, 1, 0, 1), Lines.Now.AddHours(-1), Lines.Now))],
        [new FileCursor(@"C:\logs\a.jsonl", Tool.Codex, "a", 123, [1, 2, 3], new ParserState("sid", "m", [1, 2, 3, 4, 5, 6], ["k"]))]);

    [Fact]
    public void Save_then_load_gives_the_same_snapshot()
    {
        var fs = new MockFileSystem();
        var store = new SnapshotStore(fs, Path);

        Assert.True(store.Save(Sample()));
        var loaded = store.Load()!;

        var expected = Sample();
        Assert.Equal((expected.Version, expected.SavedUtc), (loaded.Version, loaded.SavedUtc));
        Assert.Equal(expected.Buckets, loaded.Buckets);
        Assert.Equal(expected.Sessions, loaded.Sessions);
        var cursor = Assert.Single(loaded.Cursors);
        Assert.Equal((@"C:\logs\a.jsonl", Tool.Codex, "a", 123L), (cursor.Path, cursor.Tool, cursor.SessionId, cursor.Offset));
        Assert.Equal([1, 2, 3], cursor.Head);
        Assert.Equal(("sid", "m"), (cursor.Parser.SessionId, cursor.Parser.Model));
        Assert.Equal([1, 2, 3, 4, 5, 6], cursor.Parser.Previous!);
        Assert.Equal(["k"], cursor.Parser.RecentKeys!);
        Assert.False(fs.File.Exists(Path + ".tmp"));
        Assert.Equal(Path, store.FilePath);
    }

    [Fact]
    public void The_file_is_readable_json_with_named_tools()
    {
        var fs = new MockFileSystem();
        new SnapshotStore(fs, Path).Save(Sample());

        var json = fs.File.ReadAllText(Path);

        Assert.Contains("\"tool\":\"Codex\"", json);
        Assert.DoesNotContain("\"total\"", json);
        Assert.Equal(1, JsonDocument.Parse(json).RootElement.GetProperty("version").GetInt32());
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("null")]
    [InlineData("""{"version":0,"savedUtc":"2026-11-18T12:00:00Z","buckets":[],"sessions":[],"cursors":[]}""")]
    public void A_corrupt_or_old_file_loads_as_nothing(string content)
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData> { [Path] = new(content) });

        Assert.Null(new SnapshotStore(fs, Path).Load());
    }

    [Fact]
    public void A_missing_file_loads_as_nothing()
    {
        Assert.Null(new SnapshotStore(new MockFileSystem(), Path).Load());
    }

    [Fact]
    public void The_v1_1_snapshot_serializer_fixture_loads_and_restores_identical_totals()
    {
        var fixture = File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", "snapshot-v1.1.0.json"));
        var fs = new MockFileSystem(new Dictionary<string, MockFileData> { [Path] = new(fixture) });

        var loaded = new SnapshotStore(fs, Path).Load()!;

        Assert.Equal(1, loaded.Version);
        var usage = new UsageStore();
        usage.Restore(loaded.Buckets, loaded.Sessions);
        var restored = usage.Save();
        Assert.Equal(new TokenCounts(10, 2, 3, 4, 1, 1), Assert.Single(restored.Buckets).Tokens);
        Assert.Equal("session-a", Assert.Single(restored.Sessions).SessionId);
        Assert.Equal(new TokenCounts(5, 1, 2, 3, 0, 1), restored.Sessions[0].Usage.Tokens);
        Assert.Empty(loaded.DocumentCursors);
    }

    [Fact]
    public void Unknown_snapshot_properties_are_ignored()
    {
        const string content = """{"version":1,"savedUtc":"2026-11-18T12:00:00Z","buckets":[],"sessions":[],"cursors":[],"futureProperty":"ignored"}""";
        var fs = new MockFileSystem(new Dictionary<string, MockFileData> { [Path] = new(content) });

        Assert.NotNull(new SnapshotStore(fs, Path).Load());
    }

    [Fact]
    public void Additive_document_cursors_round_trip_without_changing_snapshot_version()
    {
        var fs = new MockFileSystem();
        var store = new SnapshotStore(fs, Path);
        var usage = new UsageRecord(Tool.Gemini, "session", "gemini-2.5", Lines.Now, 10, 0, 2, 3, 1, "message");
        var snapshot = Sample() with
        {
            DocumentCursors =
            [
                new DocumentCursor(@"C:\logs\session.jsonl", Tool.Gemini, "session", "5:123", Lines.Now, 123, [usage]),
            ],
        };

        Assert.True(store.Save(snapshot));
        var loaded = store.Load()!;

        Assert.Equal(1, loaded.Version);
        var cursor = Assert.Single(loaded.DocumentCursors);
        Assert.Equal((@"C:\logs\session.jsonl", Tool.Gemini, "session", "5:123", 123L),
            (cursor.Path, cursor.Tool, cursor.SessionId, cursor.Fingerprint, cursor.Length));
        Assert.Equal(usage, Assert.Single(cursor.Records));
    }

    [Fact]
    public void An_unknown_enum_value_in_a_snapshot_keeps_the_existing_reread_fallback()
    {
        const string content = """{"version":1,"savedUtc":"2026-11-18T12:00:00Z","buckets":[{"tool":"FutureTool","model":"m","hourUtc":"2026-11-18T11:00:00Z","tokens":{"input":1,"cacheWrite":0,"cacheRead":0,"output":0,"reasoning":0,"calls":1}}],"sessions":[],"cursors":[]}""";
        var fs = new MockFileSystem(new Dictionary<string, MockFileData> { [Path] = new(content) });

        Assert.Null(new SnapshotStore(fs, Path).Load());
    }

    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    public void An_unreadable_file_loads_as_nothing(Type exception)
    {
        var fs = Substitute.For<IFileSystem>();
        fs.File.Exists(Path).Returns(true);
        fs.File.ReadAllText(Path).Throws((Exception)Activator.CreateInstance(exception)!);

        Assert.Null(new SnapshotStore(fs, Path).Load());
    }

    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    public void A_failed_save_returns_false(Type exception)
    {
        var fs = Substitute.For<IFileSystem>();
        fs.Path.GetDirectoryName(Path).Returns(@"C:\appdata\Track");
        fs.File.When(f => f.WriteAllText(Arg.Any<string>(), Arg.Any<string>())).Throw((Exception)Activator.CreateInstance(exception)!);

        Assert.False(new SnapshotStore(fs, Path).Save(Sample()));
    }

    [Fact]
    public void Unexpected_errors_are_not_hidden()
    {
        var fs = Substitute.For<IFileSystem>();
        fs.File.Exists(Path).Returns(true);
        fs.File.ReadAllText(Path).Throws(new InvalidOperationException());
        fs.Path.GetDirectoryName(Path).Returns(@"C:\appdata\Track");
        fs.File.When(f => f.WriteAllText(Arg.Any<string>(), Arg.Any<string>())).Throw(new InvalidOperationException());
        var store = new SnapshotStore(fs, Path);

        Assert.Throws<InvalidOperationException>(() => store.Load());
        Assert.Throws<InvalidOperationException>(() => store.Save(Sample()));
    }

    [Fact]
    public void Default_path_is_in_the_track_folder_of_local_app_data()
    {
        Assert.Equal(@"C:\Users\me\AppData\Local\Track\snapshot.json", SnapshotStore.DefaultPath(@"C:\Users\me\AppData\Local"));
    }
}
