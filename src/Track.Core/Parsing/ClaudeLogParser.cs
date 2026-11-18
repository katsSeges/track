namespace Track.Core.Parsing;

/// <summary>
/// Reads Claude Code logs (<c>~/.claude/projects/**/*.jsonl</c>).
/// Usage sits on assistant lines under <c>message.usage</c>. One API response is written once per
/// content block with identical usage, always close together in the same file, so the parser
/// remembers the last <see cref="RecentKeyCapacity"/> keys (<c>message.id</c> + <c>requestId</c>)
/// and returns only the first copy.
/// </summary>
public sealed class ClaudeLogParser : ILogParser
{
    public const int RecentKeyCapacity = 256;
    private const string SyntheticModel = "<synthetic>";

    private readonly string _fallbackSessionId;
    private readonly Queue<string> _recentOrder = new();
    private readonly HashSet<string> _recent = new(StringComparer.Ordinal);

    public ClaudeLogParser(string fallbackSessionId, ParserState? state = null)
    {
        _fallbackSessionId = fallbackSessionId;
        foreach (var key in state?.RecentKeys ?? [])
        {
            Remember(key);
        }
    }

    public UsageRecord? Parse(string? line)
    {
        // Cheap pre-filter: most lines are prompts, tool results and other events without usage.
        if (line is null || !line.Contains("\"usage\"", StringComparison.Ordinal))
        {
            return null;
        }

        var root = JsonLine.Parse(line);
        var message = JsonLine.Get(root, "message");
        var usage = JsonLine.Get(message, "usage");
        var model = JsonLine.String(JsonLine.Get(message, "model"));
        var messageId = JsonLine.String(JsonLine.Get(message, "id"));
        var timestamp = JsonLine.Timestamp(JsonLine.Get(root, "timestamp"));
        if (usage is null || model is null || model == SyntheticModel || messageId is null || timestamp is null)
        {
            return null;
        }

        var requestId = JsonLine.String(JsonLine.Get(root, "requestId"));
        var key = requestId is null ? $"claude:{messageId}" : $"claude:{messageId}:{requestId}";
        if (_recent.Contains(key))
        {
            return null; // another content block of a response already counted
        }

        var record = new UsageRecord(
            Tool.Claude,
            JsonLine.String(JsonLine.Get(root, "sessionId")) ?? _fallbackSessionId,
            model,
            timestamp.Value,
            Input: JsonLine.Long(JsonLine.Get(usage, "input_tokens")),
            CacheWrite: JsonLine.Long(JsonLine.Get(usage, "cache_creation_input_tokens")),
            CacheRead: JsonLine.Long(JsonLine.Get(usage, "cache_read_input_tokens")),
            Output: JsonLine.Long(JsonLine.Get(usage, "output_tokens")),
            Reasoning: JsonLine.Long(JsonLine.Get(usage, "output_tokens_details", "thinking_tokens")),
            DedupeKey: key);

        if (record.Total == 0)
        {
            return null;
        }

        Remember(key);
        return record;
    }

    public ParserState SaveState() => new(RecentKeys: [.. _recentOrder]);

    private void Remember(string key)
    {
        if (!_recent.Add(key))
        {
            return;
        }

        _recentOrder.Enqueue(key);
        if (_recentOrder.Count > RecentKeyCapacity)
        {
            _recent.Remove(_recentOrder.Dequeue());
        }
    }
}
