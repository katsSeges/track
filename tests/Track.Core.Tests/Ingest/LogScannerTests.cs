using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Track.Core.Ingest;

namespace Track.Core.Tests.Ingest;

public class LogScannerTests
{
    private const string Home = @"C:\home";
    private static readonly DateTimeOffset Since = Lines.Now.AddDays(-90);

    private static MockFileData Recent(string text = "") => new(text) { LastWriteTime = Lines.Now.AddDays(-1).UtcDateTime };

    [Fact]
    public void Default_sources_cover_all_known_log_folders()
    {
        var sources = LogSources.Default(Home);

        Assert.Equal(
            [
                (Tool.Claude, @"C:\home\.claude\projects", "*.jsonl", SessionIdFrom.FileName),
                (Tool.Claude, @"C:\home\.config\claude\projects", "*.jsonl", SessionIdFrom.FileName),
                (Tool.Codex, @"C:\home\.codex\sessions", "rollout-*.jsonl", SessionIdFrom.FileName),
                (Tool.Codex, @"C:\home\.codex\archived_sessions", "rollout-*.jsonl", SessionIdFrom.FileName),
                (Tool.Copilot, @"C:\home\.copilot\session-state", "events.jsonl", SessionIdFrom.ParentFolder),
            ],
            sources.Select(s => (s.Tool, s.Root, s.Pattern, s.SessionFrom)));
    }

    [Fact]
    public void Finds_matching_files_in_subfolders_and_names_their_sessions()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [@"C:\home\.claude\projects\proj\abc.jsonl"] = Recent(),
            [@"C:\home\.claude\projects\proj\abc\subagents\agent-1.jsonl"] = Recent(),
            [@"C:\home\.claude\projects\proj\notes.txt"] = Recent(),
            [@"C:\home\.codex\sessions\2026\11\18\rollout-x.jsonl"] = Recent(),
            [@"C:\home\.codex\sessions\2026\11\18\history.jsonl"] = Recent(),
            [@"C:\home\.codex\archived_sessions\rollout-old.jsonl"] = Recent(),
            [@"C:\home\.copilot\session-state\sess-9\events.jsonl"] = Recent(),
            [@"C:\home\.copilot\session-state\sess-9\other.jsonl"] = Recent(),
        });

        var files = new LogScanner(fs, LogSources.Default(Home)).Scan(Since);

        Assert.Equal(
            [
                (Tool.Claude, "abc"),
                (Tool.Claude, "agent-1"),
                (Tool.Codex, "rollout-x"),
                (Tool.Codex, "rollout-old"),
                (Tool.Copilot, "sess-9"),
            ],
            files.Select(f => (f.Tool, f.SessionId)).OrderBy(x => x.Tool).ThenBy(x => x.SessionId == "agent-1"));
    }

    [Fact]
    public void Skips_files_not_modified_since_the_cutoff()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [@"C:\home\.claude\projects\p\new.jsonl"] = Recent(),
            [@"C:\home\.claude\projects\p\old.jsonl"] = new("") { LastWriteTime = Since.AddSeconds(-1).UtcDateTime },
            [@"C:\home\.claude\projects\p\edge.jsonl"] = new("") { LastWriteTime = Since.UtcDateTime },
        });

        var files = new LogScanner(fs, LogSources.Default(Home)).Scan(Since);

        Assert.Equal(["edge", "new"], files.Select(f => f.SessionId).Order());
    }

    [Fact]
    public void Missing_folders_give_no_files()
    {
        Assert.Empty(new LogScanner(new MockFileSystem(), LogSources.Default(Home)).Scan(Since));
    }

    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    [InlineData(typeof(DirectoryNotFoundException))]
    public void A_folder_that_fails_to_list_is_skipped(Type exception)
    {
        var fs = Substitute.For<IFileSystem>();
        fs.Directory.Exists(Arg.Any<string>()).Returns(true);
        fs.Directory.EnumerateFiles(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<EnumerationOptions>())
            .Throws((Exception)Activator.CreateInstance(exception)!);

        Assert.Empty(new LogScanner(fs, LogSources.Default(Home)).Scan(Since));
    }

    [Fact]
    public void Unexpected_errors_are_not_hidden()
    {
        var fs = Substitute.For<IFileSystem>();
        fs.Directory.Exists(Arg.Any<string>()).Returns(true);
        fs.Directory.EnumerateFiles(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<EnumerationOptions>())
            .Throws(new InvalidOperationException());

        Assert.Throws<InvalidOperationException>(() => new LogScanner(fs, LogSources.Default(Home)).Scan(Since));
    }
}
