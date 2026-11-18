using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Text;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Track.Core.Ingest;
using Track.Core.Parsing;

namespace Track.Core.Tests.Ingest;

public class IncrementalLogReaderTests
{
    private const string Path = @"C:\logs\events.jsonl";
    private static readonly LogFile File = new(Tool.Copilot, Path, "sess");

    private readonly MockFileSystem _fs = new();

    private void Write(string text) => _fs.AddFile(Path, new MockFileData(text));

    private void Append(string text) => _fs.File.AppendAllText(Path, text);

    [Fact]
    public void Reads_all_complete_lines_the_first_time()
    {
        Write(Lines.Copilot("a") + "\n" + "{\"type\":\"other\"}\n" + Lines.Copilot("b") + "\n");

        var records = new IncrementalLogReader(_fs).ReadNew(File);

        Assert.Equal(["copilot:a", "copilot:b"], records.Select(r => r.DedupeKey));
        Assert.All(records, r => Assert.Equal("sess", r.SessionId));
    }

    [Fact]
    public void Later_reads_return_only_appended_lines()
    {
        Write(Lines.Copilot("a") + "\n");
        var reader = new IncrementalLogReader(_fs);
        reader.ReadNew(File);

        Append(Lines.Copilot("b") + "\n");

        Assert.Equal(["copilot:b"], reader.ReadNew(File).Select(r => r.DedupeKey));
    }

    [Fact]
    public void An_unchanged_file_reads_zero_bytes()
    {
        Write(Lines.Copilot("a") + "\n");
        var reader = new IncrementalLogReader(_fs);
        reader.ReadNew(File);
        var before = reader.BytesRead;

        Assert.Empty(reader.ReadNew(File));
        Assert.Equal(before, reader.BytesRead);
    }

    [Fact]
    public void A_half_written_last_line_waits_for_its_newline()
    {
        var line = Lines.Copilot("a");
        Write(line[..20]);
        var reader = new IncrementalLogReader(_fs);

        Assert.Empty(reader.ReadNew(File));

        Append(line[20..] + "\n");

        Assert.Equal(["copilot:a"], reader.ReadNew(File).Select(r => r.DedupeKey));
    }

    [Fact]
    public void Lines_split_across_read_chunks_are_put_back_together()
    {
        Write(Lines.Copilot("a") + "\n" + Lines.Copilot("b") + "\n\n" + Lines.Copilot("c") + "\n");

        var records = new IncrementalLogReader(_fs, chunkSize: 7).ReadNew(File);

        Assert.Equal(["copilot:a", "copilot:b", "copilot:c"], records.Select(r => r.DedupeKey));
    }

    [Fact]
    public void Windows_line_endings_and_a_byte_order_mark_are_handled()
    {
        _fs.AddFile(Path, new MockFileData(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(Lines.Copilot("a") + "\r\n" + Lines.Copilot("b") + "\r\n")).ToArray()));

        Assert.Equal(["copilot:a", "copilot:b"], new IncrementalLogReader(_fs).ReadNew(File).Select(r => r.DedupeKey));
    }

    [Fact]
    public void A_file_that_shrinks_is_read_again_from_the_start()
    {
        Write(Lines.Copilot("a") + "\n" + Lines.Copilot("b") + "\n");
        var reader = new IncrementalLogReader(_fs);
        reader.ReadNew(File);

        Write(Lines.Copilot("c") + "\n");

        Assert.Equal(["copilot:c"], reader.ReadNew(File).Select(r => r.DedupeKey));
    }

    [Fact]
    public void Parser_state_is_kept_between_reads_of_the_same_file()
    {
        var codexFile = new LogFile(Tool.Codex, @"C:\logs\rollout-1.jsonl", "rollout-1");
        _fs.AddFile(codexFile.Path, new MockFileData(Lines.Codex(1000) + "\n"));
        var reader = new IncrementalLogReader(_fs);
        reader.ReadNew(codexFile);

        _fs.File.AppendAllText(codexFile.Path, Lines.Codex(1500) + "\n");

        Assert.Equal(500, Assert.Single(reader.ReadNew(codexFile)).Total);
    }

    [Fact]
    public void Forgotten_files_are_read_from_the_start_next_time()
    {
        Write(Lines.Copilot("a") + "\n");
        var reader = new IncrementalLogReader(_fs);
        reader.ReadNew(File);
        Assert.Equal([Path], reader.TrackedPaths);

        reader.Forget([Path]);

        Assert.Empty(reader.TrackedPaths);
        Assert.Single(reader.ReadNew(File));
    }

    [Fact]
    public void Saved_cursors_record_offset_head_and_parser_state()
    {
        var content = Lines.Copilot("a") + "\n" + Lines.Copilot("b")[..10];
        Write(content);
        var reader = new IncrementalLogReader(_fs);
        reader.ReadNew(File);

        var cursor = Assert.Single(reader.SaveCursors());

        Assert.Equal((Path, Tool.Copilot, "sess"), (cursor.Path, cursor.Tool, cursor.SessionId));
        Assert.Equal(Lines.Copilot("a").Length + 1, cursor.Offset);
        Assert.Equal(Encoding.UTF8.GetBytes(content[..IncrementalLogReader.HeadLength]), cursor.Head);
        Assert.Same(ParserState.Empty, cursor.Parser);
    }

    [Fact]
    public void A_restored_reader_reads_only_what_was_appended_since()
    {
        Write(Lines.Copilot("a") + "\n");
        var before = new IncrementalLogReader(_fs);
        before.ReadNew(File);
        Append(Lines.Copilot("b") + "\n");

        var after = new IncrementalLogReader(_fs);
        after.RestoreCursors(before.SaveCursors());

        Assert.Equal(["copilot:b"], after.ReadNew(File).Select(r => r.DedupeKey));
        Assert.Equal(IncrementalLogReader.HeadLength + Lines.Copilot("b").Length + 1, after.BytesRead);
        Assert.Empty(after.ReadNew(File));
    }

    [Fact]
    public void A_file_replaced_while_stopped_is_read_again_from_the_start()
    {
        Write(Lines.Copilot("a") + "\n");
        var before = new IncrementalLogReader(_fs);
        before.ReadNew(File);

        Write(Lines.Copilot("z") + "\n" + Lines.Copilot("y") + "\n");
        var after = new IncrementalLogReader(_fs);
        after.RestoreCursors(before.SaveCursors());

        Assert.Equal(["copilot:z", "copilot:y"], after.ReadNew(File).Select(r => r.DedupeKey));
    }

    [Fact]
    public void A_file_that_shrank_while_stopped_is_read_again_from_the_start()
    {
        Write(Lines.Copilot("a") + "\n" + Lines.Copilot("b") + "\n");
        var before = new IncrementalLogReader(_fs);
        before.ReadNew(File);

        Write(Lines.Copilot("a") + "\n");
        var after = new IncrementalLogReader(_fs);
        after.RestoreCursors(before.SaveCursors());

        Assert.Equal(["copilot:a"], after.ReadNew(File).Select(r => r.DedupeKey));
    }

    [Fact]
    public void A_short_or_empty_file_gets_its_head_when_it_grows()
    {
        Write("");
        var reader = new IncrementalLogReader(_fs);
        reader.ReadNew(File);
        Assert.Empty(reader.SaveCursors().Single().Head);

        Append("{}\n");
        reader.ReadNew(File);
        Assert.Equal("{}\n"u8.ToArray(), reader.SaveCursors().Single().Head);

        // Restoring with a 3-byte head compares only those 3 bytes, so growth is not mistaken for a new file.
        Append(Lines.Copilot("a") + "\n");
        var after = new IncrementalLogReader(_fs);
        after.RestoreCursors(reader.SaveCursors());
        Assert.Equal(["copilot:a"], after.ReadNew(File).Select(r => r.DedupeKey));
    }

    [Fact]
    public void Restored_codex_state_continues_the_growth()
    {
        var codexFile = new LogFile(Tool.Codex, @"C:\logs\rollout-1.jsonl", "rollout-1");
        _fs.AddFile(codexFile.Path, new MockFileData(Lines.Codex(1000) + "\n"));
        var before = new IncrementalLogReader(_fs);
        before.ReadNew(codexFile);
        _fs.File.AppendAllText(codexFile.Path, Lines.Codex(1500) + "\n");

        var after = new IncrementalLogReader(_fs);
        after.RestoreCursors(before.SaveCursors());

        Assert.Equal(500, Assert.Single(after.ReadNew(codexFile)).Total);
    }

    [Fact]
    public void A_missing_file_gives_nothing()
    {
        Assert.Empty(new IncrementalLogReader(_fs).ReadNew(File));
    }

    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    public void A_file_that_cannot_be_opened_gives_nothing(Type exception)
    {
        var fs = Substitute.For<IFileSystem>();
        fs.File.Open(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)
            .Throws((Exception)Activator.CreateInstance(exception)!);

        Assert.Empty(new IncrementalLogReader(fs).ReadNew(File));
    }

    [Fact]
    public void Unexpected_errors_are_not_hidden()
    {
        var fs = Substitute.For<IFileSystem>();
        fs.File.Open(Arg.Any<string>(), Arg.Any<FileMode>(), Arg.Any<FileAccess>(), Arg.Any<FileShare>())
            .Throws(new InvalidOperationException());

        Assert.Throws<InvalidOperationException>(() => new IncrementalLogReader(fs).ReadNew(File));
    }
}
