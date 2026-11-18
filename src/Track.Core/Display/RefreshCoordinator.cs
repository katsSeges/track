namespace Track.Core.Display;

/// <summary>
/// Runs the two refresh loops: usage (default every 30 s) and live status (every 5 s). A refresh that
/// is still running is never started again on top of itself, and a failing refresh is reported as a
/// status line instead of crashing the app. <paramref name="reportStatus"/> gets null after a success.
/// </summary>
public sealed class RefreshCoordinator(
    TimeProvider time,
    TimeSpan usageInterval,
    TimeSpan liveInterval,
    Action refreshUsage,
    Action refreshLive,
    Action<string?> reportStatus) : IDisposable
{
    public static readonly TimeSpan DefaultLiveInterval = TimeSpan.FromSeconds(5);

    private readonly List<ITimer> _timers = [];
    private int _usageBusy;
    private int _liveBusy;

    /// <summary>Refreshes both right away, then keeps refreshing on the timers.</summary>
    public void Start()
    {
        RefreshUsageNow();
        RefreshLiveNow();
        _timers.Add(time.CreateTimer(_ => RefreshUsageNow(), null, usageInterval, usageInterval));
        _timers.Add(time.CreateTimer(_ => RefreshLiveNow(), null, liveInterval, liveInterval));
    }

    /// <summary>Returns false when a usage refresh was already running.</summary>
    public bool RefreshUsageNow() => Run(ref _usageBusy, refreshUsage, "Usage");

    /// <summary>Returns false when a live refresh was already running.</summary>
    public bool RefreshLiveNow() => Run(ref _liveBusy, refreshLive, "Live status");

    public void Dispose()
    {
        _timers.ForEach(t => t.Dispose());
        _timers.Clear();
    }

    private bool Run(ref int busy, Action refresh, string what)
    {
        if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
        {
            return false;
        }

        try
        {
            refresh();
            reportStatus(null);
        }
#pragma warning disable CA1031 // any failure must become a status line, never a crash of the tray app
        catch (Exception e)
#pragma warning restore CA1031
        {
            reportStatus($"{what} refresh failed: {e.Message}");
        }
        finally
        {
            Volatile.Write(ref busy, 0);
        }

        return true;
    }
}
