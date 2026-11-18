using System.Text.Json;

namespace Track.Core.Parsing;

/// <summary>
/// Reads Codex logs (<c>~/.codex/sessions/**/rollout-*.jsonl</c> and <c>~/.codex/archived_sessions</c>).
/// Stateful: create one per file. <c>token_count</c> events carry cumulative totals for the session,
/// sometimes repeated; each record is the growth since the previous event. In these totals
/// <c>input_tokens</c> already includes cached (and cache-write) tokens and <c>output_tokens</c>
/// includes reasoning, so the parts are split apart to avoid counting tokens twice.
/// </summary>
public sealed class CodexLogParser : ILogParser
{
    public const string UnknownModel = "unknown";

    private string _sessionId;
    private string _model;
    private Totals _previous;

    public CodexLogParser(string fallbackSessionId, ParserState? state = null)
    {
        _sessionId = state?.SessionId ?? fallbackSessionId;
        _model = state?.Model ?? UnknownModel;
        _previous = Totals.FromArray(state?.Previous);
    }

    public ParserState SaveState() => new(SessionId: _sessionId, Model: _model, Previous: _previous.ToArray());

    public UsageRecord? Parse(string? line)
    {
        if (line is null
            || !(line.Contains("token_count", StringComparison.Ordinal)
                 || line.Contains("turn_context", StringComparison.Ordinal)
                 || line.Contains("session_meta", StringComparison.Ordinal)))
        {
            return null;
        }

        var root = JsonLine.Parse(line);
        var payload = JsonLine.Get(root, "payload");
        switch (JsonLine.String(JsonLine.Get(root, "type")))
        {
            case "session_meta":
                _sessionId = JsonLine.String(JsonLine.Get(payload, "id")) ?? _sessionId;
                return null;
            case "turn_context":
                _model = JsonLine.String(JsonLine.Get(payload, "model")) ?? _model;
                return null;
            case "event_msg" when JsonLine.String(JsonLine.Get(payload, "type")) == "token_count":
                return FromTokenCount(root, payload);
            default:
                return null;
        }
    }

    private UsageRecord? FromTokenCount(JsonElement? root, JsonElement? payload)
    {
        var totalUsage = JsonLine.Get(payload, "info", "total_token_usage");
        var timestamp = JsonLine.Timestamp(JsonLine.Get(root, "timestamp"));
        if (totalUsage is null || timestamp is null)
        {
            return null;
        }

        var current = Totals.From(totalUsage);
        if (current.Total == _previous.Total)
        {
            return null; // repeated event, nothing new
        }

        // Counters going backwards means the session's totals restarted: count the new totals from zero.
        var delta = current.Total < _previous.Total ? current : current - _previous;
        _previous = current;

        var uncachedInput = Math.Max(0, delta.Input - delta.CacheRead - delta.CacheWrite);
        return new UsageRecord(
            Tool.Codex,
            _sessionId,
            _model,
            timestamp.Value,
            Input: uncachedInput,
            CacheWrite: delta.CacheWrite,
            CacheRead: delta.CacheRead,
            Output: delta.Output,
            Reasoning: delta.Reasoning,
            DedupeKey: $"codex:{_sessionId}:{current.Total}");
    }

    private readonly record struct Totals(long Input, long CacheRead, long CacheWrite, long Output, long Reasoning, long Total)
    {
        public static readonly Totals Zero = new(0, 0, 0, 0, 0, 0);

        public static Totals From(JsonElement? usage) => new(
            JsonLine.Long(JsonLine.Get(usage, "input_tokens")),
            JsonLine.Long(JsonLine.Get(usage, "cached_input_tokens")),
            JsonLine.Long(JsonLine.Get(usage, "cache_write_input_tokens")),
            JsonLine.Long(JsonLine.Get(usage, "output_tokens")),
            JsonLine.Long(JsonLine.Get(usage, "reasoning_output_tokens")),
            JsonLine.Long(JsonLine.Get(usage, "total_tokens")));

        /// <summary>Restores saved totals; anything but exactly six numbers means "start from zero".</summary>
        public static Totals FromArray(long[]? values) => values is { Length: 6 }
            ? new(values[0], values[1], values[2], values[3], values[4], values[5])
            : Zero;

        public long[] ToArray() => [Input, CacheRead, CacheWrite, Output, Reasoning, Total];

        public static Totals operator -(Totals a, Totals b) => new(
            Math.Max(0, a.Input - b.Input),
            Math.Max(0, a.CacheRead - b.CacheRead),
            Math.Max(0, a.CacheWrite - b.CacheWrite),
            Math.Max(0, a.Output - b.Output),
            Math.Max(0, a.Reasoning - b.Reasoning),
            a.Total - b.Total);
    }
}
