using Track.Core.Activity;
using Track.Core.Providers;

namespace Track.Core.Display;

public enum AgentNotificationKind
{
    Done,
    NeedsYou,
}

public sealed record AgentNotification(Tool Tool, AgentNotificationKind Kind, string Title, string Message);

/// <summary>Emits one notification for each observed transition into done or waiting-for-user.</summary>
public sealed class NotificationPolicy
{
    private HashSet<(Tool Tool, string SessionId)> _working = [];
    private HashSet<(Tool Tool, string SessionId)> _waiting = [];
    private bool _initialized;

    public IReadOnlyList<AgentNotification> Update(
        IReadOnlyList<AgentActivity> working,
        IReadOnlySet<(Tool Tool, string SessionId)> waiting,
        bool notificationsEnabled)
    {
        var currentWorking = working.Select(agent => (agent.Tool, agent.SessionId)).ToHashSet();
        var currentWaiting = waiting.ToHashSet();
        if (!_initialized)
        {
            _initialized = true;
            _working = currentWorking;
            _waiting = currentWaiting;
            return [];
        }

        var events = new List<AgentNotification>();
        if (notificationsEnabled)
        {
            foreach (var key in currentWaiting.Except(_waiting).OrderBy(key => key.Tool).ThenBy(key => key.SessionId, StringComparer.Ordinal))
            {
                events.Add(Create(key.Tool, AgentNotificationKind.NeedsYou));
            }

            foreach (var key in _working.Except(currentWorking).Except(currentWaiting).OrderBy(key => key.Tool).ThenBy(key => key.SessionId, StringComparer.Ordinal))
            {
                events.Add(Create(key.Tool, AgentNotificationKind.Done));
            }
        }

        _working = currentWorking;
        _waiting = currentWaiting;
        return events;
    }

    internal static AgentNotification Create(Tool tool, AgentNotificationKind kind)
    {
        var provider = ProviderCatalog.Get(tool);
        return kind switch
        {
            AgentNotificationKind.Done => new AgentNotification(tool, kind, $"{provider.Name} done", $"{provider.Name} finished working."),
            AgentNotificationKind.NeedsYou => new AgentNotification(tool, kind, $"{provider.Name} needs you", $"{provider.Name} is waiting for your input."),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown notification kind."),
        };
    }
}
