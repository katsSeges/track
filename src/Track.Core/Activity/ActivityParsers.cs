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

    /// <summary>The agent asked the user something and waits for the answer (Copilot's <c>ask_user</c>): not working.</summary>
    WaitStart,

    /// <summary>The user answered: the agent works again and the task timer restarts, like after a prompt.</summary>
    WaitEnd,

    /// <summary>The agent finished or was interrupted.</summary>
    TurnEnd,

    /// <summary>The session was closed.</summary>
    SessionEnd,

    /// <summary>A model name was seen.</summary>
    ModelSeen,

    /// <summary>A sub-agent started, or a finished one was given more work (<see cref="ActivityEvent.Sub"/>).</summary>
    SubAgentStart,

    /// <summary>A step of a sub-agent ended; it is done unless another step follows within the grace period.</summary>
    SubAgentStepEnd,

    /// <summary>A sub-agent finished.</summary>
    SubAgentEnd,

    /// <summary>A sub-agent used tokens (<see cref="SubAgent.Tokens"/>, added up).</summary>
    SubAgentTokens,

    /// <summary>Facts about a sub-agent (name, task, model, parent); does not start it.</summary>
    SubAgentInfo,

    /// <summary>A sub-agent launched another one through the tool call <see cref="SubAgent.LaunchId"/> (Claude Code nesting).</summary>
    SubAgentLaunch,
}

/// <summary>A sub-agent fact carried by a sub-agent event. Null fields mean "not in this line".</summary>
/// <param name="Id">The sub-agent's own id (Claude <c>agentId</c>, Copilot <c>agentId</c>, Codex thread id).</param>
/// <param name="ParentId">The sub-agent that started this one, when it is nested.</param>
/// <param name="OwnSession">It has its own session log and usage (Codex), not part of the parent's.</param>
/// <param name="LaunchId">Claude Code: the tool call that launched it (from its meta file), or the call it made (launch events).</param>
public sealed record SubAgent(
    string Id,
    string? Name = null,
    string? Task = null,
    string? Model = null,
    long Tokens = 0,
    string? ParentId = null,
    bool OwnSession = false,
    string? LaunchId = null);

/// <param name="SessionId">The main session the event belongs to (for sub-agent events: the parent's session).</param>
/// <param name="Sub">Set on sub-agent events only.</param>
public sealed record ActivityEvent(Tool Tool, string SessionId, ActivityKind Kind, DateTimeOffset At, string? Model = null, SubAgent? Sub = null);

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
        Tool.Gemini or Tool.OpenCode or Tool.Cline or Tool.Roo or Tool.Ollama => NoActivityParser.Instance,
        _ => throw new ArgumentOutOfRangeException(nameof(tool), tool, "Unknown tool."),
    };

    public static bool HasDetailedActivity(Tool tool) => tool is Tool.Claude or Tool.Codex or Tool.Copilot;

    private sealed class NoActivityParser : IActivityParser
    {
        public static NoActivityParser Instance { get; } = new();

        public IReadOnlyList<ActivityEvent> Parse(string line) => [];
    }
}

/// <summary>
/// Claude Code: a turn starts at a user line with text that is not a tool result or a meta line, and ends
/// at an assistant line with <c>stop_reason: end_turn</c>, a <c>stop_hook_summary</c> line, or the
/// "[Request interrupted by user]" line. The session starts at the file's first timestamp.
/// <para>
/// Sub-agent logs (<c>subagents/agent-&lt;id&gt;.jsonl</c>) carry the parent's <c>sessionId</c> with
/// <c>isSidechain: true</c> and an <c>agentId</c>: those lines are the sub-agent's (start, tokens, launches, end)
/// and never start or end the parent's turn (found 2026-11-19: a sub-agent's prompt restarted the parent's timer).
/// </para>
/// </summary>
public sealed class ClaudeActivityParser(string fallbackSessionId) : IActivityParser
{
    private const string Interrupted = "[Request interrupted by user";
    private static readonly string[] LaunchTools = ["Agent", "Task"];
    private readonly HashSet<string> _counted = new(StringComparer.Ordinal);
    private bool _started;
    private bool _modelKnown;

    public IReadOnlyList<ActivityEvent> Parse(string line)
    {
        // Cheap pre-filter (most lines are tool calls and results): after the first line, only prompts,
        // turn ends and, until a model is known, assistant lines can matter.
        var sidechain = line.Contains("\"isSidechain\":true", StringComparison.Ordinal) || line.Contains("\"agentId\":", StringComparison.Ordinal);
        if (_started && !sidechain
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
        if (sidechain && JsonLine.String(JsonLine.Get(root, "agentId")) is { } agent)
        {
            return SubAgentLine(root, session, agent, at);
        }

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

    private List<ActivityEvent> SubAgentLine(System.Text.Json.JsonElement? root, string session, string agent, DateTimeOffset at)
    {
        var events = new List<ActivityEvent>();
        ActivityEvent Sub(ActivityKind kind, SubAgent sub) => new(Tool.Claude, session, kind, at, Sub: sub);
        var message = JsonLine.Get(root, "message");
        switch (JsonLine.String(JsonLine.Get(root, "type")))
        {
            case "user" when PromptText(JsonLine.Get(message, "content")) is { } text:
                events.Add(Sub(text.StartsWith(Interrupted, StringComparison.Ordinal) ? ActivityKind.SubAgentEnd : ActivityKind.SubAgentStart, new SubAgent(agent)));
                break;
            case "assistant":
                if (JsonLine.String(JsonLine.Get(message, "model")) is { } model && model != "<synthetic>")
                {
                    events.Add(Sub(ActivityKind.SubAgentInfo, new SubAgent(agent, Model: model)));
                }

                // A message's usage repeats on each of its content lines: count it once.
                var usage = JsonLine.Get(message, "usage");
                var tokens = JsonLine.Long(JsonLine.Get(usage, "input_tokens")) + JsonLine.Long(JsonLine.Get(usage, "cache_creation_input_tokens"))
                             + JsonLine.Long(JsonLine.Get(usage, "cache_read_input_tokens")) + JsonLine.Long(JsonLine.Get(usage, "output_tokens"));
                if (tokens > 0 && _counted.Add(JsonLine.String(JsonLine.Get(message, "id")) ?? $"{agent}@{at:O}"))
                {
                    events.Add(Sub(ActivityKind.SubAgentTokens, new SubAgent(agent, Tokens: tokens)));
                }

                if (JsonLine.Get(message, "content") is { ValueKind: System.Text.Json.JsonValueKind.Array } items)
                {
                    events.AddRange(items.EnumerateArray()
                        .Where(i => JsonLine.String(JsonLine.Get(i, "type")) == "tool_use" && LaunchTools.Contains(JsonLine.String(JsonLine.Get(i, "name"))))
                        .Select(i => Sub(ActivityKind.SubAgentLaunch, new SubAgent(agent, LaunchId: JsonLine.String(JsonLine.Get(i, "id"))))));
                }

                if (JsonLine.String(JsonLine.Get(message, "stop_reason")) == "end_turn")
                {
                    events.Add(Sub(ActivityKind.SubAgentEnd, new SubAgent(agent)));
                }

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

    /// <summary>
    /// Set when this log is a spawned sub-agent thread (<c>source.subagent.thread_spawn</c>): its events go to the
    /// parent thread as sub-agent events, so it never shows as a top-level agent.
    /// </summary>
    private string? _parent;
    private long _lastTotal;

    /// <summary>
    /// A forked sub-agent thread replays a copy of its parent's history after its own header (a second
    /// <c>session_meta</c>, the parent's turn start, messages), up to <c>thread_settings_applied</c>; that copy is not
    /// the sub-agent's work and is skipped (checked on 16 real child logs, 2026-11-19).
    /// </summary>
    private bool _copiedHistory;

    public IReadOnlyList<ActivityEvent> Parse(string line)
    {
        if (_copiedHistory)
        {
            _copiedHistory = !line.Contains("thread_settings_applied", StringComparison.Ordinal);
            return [];
        }

        var tokenLine = _parent is not null && line.Contains("token_count", StringComparison.Ordinal);
        if (!tokenLine && !Markers.Any(m => line.Contains(m, StringComparison.Ordinal)))
        {
            return []; // most lines are tool calls, outputs and token counts
        }

        var root = JsonLine.Parse(line);
        if (JsonLine.Timestamp(JsonLine.Get(root, "timestamp")) is not { } at)
        {
            return [];
        }

        var payload = JsonLine.Get(root, "payload");
        if (JsonLine.String(JsonLine.Get(root, "type")) == "session_meta")
        {
            if (_parent is not null)
            {
                _copiedHistory = true; // the parent's header: its copied history follows
                return [];
            }

            _session = JsonLine.String(JsonLine.Get(payload, "id")) ?? _session;
            var spawn = JsonLine.Get(payload, "source", "subagent", "thread_spawn");
            _parent = JsonLine.String(JsonLine.Get(spawn, "parent_thread_id"));
            if (_parent is not null)
            {
                // agent_path "/root/moss_final_qa" reads as the task "moss final qa".
                var task = JsonLine.String(JsonLine.Get(spawn, "agent_path"))?.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Replace('_', ' ');
                return [SubEvent(ActivityKind.SubAgentInfo, at, new SubAgent(_session, Name: JsonLine.String(JsonLine.Get(spawn, "agent_nickname")), Task: task, OwnSession: true))];
            }
        }

        return _parent is null ? MainEvent(root, payload, at) : SubAgentEvent(root, payload, at);
    }

    private ActivityEvent SubEvent(ActivityKind kind, DateTimeOffset at, SubAgent sub) => new(Tool.Codex, _parent!, kind, at, Sub: sub);

    private IReadOnlyList<ActivityEvent> SubAgentEvent(System.Text.Json.JsonElement? root, System.Text.Json.JsonElement? payload, DateTimeOffset at)
    {
        switch (JsonLine.String(JsonLine.Get(root, "type")))
        {
            case "turn_context" when JsonLine.String(JsonLine.Get(payload, "model")) is { } model:
                return [SubEvent(ActivityKind.SubAgentInfo, at, new SubAgent(_session, Model: model))];
            case "event_msg":
                switch (JsonLine.String(JsonLine.Get(payload, "type")))
                {
                    case "task_started":
                        return [SubEvent(ActivityKind.SubAgentStart, at, new SubAgent(_session))];
                    case "task_complete" or "turn_aborted":
                        return [SubEvent(ActivityKind.SubAgentEnd, at, new SubAgent(_session))];
                    case "token_count" when JsonLine.Get(payload, "info", "total_token_usage") is { } usage:
                        // Totals are cumulative; a smaller total means the counters restarted.
                        var total = JsonLine.Long(JsonLine.Get(usage, "total_tokens"));
                        var delta = total < _lastTotal ? total : total - _lastTotal;
                        _lastTotal = total;
                        return delta > 0 ? [SubEvent(ActivityKind.SubAgentTokens, at, new SubAgent(_session, Tokens: delta))] : [];
                }

                return [];
            default:
                return [];
        }
    }

    private IReadOnlyList<ActivityEvent> MainEvent(System.Text.Json.JsonElement? root, System.Text.Json.JsonElement? payload, DateTimeOffset at)
    {
        switch (JsonLine.String(JsonLine.Get(root, "type")))
        {
            case "session_meta":
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
/// <para>
/// A message typed while Copilot works (<c>delivery: "steering"</c>) or one in the same <c>interactionId</c>
/// continues the current task instead of starting a new one, so the task timer keeps counting like Copilot's own
/// timer does (found 2026-11-19: 18 steering messages in one evening each restarted the island's timer).
/// </para>
/// <para>
/// Sub-agents write into the same log with a top-level <c>agentId</c>: their prompts are steps of the main task.
/// <c>ask_user</c> pauses the task until <c>human_response.recorded</c> (found 2026-11-19: a 5-hour wait for an
/// answer counted as working, and 18 sub-agent prompts restarted the timer).
/// </para>
/// </summary>
public sealed class CopilotActivityParser(string sessionId) : IActivityParser
{
    private static readonly string[] Markers = ["\"session.", "\"user.message\"", "\"assistant.turn_", "\"human_response.recorded\"", "\"ask_user\"", "\"subagent.", "\"toolName\":\"task\""];
    private readonly Dictionary<string, string> _launchedBy = new(StringComparer.Ordinal);
    private string? _interaction;

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
        if (JsonLine.String(JsonLine.Get(root, "agentId")) is { } agent)
        {
            return SubAgentLine(root, agent, at);
        }

        return JsonLine.String(JsonLine.Get(root, "type")) switch
        {
            "session.start" or "session.resume" => [Event(ActivityKind.SessionStart)],
            "session.shutdown" => [Event(ActivityKind.SessionEnd)],
            "user.message" => [Event(Prompt(root))],
            "tool.execution_start" when JsonLine.String(JsonLine.Get(root, "data", "toolName")) == "ask_user" => [Event(ActivityKind.WaitStart)],
            "human_response.recorded" => [Event(ActivityKind.WaitEnd)],
            "assistant.turn_start" => [Event(ActivityKind.StepStart)],
            "assistant.turn_end" => [Event(ActivityKind.StepEnd)],
            "session.task_complete" => [Event(Complete())],
            "session.usage_record" when JsonLine.String(JsonLine.Get(root, "data", "usage", "model")) is { } model => [Event(ActivityKind.ModelSeen, model)],
            _ => [],
        };
    }

    /// <summary>
    /// A sub-agent's line (top-level <c>agentId</c>): <c>subagent.started</c>/<c>completed</c>, its steps, its usage, and
    /// the main agent's prompts to it (which also keep the main task going). A <c>task</c> tool call made by a
    /// sub-agent records it as the parent of the sub-agent that call starts.
    /// </summary>
    private List<ActivityEvent> SubAgentLine(System.Text.Json.JsonElement? root, string agent, DateTimeOffset at)
    {
        ActivityEvent Sub(ActivityKind kind, SubAgent sub) => new(Tool.Copilot, sessionId, kind, at, Sub: sub);
        var data = JsonLine.Get(root, "data");
        switch (JsonLine.String(JsonLine.Get(root, "type")))
        {
            case "subagent.started":
                var call = JsonLine.String(JsonLine.Get(data, "toolCallId"));
                return [Sub(ActivityKind.SubAgentStart, new SubAgent(
                    agent,
                    Name: JsonLine.String(JsonLine.Get(data, "agentDisplayName")) ?? JsonLine.String(JsonLine.Get(data, "agentName")),
                    Task: JsonLine.String(JsonLine.Get(data, "agentDescription")),
                    Model: JsonLine.String(JsonLine.Get(data, "model")),
                    ParentId: call is not null && _launchedBy.TryGetValue(call, out var parent) ? parent : null))];
            case "subagent.completed":
                return [Sub(ActivityKind.SubAgentEnd, new SubAgent(agent))];
            case "user.message":
                return [new(Tool.Copilot, sessionId, ActivityKind.StepStart, at), Sub(ActivityKind.SubAgentStart, new SubAgent(agent))];
            case "assistant.turn_start":
                return [Sub(ActivityKind.SubAgentStart, new SubAgent(agent))];
            case "assistant.turn_end":
                return [Sub(ActivityKind.SubAgentStepEnd, new SubAgent(agent))];
            case "session.usage_record":
                var usage = JsonLine.Get(data, "usage");
                return [Sub(ActivityKind.SubAgentTokens, new SubAgent(agent, Tokens: JsonLine.Long(JsonLine.Get(usage, "inputTokens")) + JsonLine.Long(JsonLine.Get(usage, "outputTokens"))))];
            case "tool.execution_start" when JsonLine.String(JsonLine.Get(data, "toolName")) == "ask_user":
                return [new(Tool.Copilot, sessionId, ActivityKind.WaitStart, at)]; // a sub-agent asking you still waits for you
            case "tool.execution_start" when JsonLine.String(JsonLine.Get(data, "toolName")) == "task" && JsonLine.String(JsonLine.Get(data, "toolCallId")) is { } launch:
                _launchedBy[launch] = agent;
                return [];
            default:
                return [];
        }
    }

    /// <summary>A new task, or (steering / same interaction) a step of the running one.</summary>
    private ActivityKind Prompt(System.Text.Json.JsonElement? root)
    {
        var interaction = JsonLine.String(JsonLine.Get(root, "data", "interactionId"));
        var continues = JsonLine.String(JsonLine.Get(root, "data", "delivery")) == "steering"
                        || (interaction is not null && interaction == _interaction);
        // A new task takes its own interaction (none for older Copilot versions); a continuing message keeps the current one unless it names another.
        _interaction = continues ? interaction ?? _interaction : interaction;
        return continues ? ActivityKind.StepStart : ActivityKind.TurnStart;
    }

    /// <summary>The task is over: the next message starts a new one, whatever its interaction.</summary>
    private ActivityKind Complete()
    {
        _interaction = null;
        return ActivityKind.TurnEnd;
    }
}
