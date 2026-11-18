using System.Text.Json.Serialization;
using Track.Core.Parsing;

namespace Track.Core.Ingest;

/// <summary>Summed token parts (same meaning as on <see cref="UsageRecord"/>) and how many calls they came from.</summary>
public readonly record struct TokenCounts(long Input, long CacheWrite, long CacheRead, long Output, long Reasoning, long Calls)
{
    [JsonIgnore]
    public long Total => Input + CacheWrite + CacheRead + Output;

    public static TokenCounts From(UsageRecord r) => new(r.Input, r.CacheWrite, r.CacheRead, r.Output, r.Reasoning, 1);

    public static TokenCounts operator +(TokenCounts a, TokenCounts b) => new(
        a.Input + b.Input,
        a.CacheWrite + b.CacheWrite,
        a.CacheRead + b.CacheRead,
        a.Output + b.Output,
        a.Reasoning + b.Reasoning,
        a.Calls + b.Calls);
}

/// <summary>Usage of one model in one clock hour (UTC). Hours keep every period exact while staying small.</summary>
public readonly record struct UsageBucketKey(Tool Tool, string Model, DateTimeOffset HourUtc)
{
    public static UsageBucketKey For(UsageRecord r)
    {
        var ticks = r.Timestamp.UtcTicks;
        return new(r.Tool, r.Model, new DateTimeOffset(ticks - (ticks % TimeSpan.TicksPerHour), TimeSpan.Zero));
    }
}

/// <summary>Usage of one agent session, with when it was first and last used.</summary>
public readonly record struct SessionUsage(TokenCounts Tokens, DateTimeOffset FirstUtc, DateTimeOffset LastUtc)
{
    public SessionUsage With(UsageRecord r) => new(
        Tokens + TokenCounts.From(r),
        r.Timestamp < FirstUtc ? r.Timestamp : FirstUtc,
        r.Timestamp > LastUtc ? r.Timestamp : LastUtc);
}

public readonly record struct SessionKey(Tool Tool, string SessionId);
