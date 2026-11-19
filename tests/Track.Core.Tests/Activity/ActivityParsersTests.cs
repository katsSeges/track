using Track.Core.Activity;

namespace Track.Core.Tests.Activity;

public class ActivityParsersTests
{
    private const string T = "\"timestamp\":\"2026-11-18T10:00:00Z\"";
    private static readonly DateTimeOffset At = new(2026, 11, 18, 10, 0, 0, TimeSpan.Zero);

    private static ActivityKind[] Kinds(IActivityParser parser, string line) => parser.Parse(line).Select(e => e.Kind).ToArray();

    [Fact]
    public void Claude_first_line_starts_the_session_and_a_typed_prompt_starts_a_turn()
    {
        var p = new ClaudeActivityParser("file-id");

        var events = p.Parse("{\"type\":\"user\"," + T + ",\"sessionId\":\"s1\",\"message\":{\"content\":\"fix the bug\"}}");

        Assert.Equal([new ActivityEvent(Tool.Claude, "s1", ActivityKind.SessionStart, At), new ActivityEvent(Tool.Claude, "s1", ActivityKind.TurnStart, At)], events);
        Assert.Equal([ActivityKind.TurnStart], Kinds(p, "{\"type\":\"user\"," + T + ",\"message\":{\"content\":[{\"type\":\"text\",\"text\":\"and this\"}]}}"));
    }

    [Theory]
    [InlineData("{\"type\":\"user\"," + T + ",\"message\":{\"content\":[{\"type\":\"tool_result\",\"content\":\"ok\"}]}}")]
    [InlineData("{\"type\":\"user\"," + T + ",\"message\":{\"content\":[{\"type\":\"text\",\"text\":\"x\"},{\"type\":\"tool_result\"}]}}")]
    [InlineData("{\"type\":\"user\"," + T + ",\"isMeta\":true,\"message\":{\"content\":\"hook text\"}}")]
    [InlineData("{\"type\":\"user\"," + T + ",\"message\":{\"content\":[{\"type\":\"image\"}]}}")]
    [InlineData("{\"type\":\"user\"," + T + ",\"message\":{\"content\":{\"odd\":1}}}")]
    [InlineData("{\"type\":\"user\"," + T + ",\"message\":{}}")]
    [InlineData("{\"type\":\"assistant\"," + T + ",\"message\":{\"model\":\"<synthetic>\",\"stop_reason\":\"tool_use\"}}")]
    [InlineData("{\"type\":\"system\"," + T + ",\"subtype\":\"other\"}")]
    [InlineData("{\"type\":\"summary\"," + T + "}")]
    public void Claude_tool_results_meta_lines_and_other_lines_do_not_start_or_end_a_turn(string line)
    {
        var p = new ClaudeActivityParser("f");
        p.Parse("{\"type\":\"x\"," + T + "}"); // session start already seen

        Assert.Empty(p.Parse(line));
    }

    [Theory]
    [InlineData("{\"type\":\"assistant\"," + T + ",\"message\":{\"stop_reason\":\"end_turn\"}}")]
    [InlineData("{\"type\":\"system\"," + T + ",\"subtype\":\"stop_hook_summary\"}")]
    [InlineData("{\"type\":\"user\"," + T + ",\"message\":{\"content\":[{\"type\":\"text\",\"text\":\"[Request interrupted by user]\"}]}}")]
    [InlineData("{\"type\":\"user\"," + T + ",\"message\":{\"content\":\"[Request interrupted by user for tool use]\"}}")]
    public void Claude_turn_ends_when_done_or_interrupted(string line)
    {
        var p = new ClaudeActivityParser("f");
        p.Parse("{\"type\":\"x\"," + T + "}");

        Assert.Equal([ActivityKind.TurnEnd], Kinds(p, line));
    }

    [Fact]
    public void Claude_reports_the_model_and_falls_back_to_the_file_session()
    {
        var p = new ClaudeActivityParser("file-id");

        var events = p.Parse("{\"type\":\"assistant\"," + T + ",\"message\":{\"model\":\"claude-opus-5-5\",\"stop_reason\":\"tool_use\"}}");

        Assert.Equal(new ActivityEvent(Tool.Claude, "file-id", ActivityKind.ModelSeen, At, "claude-opus-5-5"), events[1]);
    }

    [Theory]
    [InlineData("{\"type\":\"user\",\"message\":{\"content\":\"no time\"}}")]
    [InlineData("not json")]
    public void Lines_without_a_timestamp_give_nothing(string line)
    {
        Assert.Empty(new ClaudeActivityParser("f").Parse(line));
        Assert.Empty(new CodexActivityParser("f").Parse(line));
        Assert.Empty(new CopilotActivityParser("f").Parse(line));
    }

    [Fact]
    public void Codex_markers()
    {
        var p = new CodexActivityParser("rollout-1");

        Assert.Equal([new ActivityEvent(Tool.Codex, "thread-9", ActivityKind.SessionStart, At)], p.Parse("{" + T + ",\"type\":\"session_meta\",\"payload\":{\"id\":\"thread-9\"}}"));
        Assert.Equal([new ActivityEvent(Tool.Codex, "thread-9", ActivityKind.ModelSeen, At, "gpt-5.6-luna")], p.Parse("{" + T + ",\"type\":\"turn_context\",\"payload\":{\"model\":\"gpt-5.6-luna\"}}"));
        Assert.Equal([ActivityKind.TurnStart], Kinds(p, "{" + T + ",\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\"}}"));
        Assert.Equal([ActivityKind.TurnEnd], Kinds(p, "{" + T + ",\"type\":\"event_msg\",\"payload\":{\"type\":\"task_complete\"}}"));
        Assert.Equal([ActivityKind.TurnEnd], Kinds(p, "{" + T + ",\"type\":\"event_msg\",\"payload\":{\"type\":\"turn_aborted\"}}"));
        Assert.Empty(p.Parse("{" + T + ",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\"}}"));
        Assert.Empty(p.Parse("{" + T + ",\"type\":\"turn_context\",\"payload\":{}}"));
        Assert.Empty(p.Parse("{" + T + ",\"type\":\"response_item\"}"));
    }

    [Fact]
    public void Codex_without_a_session_id_keeps_the_file_name()
    {
        Assert.Equal("rollout-1", new CodexActivityParser("rollout-1").Parse("{" + T + ",\"type\":\"session_meta\",\"payload\":{}}")[0].SessionId);
    }

    [Theory]
    [InlineData("session.start", ActivityKind.SessionStart)]
    [InlineData("session.resume", ActivityKind.SessionStart)]
    [InlineData("session.shutdown", ActivityKind.SessionEnd)]
    [InlineData("user.message", ActivityKind.TurnStart)]
    [InlineData("assistant.turn_start", ActivityKind.StepStart)]
    [InlineData("assistant.turn_end", ActivityKind.StepEnd)]
    [InlineData("session.task_complete", ActivityKind.TurnEnd)]
    public void Copilot_markers(string type, ActivityKind kind)
    {
        Assert.Equal([new ActivityEvent(Tool.Copilot, "folder", kind, At)], new CopilotActivityParser("folder").Parse("{\"type\":\"" + type + "\"," + T + "}"));
    }

    [Fact]
    public void Copilot_steering_and_the_same_interaction_continue_the_task_instead_of_restarting_its_timer()
    {
        var p = new CopilotActivityParser("folder");
        string Message(string? delivery, string? interaction) =>
            "{\"type\":\"user.message\"," + T + ",\"data\":{" +
            string.Join(",", new[] { delivery is null ? null : $"\"delivery\":\"{delivery}\"", interaction is null ? null : $"\"interactionId\":\"{interaction}\"" }.OfType<string>()) + "}}";

        Assert.Equal([ActivityKind.TurnStart], Kinds(p, Message("idle", "i1")));           // a new task
        Assert.Equal([ActivityKind.StepStart], Kinds(p, Message("steering", "i1")));       // typed while it works
        Assert.Equal([ActivityKind.StepStart], Kinds(p, Message("steering", "i2")));       // steering always continues
        Assert.Equal([ActivityKind.StepStart], Kinds(p, Message("queued", "i2")));         // same interaction
        Assert.Equal([ActivityKind.TurnStart], Kinds(p, Message(null, null)));             // no fields (older Copilot): a new task
        Assert.Equal([ActivityKind.TurnStart], Kinds(p, Message("queued", "i3")));         // a new interaction is a new task
        Assert.Equal([ActivityKind.StepStart], Kinds(p, Message("steering", null)));       // steering without an id still continues
        Assert.Equal([ActivityKind.StepStart], Kinds(p, Message("idle", "i3")));           // …and i3 is still the current interaction
        Assert.Equal([ActivityKind.TurnEnd], Kinds(p, "{\"type\":\"session.task_complete\"," + T + "}"));
        Assert.Equal([ActivityKind.TurnStart], Kinds(p, Message("idle", "i3")));           // after completion, even i3 is a new task
    }

    [Fact]
    public void Copilot_reports_the_model_from_usage_records()
    {
        var p = new CopilotActivityParser("folder");

        Assert.Equal("gpt-6.1-sol", p.Parse("{\"type\":\"session.usage_record\"," + T + ",\"data\":{\"usage\":{\"model\":\"gpt-6.1-sol\"}}}")[0].Model);
        Assert.Empty(p.Parse("{\"type\":\"session.usage_record\"," + T + ",\"data\":{}}"));
        Assert.Empty(p.Parse("{\"type\":\"tool.execution_start\"," + T + "}"));
    }

    [Fact]
    public void Factory_makes_one_parser_per_tool()
    {
        Assert.IsType<ClaudeActivityParser>(ActivityParsers.Create(Tool.Claude, "s"));
        Assert.IsType<CodexActivityParser>(ActivityParsers.Create(Tool.Codex, "s"));
        Assert.IsType<CopilotActivityParser>(ActivityParsers.Create(Tool.Copilot, "s"));
        Assert.Throws<ArgumentOutOfRangeException>(() => ActivityParsers.Create((Tool)9, "s"));
    }

    [Fact]
    public void Copilot_sub_agent_prompts_are_steps_of_the_main_task_and_ask_user_waits_for_the_answer()
    {
        var p = new CopilotActivityParser("folder");
        Assert.Equal([ActivityKind.TurnStart], Kinds(p, "{\"type\":\"user.message\"," + T + ",\"data\":{\"delivery\":\"idle\",\"interactionId\":\"main\"}}"));

        // The main agent prompts a sub-agent: same log, top-level agentId, its own new interaction.
        Assert.Equal([ActivityKind.StepStart, ActivityKind.SubAgentStart], Kinds(p, "{\"type\":\"user.message\"," + T + ",\"agentId\":\"sub-1\",\"data\":{\"delivery\":\"idle\",\"interactionId\":\"sub\"}}"));
        // …which does not replace the main interaction.
        Assert.Equal([ActivityKind.StepStart], Kinds(p, "{\"type\":\"user.message\"," + T + ",\"data\":{\"delivery\":\"queued\",\"interactionId\":\"main\"}}"));

        Assert.Equal([new ActivityEvent(Tool.Copilot, "folder", ActivityKind.WaitStart, At)],
            p.Parse("{\"type\":\"tool.execution_start\"," + T + ",\"data\":{\"toolCallId\":\"c1\",\"toolName\":\"ask_user\"}}"));
        Assert.Equal([ActivityKind.WaitEnd], Kinds(p, "{\"type\":\"human_response.recorded\"," + T + ",\"data\":{\"toolCallId\":\"c1\",\"actor\":\"human_response\"}}"));

        // Other tools are neither: filtered out early, or parsed and ignored when "ask_user" appears elsewhere.
        Assert.Empty(p.Parse("{\"type\":\"tool.execution_start\"," + T + ",\"data\":{\"toolName\":\"powershell\"}}"));
        Assert.Empty(p.Parse("{\"type\":\"tool.execution_start\"," + T + ",\"data\":{\"toolName\":\"view\",\"arguments\":\"ask_user\"}}"));
    }
}
