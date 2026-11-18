using System.IO.Abstractions;

namespace Track.Core.Ingest;

/// <summary>Finds agent log files that changed recently enough to matter.</summary>
public sealed class LogScanner(IFileSystem fileSystem, IReadOnlyList<LogSource> sources)
{
    private static readonly EnumerationOptions Recursive = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        MatchCasing = MatchCasing.CaseInsensitive,
    };

    /// <summary>Log files modified at or after <paramref name="modifiedSince"/>. Missing folders are skipped.</summary>
    public IReadOnlyList<LogFile> Scan(DateTimeOffset modifiedSince)
    {
        var found = new List<LogFile>();
        foreach (var source in sources)
        {
            foreach (var path in Enumerate(source))
            {
                // A file deleted after listing reports 1601-01-01, which is never recent enough.
                var written = new DateTimeOffset(fileSystem.File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
                if (written >= modifiedSince)
                {
                    found.Add(new LogFile(source.Tool, path, SessionId(source, path), written));
                }
            }
        }

        return found;
    }

    private IEnumerable<string> Enumerate(LogSource source)
    {
        try
        {
            return fileSystem.Directory.Exists(source.Root)
                ? fileSystem.Directory.EnumerateFiles(source.Root, source.Pattern, Recursive).ToList()
                : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private string SessionId(LogSource source, string path) => source.SessionFrom == SessionIdFrom.ParentFolder
        ? fileSystem.Path.GetFileName(fileSystem.Path.GetDirectoryName(path))!
        : fileSystem.Path.GetFileNameWithoutExtension(path);
}
