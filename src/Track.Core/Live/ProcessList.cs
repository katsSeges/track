using System.Diagnostics;

namespace Track.Core.Live;

/// <summary>Which programs are running.</summary>
public interface IProcessList
{
    /// <summary>True when a process with any of these names (without <c>.exe</c>) is running.</summary>
    bool AnyRunning(IReadOnlyCollection<string> names);

    /// <summary>True when the process with this id is running.</summary>
    bool IsAlive(int processId);
}

/// <summary>
/// The real process list of this machine. One snapshot of all process names is reused for
/// <see cref="SnapshotAge"/>, so checking several names costs one process enumeration.
/// </summary>
public sealed class SystemProcessList(TimeProvider? time = null, Func<IReadOnlyCollection<string>>? names = null) : IProcessList
{
    public static readonly TimeSpan SnapshotAge = TimeSpan.FromSeconds(2);

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly Func<IReadOnlyCollection<string>> _names = names ?? AllProcessNames;
    private HashSet<string> _snapshot = [];
    private DateTimeOffset _takenAt = DateTimeOffset.MinValue;

    public bool AnyRunning(IReadOnlyCollection<string> names)
    {
        var now = _time.GetUtcNow();
        if (now - _takenAt >= SnapshotAge)
        {
            _snapshot = new HashSet<string>(_names(), StringComparer.OrdinalIgnoreCase);
            _takenAt = now;
        }

        return names.Any(_snapshot.Contains);
    }

    public bool IsAlive(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false; // no process with that id
        }
    }

    private static IReadOnlyCollection<string> AllProcessNames()
    {
        var processes = Process.GetProcesses();
        var result = processes.Select(p => p.ProcessName).ToList();
        Array.ForEach(processes, p => p.Dispose());
        return result;
    }
}
