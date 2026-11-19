using Track.Core.Parsing;

namespace Track.Core.Ingest;

/// <summary>
/// Usage totals for the retention window: per tool, model and hour, plus per session for recent
/// sessions. Raw records are not kept, which keeps memory and the saved snapshot small.
/// Duplicates are removed by the parsers before records get here.
/// </summary>
public sealed class UsageStore
{
    private readonly Dictionary<UsageBucketKey, TokenCounts> _buckets = [];
    private readonly Dictionary<SessionKey, SessionUsage> _sessions = [];
    private readonly Dictionary<string, DocumentContribution> _documentContributions = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<UsageBucketKey, TokenCounts> Buckets => _buckets;

    public IReadOnlyDictionary<SessionKey, SessionUsage> Sessions => _sessions;

    public IReadOnlyDictionary<string, DocumentContribution> DocumentContributions => _documentContributions;

    /// <summary>Adds records; returns how many were added.</summary>
    public int Add(IEnumerable<UsageRecord> records)
    {
        var added = 0;
        foreach (var record in records)
        {
            var bucket = UsageBucketKey.For(record);
            _buckets[bucket] = _buckets.GetValueOrDefault(bucket) + TokenCounts.From(record);

            var session = new SessionKey(record.Tool, record.SessionId);
            _sessions[session] = _sessions.TryGetValue(session, out var usage)
                ? usage.With(record)
                : new SessionUsage(TokenCounts.From(record), record.Timestamp, record.Timestamp);
            added++;
        }

        return added;
    }

    /// <summary>Replaces one whole-file contribution without changing other files' totals. Returns its record count.</summary>
    public int ReplaceDocumentContribution(string path, IReadOnlyList<UsageRecord> records)
    {
        var next = DocumentContribution.From(records);
        var affected = new HashSet<SessionKey>();
        if (_documentContributions.TryGetValue(path, out var previous))
        {
            foreach (var bucket in previous.Buckets)
            {
                ChangeBucket(bucket, -1);
            }

            affected.UnionWith(previous.Sessions.Select(s => new SessionKey(s.Tool, s.SessionId)));
        }

        _documentContributions[path] = next;
        foreach (var bucket in next.Buckets)
        {
            ChangeBucket(bucket, +1);
        }

        affected.UnionWith(next.Sessions.Select(s => new SessionKey(s.Tool, s.SessionId)));

        // A session can be spread over several documents: sum what each one says about it (one pass over the
        // per-session sums, not over raw records).
        var combined = new Dictionary<SessionKey, SessionUsage>();
        foreach (var session in _documentContributions.Values.SelectMany(c => c.Sessions))
        {
            var key = new SessionKey(session.Tool, session.SessionId);
            if (affected.Contains(key))
            {
                combined[key] = combined.TryGetValue(key, out var sum) ? Merge(sum, session.Usage) : session.Usage;
            }
        }

        foreach (var key in affected)
        {
            if (combined.TryGetValue(key, out var usage))
            {
                _sessions[key] = usage;
            }
            else
            {
                _sessions.Remove(key);
            }
        }

        return next.Records;
    }

    /// <summary>Restores document ownership metadata without adding the already-snapshotted totals again.</summary>
    public void RestoreDocumentContributions(IEnumerable<DocumentCursor> cursors)
    {
        foreach (var cursor in cursors)
        {
            _documentContributions[cursor.Path] = cursor.Contribution ?? DocumentContribution.Empty;
        }
    }

    /// <summary>Puts back saved totals (from a snapshot), adding to anything already there.</summary>
    public void Restore(IReadOnlyList<Snapshot.Bucket> buckets, IReadOnlyList<Snapshot.Session> sessions)
    {
        for (var i = 0; i < buckets.Count; i++)
        {
            var b = buckets[i];
            var key = new UsageBucketKey(b.Tool, b.Model, b.HourUtc);
            _buckets[key] = _buckets.GetValueOrDefault(key) + b.Tokens;
        }

        for (var i = 0; i < sessions.Count; i++)
        {
            _sessions[new SessionKey(sessions[i].Tool, sessions[i].SessionId)] = sessions[i].Usage;
        }
    }

    /// <summary>The totals in snapshot form.</summary>
    public (List<Snapshot.Bucket> Buckets, List<Snapshot.Session> Sessions) Save() => (
        _buckets.Select(b => new Snapshot.Bucket(b.Key.Tool, b.Key.Model, b.Key.HourUtc, b.Value)).ToList(),
        _sessions.Select(s => new Snapshot.Session(s.Key.Tool, s.Key.SessionId, s.Value)).ToList());

    /// <summary>Drops hours before <paramref name="keepFrom"/> and sessions last used before <paramref name="sessionsFrom"/>.</summary>
    public void Prune(DateTimeOffset keepFrom, DateTimeOffset sessionsFrom)
    {
        _buckets.Keys.Where(k => k.HourUtc < keepFrom).ToList().ForEach(k => _buckets.Remove(k));
        _sessions.Where(p => p.Value.LastUtc < sessionsFrom).Select(p => p.Key).ToList().ForEach(k => _sessions.Remove(k));

        // Only a document with an hour that just fell out of the window is rebuilt; the others cost one look.
        foreach (var path in _documentContributions.Keys.ToList())
        {
            var contribution = _documentContributions[path];
            if (contribution.OldestHourUtc < keepFrom)
            {
                _documentContributions[path] = contribution.From(keepFrom);
            }
        }
    }

    private void ChangeBucket(Snapshot.Bucket bucket, int sign)
    {
        var key = new UsageBucketKey(bucket.Tool, bucket.Model, bucket.HourUtc);
        var current = _buckets.GetValueOrDefault(key);
        var changed = sign > 0 ? current + bucket.Tokens : current - bucket.Tokens;
        if (changed.Calls <= 0)
        {
            _buckets.Remove(key);
        }
        else
        {
            _buckets[key] = changed;
        }
    }

    private static SessionUsage Merge(SessionUsage a, SessionUsage b) => new(
        a.Tokens + b.Tokens,
        a.FirstUtc < b.FirstUtc ? a.FirstUtc : b.FirstUtc,
        a.LastUtc > b.LastUtc ? a.LastUtc : b.LastUtc);
}

/// <summary>
/// What one whole-file document (a Gemini chat, a Cline/Roo task, OpenCode's database) adds to the totals,
/// already summed per hour bucket and per session, so replacing or saving it never walks raw records.
/// </summary>
public sealed record DocumentContribution(
    IReadOnlyList<Snapshot.Bucket> Buckets,
    IReadOnlyList<Snapshot.Session> Sessions,
    int Records)
{
    public static readonly DocumentContribution Empty = new([], [], 0);

    /// <summary>The earliest hour it covers (<see cref="DateTimeOffset.MaxValue"/> when empty).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public DateTimeOffset OldestHourUtc => Buckets.Count == 0 ? DateTimeOffset.MaxValue : Buckets.Min(b => b.HourUtc);

    /// <summary>Sums records, counting each <see cref="UsageRecord.DedupeKey"/> once.</summary>
    public static DocumentContribution From(IEnumerable<UsageRecord> records)
    {
        var buckets = new Dictionary<UsageBucketKey, TokenCounts>();
        var sessions = new Dictionary<SessionKey, SessionUsage>();
        var count = 0;
        foreach (var record in records.DistinctBy(record => record.DedupeKey, StringComparer.Ordinal))
        {
            var bucket = UsageBucketKey.For(record);
            buckets[bucket] = buckets.GetValueOrDefault(bucket) + TokenCounts.From(record);
            var session = new SessionKey(record.Tool, record.SessionId);
            sessions[session] = sessions.TryGetValue(session, out var usage)
                ? usage.With(record)
                : new SessionUsage(TokenCounts.From(record), record.Timestamp, record.Timestamp);
            count++;
        }

        return new(
            buckets.Select(b => new Snapshot.Bucket(b.Key.Tool, b.Key.Model, b.Key.HourUtc, b.Value)).ToList(),
            sessions.Select(s => new Snapshot.Session(s.Key.Tool, s.Key.SessionId, s.Value)).ToList(),
            count);
    }

    /// <summary>Only the hours from <paramref name="keepFrom"/> on, and the sessions last used then.</summary>
    public DocumentContribution From(DateTimeOffset keepFrom) => this with
    {
        Buckets = Buckets.Where(b => b.HourUtc >= keepFrom).ToList(),
        Sessions = Sessions.Where(s => s.Usage.LastUtc >= keepFrom).ToList(),
    };
}
