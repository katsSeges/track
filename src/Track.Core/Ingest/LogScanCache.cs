using System.IO.Abstractions;

namespace Track.Core.Ingest;

/// <summary>
/// Recent log files for the 5-second live checks without walking every log folder each time.
/// A full scan runs at most every <see cref="FullScanEvery"/>, or at once after <see cref="MarkDirty"/>
/// (the app calls it when a file watcher sees a new log). In between, only the known files' write
/// times are read again, which is cheap.
/// </summary>
public sealed class LogScanCache(IFileSystem fileSystem, LogScanner scanner, TimeProvider time)
{
    public static readonly TimeSpan FullScanEvery = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan Window = TimeSpan.FromDays(1);

    private IReadOnlyList<LogFile> _files = [];
    private DateTimeOffset _lastFullScan = DateTimeOffset.MinValue;
    private int _dirty = 1;

    /// <summary>How many full folder scans ran (to check the cache works).</summary>
    public int FullScans { get; private set; }

    /// <summary>A new log file may exist: the next <see cref="Recent"/> does a full scan. Safe from any thread.</summary>
    public void MarkDirty() => Interlocked.Exchange(ref _dirty, 1);

    /// <summary>Log files written within <see cref="Window"/>, with fresh write times.</summary>
    public IReadOnlyList<LogFile> Recent()
    {
        var now = time.GetUtcNow();
        if (Interlocked.Exchange(ref _dirty, 0) == 1 || now - _lastFullScan >= FullScanEvery)
        {
            _files = scanner.Scan(now - Window);
            _lastFullScan = now;
            FullScans++;
            return _files;
        }

        // A file deleted since the scan reports 1601-01-01 and drops out here.
        _files = _files
            .Select(f => f with { LastWriteUtc = new DateTimeOffset(fileSystem.File.GetLastWriteTimeUtc(f.Path), TimeSpan.Zero) })
            .Where(f => f.LastWriteUtc >= now - Window)
            .ToList();
        return _files;
    }
}
