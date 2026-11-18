namespace Track.Core.Parsing;

/// <summary>
/// Reads GitHub Copilot logs (<c>~/.copilot/session-state/&lt;session&gt;/events.jsonl</c>), written by both
/// the Copilot app and the Copilot CLI. Usage comes from <c>session.usage_record</c> events.
/// <c>inputTokens</c> already includes cache reads and writes, and <c>outputTokens</c> includes reasoning,
/// so the parts are split apart to avoid counting tokens twice.
/// </summary>
public sealed class CopilotLogParser(string sessionId) : ILogParser
{
    /// <summary>Every event has a unique id, so nothing needs remembering between restarts.</summary>
    public ParserState SaveState() => ParserState.Empty;

    public UsageRecord? Parse(string? line)
    {
        if (line is null || !line.Contains("session.usage_record", StringComparison.Ordinal))
        {
            return null;
        }

        var root = JsonLine.Parse(line);
        var usage = JsonLine.Get(root, "data", "usage");
        var model = JsonLine.String(JsonLine.Get(usage, "model"));
        var eventId = JsonLine.String(JsonLine.Get(root, "id"));
        var timestamp = JsonLine.Timestamp(JsonLine.Get(root, "timestamp"));
        if (JsonLine.String(JsonLine.Get(root, "type")) != "session.usage_record"
            || model is null || eventId is null || timestamp is null)
        {
            return null;
        }

        var input = JsonLine.Long(JsonLine.Get(usage, "inputTokens"));
        var cacheRead = JsonLine.Long(JsonLine.Get(usage, "cacheReadTokens"));
        var cacheWrite = JsonLine.Long(JsonLine.Get(usage, "cacheWriteTokens"));
        var record = new UsageRecord(
            Tool.Copilot,
            sessionId,
            model,
            timestamp.Value,
            Input: Math.Max(0, input - cacheRead - cacheWrite),
            CacheWrite: cacheWrite,
            CacheRead: cacheRead,
            Output: JsonLine.Long(JsonLine.Get(usage, "outputTokens")),
            Reasoning: JsonLine.Long(JsonLine.Get(usage, "reasoningTokens")),
            DedupeKey: $"copilot:{eventId}");

        return record.Total > 0 ? record : null;
    }
}
