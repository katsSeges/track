namespace Track.Core.Parsing;

/// <summary>
/// What a parser needs to continue a file after a restart. Only the fields a parser uses are set:
/// Claude keeps <see cref="RecentKeys"/>; Codex keeps <see cref="SessionId"/>, <see cref="Model"/> and
/// <see cref="Previous"/> (its last cumulative totals); Copilot keeps nothing.
/// </summary>
public sealed record ParserState(
    string? SessionId = null,
    string? Model = null,
    long[]? Previous = null,
    string[]? RecentKeys = null)
{
    public static readonly ParserState Empty = new();
}
