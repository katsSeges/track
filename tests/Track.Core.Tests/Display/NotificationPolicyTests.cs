using Track.Core.Activity;
using Track.Core.Display;

namespace Track.Core.Tests.Display;

public sealed class NotificationPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    private static AgentActivity Agent(Tool tool, string session) => new(tool, session, Now, Now, null);

    [Fact]
    public void Emits_done_once_when_working_session_leaves_without_waiting()
    {
        var policy = new NotificationPolicy();
        var agent = Agent(Tool.Claude, "session");
        Assert.Empty(policy.Update([agent], new HashSet<(Tool, string)>(), notificationsEnabled: true));

        var done = Assert.Single(policy.Update([], new HashSet<(Tool, string)>(), notificationsEnabled: true));
        Assert.Equal(AgentNotificationKind.Done, done.Kind);
        Assert.Equal("Claude Code done", done.Title);
        Assert.Empty(policy.Update([], new HashSet<(Tool, string)>(), notificationsEnabled: true));
    }

    [Fact]
    public void Waiting_transition_emits_needs_you_without_a_done_event()
    {
        var policy = new NotificationPolicy();
        var agent = Agent(Tool.Copilot, "session");
        policy.Update([agent], new HashSet<(Tool, string)>(), notificationsEnabled: true);

        var needsYou = Assert.Single(policy.Update([], new HashSet<(Tool, string)> { (Tool.Copilot, "session") }, notificationsEnabled: true));

        Assert.Equal(AgentNotificationKind.NeedsYou, needsYou.Kind);
        Assert.Equal("Copilot needs you", needsYou.Title);
        Assert.Empty(policy.Update([], new HashSet<(Tool, string)> { (Tool.Copilot, "session") }, notificationsEnabled: true));
    }

    [Fact]
    public void Disabled_notifications_update_the_baseline_but_emit_nothing_after_reenable()
    {
        var policy = new NotificationPolicy();
        var agent = Agent(Tool.Codex, "session");
        policy.Update([agent], new HashSet<(Tool, string)>(), notificationsEnabled: false);

        Assert.Empty(policy.Update([], new HashSet<(Tool, string)>(), notificationsEnabled: false));
        Assert.Empty(policy.Update([], new HashSet<(Tool, string)>(), notificationsEnabled: true));
    }

    [Fact]
    public void First_observation_is_a_baseline_not_a_transition()
    {
        var policy = new NotificationPolicy();

        var notifications = policy.Update(
            [],
            new HashSet<(Tool, string)> { (Tool.Roo, "already-waiting") },
            notificationsEnabled: true);

        Assert.Empty(notifications);
    }

    [Fact]
    public void Unknown_notification_kind_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NotificationPolicy.Create(Tool.Claude, (AgentNotificationKind)999));
    }
}
