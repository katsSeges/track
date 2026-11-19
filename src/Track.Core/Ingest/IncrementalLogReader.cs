using System.IO.Abstractions;
using Track.Core.Parsing;

namespace Track.Core.Ingest;

/// <summary>Where reading of one file stopped, saved in the snapshot so a restart can continue there.</summary>
/// <param name="Head">The first bytes of the file when it was first read, to notice a replaced file.</param>
public sealed record FileCursor(string Path, Tool Tool, string SessionId, long Offset, byte[] Head, ParserState Parser);

/// <summary>
/// Reads only what was appended to each log file since the last read. Remembers a byte offset and a
/// parser per file, consumes complete lines only (a half-written last line is read again next time),
/// and starts over when a file shrinks or, after a restart, when its first bytes changed. Files are
/// opened so the agents can keep writing, renaming or deleting them; a file that cannot be opened is
/// skipped this round and tried again later.
/// </summary>
public sealed class IncrementalLogReader(IFileSystem fileSystem, int chunkSize = 1 << 20)
{
    /// <summary>How many leading bytes identify a file.</summary>
    public const int HeadLength = 64;

    private readonly Dictionary<string, Cursor> _cursors = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Total bytes read from disk by this reader (to check that unchanged files cost nothing).</summary>
    public long BytesRead { get; private set; }

    /// <summary>Files currently tracked.</summary>
    public IReadOnlyCollection<string> TrackedPaths => _cursors.Keys;

    /// <summary>Tracked files with the tool each belongs to.</summary>
    public IEnumerable<LogFile> TrackedFiles => _cursors.Values.Select(c => c.File);

    public IReadOnlyList<UsageRecord> ReadNew(LogFile file)
    {
        var records = new List<UsageRecord>();
        try
        {
            using var stream = fileSystem.File.Open(file.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (!_cursors.TryGetValue(file.Path, out var cursor)
                || stream.Length < cursor.Offset
                || (cursor.NeedsHeadCheck && !ReadHead(stream, cursor.Head.Length).SequenceEqual(cursor.Head)))
            {
                cursor = new Cursor(file, LogParsers.Create(file.Tool, file.SessionId), 0, []);
                _cursors[file.Path] = cursor;
            }

            cursor.NeedsHeadCheck = false;
            if (cursor.Head.Length == 0 && stream.Length > 0)
            {
                cursor.Head = ReadHead(stream, HeadLength);
            }

            if (stream.Length > cursor.Offset)
            {
                stream.Seek(cursor.Offset, SeekOrigin.Begin);
                ReadLines(stream, cursor, records);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Locked, deleted or unreadable right now: keep what we had and try again next refresh.
        }

        return records;
    }

    /// <summary>Stops tracking files that no longer exist or are out of range.</summary>
    public void Forget(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            _cursors.Remove(path);
        }
    }

    /// <summary>Where every tracked file stopped, for the snapshot.</summary>
    public IReadOnlyList<FileCursor> SaveCursors() =>
        _cursors.Values.Select(c => new FileCursor(c.File.Path, c.File.Tool, c.File.SessionId, c.Offset, c.Head, c.Parser.SaveState())).ToList();

    /// <summary>Continues from saved cursors. Each file's first bytes are checked on its next read.</summary>
    public void RestoreCursors(IEnumerable<FileCursor> cursors)
    {
        foreach (var c in cursors)
        {
            var file = new LogFile(c.Tool, c.Path, c.SessionId);
            _cursors[c.Path] = new Cursor(file, LogParsers.Create(c.Tool, c.SessionId, c.Parser), c.Offset, c.Head) { NeedsHeadCheck = true };
        }
    }

    private byte[] ReadHead(Stream stream, int length)
    {
        var head = new byte[Math.Min(length, stream.Length)];
        stream.Seek(0, SeekOrigin.Begin);
        stream.ReadExactly(head);
        BytesRead += head.Length;
        return head;
    }

    private void ReadLines(Stream stream, Cursor cursor, List<UsageRecord> records) =>
        cursor.Offset += LineReader.ReadCompleteLines(
            stream,
            line =>
            {
                if (cursor.Parser.Parse(line) is { } record)
                {
                    records.Add(record);
                }
            },
            chunkSize,
            read => BytesRead += read);

    private sealed class Cursor(LogFile file, ILogParser parser, long offset, byte[] head)
    {
        public LogFile File { get; } = file;

        public ILogParser Parser { get; } = parser;

        public long Offset { get; set; } = offset;

        public byte[] Head { get; set; } = head;

        public bool NeedsHeadCheck { get; set; }
    }
}
