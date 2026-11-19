namespace Track.Core.Ingest;

/// <summary>
/// One refresh: find recent log files, read what is new in them, keep the store inside the retention
/// window. With a <see cref="SnapshotStore"/>, the first refresh continues from the saved snapshot and
/// later refreshes save a new one (at most once per <see cref="SaveInterval"/>) when usage changes.
/// </summary>
public sealed class UsageIngestor(
    LogScanner scanner,
    IncrementalLogReader reader,
    UsageStore store,
    TimeProvider time,
    SnapshotStore? snapshots = null,
    DocumentLogReader? documents = null)
{
    public static readonly TimeSpan SaveInterval = TimeSpan.FromMinutes(1);

    /// <summary>Retain sessions for the analytics window so range-overlapping totals remain available.</summary>
    public static readonly TimeSpan SessionRetention = TimeSpan.FromDays(RetentionWindow.Days);

    private bool _started;
    private DateTimeOffset _lastSave = DateTimeOffset.MinValue;

    public UsageStore Store => store;

    /// <summary>True when the first refresh continued from a saved snapshot.</summary>
    public bool RestoredFromSnapshot { get; private set; }

    /// <summary>Returns how many new records were added.</summary>
    public int Refresh()
    {
        if (!_started)
        {
            _started = true;
            Restore();
        }

        var now = time.GetUtcNow();
        var keepFrom = RetentionWindow.Start(now);
        var files = scanner.Scan(keepFrom);

        var added = 0;
        var documentChanged = false;
        foreach (var file in files.Where(file => file.Mode == LogReadMode.Lines))
        {
            added += store.Add(reader.ReadNew(file));
        }

        if (documents is not null)
        {
            foreach (var file in files.Where(file => file.Mode != LogReadMode.Lines))
            {
                if (documents.ReadChanged(file, keepFrom) is { } changed)
                {
                    documentChanged = true;
                    added += store.ReplaceDocumentContribution(changed.Path, changed.Records);
                }
            }

            documents.Prune(keepFrom);
        }

        var current = files
            .Where(file => file.Mode == LogReadMode.Lines)
            .Select(file => file.Path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        reader.Forget(reader.TrackedPaths.Where(p => !current.Contains(p)).ToList());
        store.Prune(keepFrom, now - SessionRetention);

        if ((added > 0 || documentChanged) && now - _lastSave >= SaveInterval)
        {
            SaveSnapshot();
        }

        return added;
    }

    /// <summary>Saves now (e.g. when the app exits). Returns false when there is nothing to save to or it failed.</summary>
    public bool SaveSnapshot()
    {
        if (snapshots is null)
        {
            return false;
        }

        _lastSave = time.GetUtcNow();
        var (buckets, sessions) = store.Save();
        return snapshots.Save(new Snapshot(SnapshotStore.CurrentVersion, _lastSave, buckets, sessions, reader.SaveCursors())
        {
            DocumentCursors = documents?.SaveCursors() ?? [],
        });
    }

    private void Restore()
    {
        if (snapshots?.Load() is not { } snapshot)
        {
            return;
        }

        store.Restore(snapshot.Buckets, snapshot.Sessions);
        reader.RestoreCursors(snapshot.Cursors);
        store.RestoreDocumentContributions(snapshot.DocumentCursors);
        documents?.RestoreCursors(snapshot.DocumentCursors);
        _lastSave = snapshot.SavedUtc;
        RestoredFromSnapshot = true;
    }
}
