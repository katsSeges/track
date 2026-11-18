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

    public IReadOnlyDictionary<UsageBucketKey, TokenCounts> Buckets => _buckets;

    public IReadOnlyDictionary<SessionKey, SessionUsage> Sessions => _sessions;

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
    }
}
