using System.Text.Json;
using Track.Core.Parsing;

namespace Track.Core.Activity;

public enum ActivityKind
{
    /// <summary>The session's first moment.</summary>
    SessionStart,

    /// <summary>The user sent a prompt: the agent starts working and the task timer starts.</summary>
    TurnStart,

    /// <summary>The agent started a step without a new prompt (Copilot's <c>assistant.turn_start</c>).</summary>
    StepStart,

    /// <summary>A step ended; the task is over unless another step follows within a short grace period.</summary>
    StepEnd,

    /// <summary>The agent finished or was interrupted.</summary>
    TurnEnd,

    /// <summary>The session was closed.</summary>
    SessionEnd,

    /// <summary>A model name was seen.</summary>
    ModelSeen,
}

public sealed record ActivityEvent(Tool Tool, string SessionId, ActivityKind Kind, DateTimeOffset At, string? Model = null);

/// <summary>Turns log lines of one file into work start/finish markers. One instance per file.</summary>
public interface IActivityParser
{
    IReadOnlyList<ActivityEvent> Parse(string line);
}

public static class ActivityParsers
{
    public static IActivityParser Create(Tool tool, string fallbackSessionId) => tool switch
    {
        Tool.Claude => new ClaudeActivityParser(fallbackSessionId),
        Tool.Codex => new CodexActivityParser(fallbackSessionId),
        Tool.Copilot => new CopilotActivityParser(fallbackSessionId),
        _ => throw new ArgumentOutOfRangeException(nameof(tool), tool, "Unknown tool."),
    };
}

/// <summary>
/// Claude Code: a turn starts at a user line with text that is not a tool result or a meta line, and ends
/// at an assistant line with <c>stop_reason: end_turn</c>, a <c>stop_hook_summary</c> line, or the
/// "[Request interrupted by user]" line. The session starts at the file's first timestamp.
/// </summary>
public sealed class ClaudeActivityParser(string fallbackSessionId) : IActivityParser
{
    private const string Interrupted = "[Request interrupted by user";
    private bool _started;
    private bool _modelKnown;

    public IReadOnlyList<ActivityEvent> Parse(string line)
    {
        // Cheap pre-filter (most lines are tool calls and results): after the first line, only prompts,
        // turn ends and, until a model is known, assistant lines can matter.
        if (_started
            && (!line.Contains("\"type\":\"user\"", StringComparison.Ordinal) || line.Contains("\"tool_result\"", StringComparison.Ordinal))
            && !line.Contains("end_turn", StringComparison.Ordinal)
            && !line.Contains("stop_hook_summary", StringComparison.Ordinal)
            && (_modelKnown || !line.Contains("\"model\"", StringComparison.Ordinal)))
        {
            return [];
        }

        var root = JsonLine.Parse(line);
        if (JsonLine.Timestamp(JsonLine.Get(root, "timestamp")) is not { } at)
        {
            return [];
        }

        var session = JsonLine.String(JsonLine.Get(root, "sessionId")) ?? fallbackSessionId;
        var events = new List<ActivityEvent>();
        if (!_started)
        {
            _started = true;
            events.Add(new(Tool.Claude, session, ActivityKind.SessionStart, at));
        }

        var message = JsonLine.Get(root, "message");
        switch (JsonLine.String(JsonLine.Get(root, "type")))
        {
            case "user" when JsonLine.Get(root, "isMeta") is not { ValueKind: JsonValueKind.True }:
                if (PromptText(JsonLine.Get(message, "content")) is { } text)
                {
                    events.Add(new(Tool.Claude, session, text.StartsWith(Interrupted, StringComparison.Ordinal) ? ActivityKind.TurnEnd : ActivityKind.TurnStart, at));
                }

                break;
            case "assistant":
                if (JsonLine.String(JsonLine.Get(message, "model")) is { } model && model != "<synthetic>")
                {
                    _modelKnown = true;
                    events.Add(new(Tool.Claude, session, ActivityKind.ModelSeen, at, model));
                }

                if (JsonLine.String(JsonLine.Get(message, "stop_reason")) == "end_turn")
                {
                    events.Add(new(Tool.Claude, session, ActivityKind.TurnEnd, at));
                }

                break;
            case "system" when JsonLine.String(JsonLine.Get(root, "subtype")) == "stop_hook_summary":
                events.Add(new(Tool.Claude, session, ActivityKind.TurnEnd, at));
                break;
        }

        return events;
    }

    /// <summary>The text of a typed prompt, or null for tool results and empty content.</summary>
    private static string? PromptText(JsonElement? content)
    {
        if (JsonLine.String(content) is { } text)
        {
            return text;
        }

        if (content is not { ValueKind: JsonValueKind.Array } items)
        {
            return null;
        }

        string? first = null;
        foreach (var item in items.EnumerateArray())
        {
            var type = JsonLine.String(JsonLine.Get(item, "type"));
            if (type == "tool_result")
            {
                return null;
            }

            first ??= type == "text" ? JsonLine.String(JsonLine.Get(item, "text")) : null;
        }

        return first;
    }
}

/// <summary>Codex: <c>task_started</c> → working; <c>task_complete</c> or <c>turn_aborted</c> → done.</summary>
public sealed class CodexActivityParser(string fallbackSessionId) : IActivityParser
{
    private static readonly string[] Markers = ["session_meta", "turn_context", "task_started", "task_complete", "turn_aborted"];
    private string _session = fallbackSessionId;

    public IReadOnlyList<ActivityEvent> Parse(string line)
    {
        if (!Markers.Any(m => line.Contains(m, StringComparison.Ordinal)))
        {
            return []; // most lines are tool calls, outputs and token counts
        }

        var root = JsonLine.Parse(line);
        if (JsonLine.Timestamp(JsonLine.Get(root, "timestamp")) is not { } at)
        {
            return [];
        }

        var payload = JsonLine.Get(root, "payload");
        switch (JsonLine.String(JsonLine.Get(root, "type")))
        {
            case "session_meta":
                _session = JsonLine.String(JsonLine.Get(payload, "id")) ?? _session;
                return [new(Tool.Codex, _session, ActivityKind.SessionStart, at)];
            case "turn_context" when JsonLine.String(JsonLine.Get(payload, "model")) is { } model:
                return [new(Tool.Codex, _session, ActivityKind.ModelSeen, at, model)];
            case "event_msg":
                return JsonLine.String(JsonLine.Get(payload, "type")) switch
                {
                    "task_started" => [new(Tool.Codex, _session, ActivityKind.TurnStart, at)],
                    "task_complete" or "turn_aborted" => [new(Tool.Codex, _session, ActivityKind.TurnEnd, at)],
                    _ => [],
                };
            default:
                return [];
        }
    }
}

/// <summary>
/// Copilot: <c>user.message</c> starts a task; <c>assistant.turn_start</c>/<c>turn_end</c> bracket each step
/// (the task ends when no step follows shortly); <c>session.task_complete</c> ends it at once;
/// <c>session.start</c>/<c>resume</c>/<c>shutdown</c> bound the session.
/// </summary>
public sealed class CopilotActivityParser(string sessionId) : IActivityParser
{
    private static readonly string[] Markers = ["\"session.", "\"user.message\"", "\"assistant.turn_"];

    public IReadOnlyList<ActivityEvent> Parse(string line)
    {
        if (!Markers.Any(m => line.Contains(m, StringComparison.Ordinal)))
        {
            return []; // most lines are tool runs, hooks and message chunks
        }

        var root = JsonLine.Parse(line);
        if (JsonLine.Timestamp(JsonLine.Get(root, "timestamp")) is not { } at)
        {
            return [];
        }

        ActivityEvent Event(ActivityKind kind, string? model = null) => new(Tool.Copilot, sessionId, kind, at, model);
        return JsonLine.String(JsonLine.Get(root, "type")) switch
        {
            "session.start" or "session.resume" => [Event(ActivityKind.SessionStart)],
            "session.shutdown" => [Event(ActivityKind.SessionEnd)],
            "user.message" => [Event(ActivityKind.TurnStart)],
            "assistant.turn_start" => [Event(ActivityKind.StepStart)],
            "assistant.turn_end" => [Event(ActivityKind.StepEnd)],
            "session.task_complete" => [Event(ActivityKind.TurnEnd)],
            "session.usage_record" when JsonLine.String(JsonLine.Get(root, "data", "usage", "model")) is { } model => [Event(ActivityKind.ModelSeen, model)],
            _ => [],
        };
    }
}
