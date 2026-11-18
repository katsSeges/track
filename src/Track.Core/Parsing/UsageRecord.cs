namespace Track.Core.Parsing;

/// <summary>
/// One billed model call, normalized so the token parts never overlap:
/// <see cref="Total"/> = <see cref="Input"/> + <see cref="CacheWrite"/> + <see cref="CacheRead"/> + <see cref="Output"/>.
/// </summary>
/// <param name="Input">Input tokens that were neither written to nor read from the prompt cache.</param>
/// <param name="CacheWrite">Input tokens written to the prompt cache.</param>
/// <param name="CacheRead">Input tokens served from the prompt cache ("cache hits").</param>
/// <param name="Output">Output tokens, reasoning included.</param>
/// <param name="Reasoning">The part of <see cref="Output"/> spent on reasoning (informational, already in Output).</param>
/// <param name="DedupeKey">Same key = same call logged twice; keep the first.</param>
public sealed record UsageRecord(
    Tool Tool,
    string SessionId,
    string Model,
    DateTimeOffset Timestamp,
    long Input,
    long CacheWrite,
    long CacheRead,
    long Output,
    long Reasoning,
    string DedupeKey)
{
    public long Total => Input + CacheWrite + CacheRead + Output;
}
