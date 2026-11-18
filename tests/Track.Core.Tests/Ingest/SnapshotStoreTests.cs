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
