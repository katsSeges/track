using System.IO.Abstractions;
using Track.Core.Ingest;
using Track.Core.Live;

namespace Track.Core.Activity;

/// <summary>An agent session that is working right now.</summary>
/// <param name="TurnStartedUtc">When the current task started (the user's last prompt).</param>
public sealed record AgentActivity(
    Tool Tool,
    string SessionId,
    DateTimeOffset TurnStartedUtc,
    DateTimeOffset SessionStartedUtc,
    string? Model);

/// <summary>
/// Keeps per-session work state from <see cref="ActivityEvent"/>s and answers "who is working now".
/// A step end (Copilot) finishes the task when no new step starts within <see cref="StepGrace"/>.
/// A working session whose log has been silent for <see cref="StaleAfter"/> while its process is gone
/// (or for <see cref="DeadAfter"/> in any case) counts as ended: the agent crashed or was killed.
/// <para>
/// The tray's rule is shared here, so the island and the tray never disagree: a session whose log is still being
/// written (within <see cref="LiveStatusDetector.ActiveWindow"/>) more than <see cref="EndSettle"/> after its last
/// end marker is working too. That covers work the markers miss, e.g. a Stop hook sending Claude back to work
/// (a meta prompt plus a <c>stop_hook_summary</c>, found 2026-11-18), or a session whose prompt was never seen.
/// </para>
/// </summary>
public sealed class AgentActivityTracker
{
    public static readonly TimeSpan StepGrace = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan DeadAfter = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan Forget = TimeSpan.FromDays(1);

    /// <summary>Lines written this soon after an end marker (stop hook summary, usage records) still belong to the ended turn.</summary>
    public static readonly TimeSpan EndSettle = TimeSpan.FromSeconds(15);

    private readonly Dictionary<(Tool, string), Session> _sessions = [];

    public void Apply(ActivityEvent e)
    {
        var key = (e.Tool, e.SessionId);
        if (!_sessions.TryGetValue(key, out var s))
        {
            _sessions[key] = s = new Session { SessionStart = e.At };
        }

        s.SessionStart = e.At < s.SessionStart ? e.At : s.SessionStart;
        s.LastEvent = e.At > s.LastEvent ? e.At : s.LastEvent;
        switch (e.Kind)
        {
            case ActivityKind.TurnStart:
                s.TurnStart = e.At;
                s.Working = true;
                s.PendingEnd = null;
                break;
            case ActivityKind.StepStart:
                s.TurnStart = s.Working ? s.TurnStart : e.At;
                s.Working = true;
                s.PendingEnd = null;
                break;
            case ActivityKind.StepEnd:
                s.PendingEnd = s.Working ? e.At : null;
                break;
            case ActivityKind.TurnEnd:
            case ActivityKind.SessionEnd:
                s.Working = false;
                s.PendingEnd = null;
                s.LastEnd = e.At;
                break;
            case ActivityKind.ModelSeen:
                s.Model = e.Model;
                break;
        }
    }

    /// <summary>Sessions working at <paramref name="now"/>, longest-running first.</summary>
    /// <param name="lastWrite">Last write time of each session's log.</param>
    /// <param name="processAlive">Whether the tool's process is running.</param>
    public IReadOnlyList<AgentActivity> Working(
        DateTimeOffset now,
        IReadOnlyDictionary<(Tool, string), DateTimeOffset> lastWrite,
        Func<Tool, bool> processAlive)
    {
        _sessions.Where(p => now - p.Value.LastEvent > Forget).Select(p => p.Key).ToList().ForEach(k => _sessions.Remove(k));

        return _sessions
            .Select(p => (p.Key, Session: p.Value, Written: lastWrite.TryGetValue(p.Key, out var w) ? w : (DateTimeOffset?)null))
            .Select(x => (x.Key, x.Session, Marked: IsWorking(x.Key.Item1, x.Session, now, x.Written ?? x.Session.LastEvent, processAlive), StillWritten: StillWritten(x.Session, now, x.Written)))
            .Where(x => x.Marked || x.StillWritten)
            // Carried on after an end marker: the task timer starts at that end (the prompt that led to it is unknown).
            .Select(x => new AgentActivity(x.Key.Item1, x.Key.Item2, x.Marked || x.Session.Working ? x.Session.TurnStart : x.Session.LastEnd ?? x.Session.SessionStart, x.Session.SessionStart, x.Session.Model))
            .OrderBy(a => a.TurnStartedUtc)
            .ThenBy(a => a.Tool)
            .ToList();
    }

    private static bool IsWorking(Tool tool, Session s, DateTimeOffset now, DateTimeOffset lastWrite, Func<Tool, bool> processAlive)
    {
        if (!s.Working || (s.PendingEnd is { } end && now - end >= StepGrace))
        {
            return false;
        }

        var silent = now - lastWrite;
        return silent < StaleAfter || (silent < DeadAfter && processAlive(tool));
    }

    /// <summary>
    /// The tray's rule: the log was written within the active window, and not just as the tail of an ended turn.
    /// Only real log writes count (no write seen → no).
    /// </summary>
    private static bool StillWritten(Session s, DateTimeOffset now, DateTimeOffset? lastWrite)
    {
        var end = s.Working ? s.PendingEnd : s.LastEnd;
        return lastWrite is { } w && now - w <= LiveStatusDetector.ActiveWindow && (end is not { } e || w - e > EndSettle);
    }

    private sealed class Session
    {
        public DateTimeOffset SessionStart { get; set; }

        public DateTimeOffset TurnStart { get; set; }

        public DateTimeOffset LastEvent { get; set; }

        public DateTimeOffset? PendingEnd { get; set; }

        public DateTimeOffset? LastEnd { get; set; }

        public bool Working { get; set; }

        public string? Model { get; set; }
    }
}

/// <summary>
/// Reads work markers from logs written in the last day, incrementally (only new lines), and feeds them
/// to an <see cref="AgentActivityTracker"/>. Meant to run every few seconds.
/// </summary>
public sealed class ActivityReader(IFileSystem fileSystem, LogScanner scanner, AgentActivityTracker tracker, int chunkSize = 1 << 20)
{
    private readonly Dictionary<string, FileState> _files = new(StringComparer.OrdinalIgnoreCase);

    public AgentActivityTracker Tracker => tracker;

    /// <summary>Reads new lines; returns the last write time of each (tool, session) seen in recent logs.</summary>
    /// <param name="recent">Log files written in the last day (from <see cref="LogScanCache"/>); scanned here when null.</param>
    public IReadOnlyDictionary<(Tool, string), DateTimeOffset> Refresh(DateTimeOffset now, IReadOnlyList<LogFile>? recent = null)
    {
        var lastWrite = new Dictionary<(Tool, string), DateTimeOffset>();
        var files = recent ?? scanner.Scan(now - AgentActivityTracker.Forget);
        foreach (var file in files)
        {
            var state = ReadNew(file);
            foreach (var session in state?.Sessions ?? [])
            {
                var key = (file.Tool, session);
                lastWrite[key] = lastWrite.TryGetValue(key, out var seen) && seen > file.LastWriteUtc ? seen : file.LastWriteUtc;
            }
        }

        var current = files.Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _files.Keys.Where(p => !current.Contains(p)).ToList().ForEach(p => _files.Remove(p));
        return lastWrite;
    }

    private FileState? ReadNew(LogFile file)
    {
        try
        {
            using var stream = fileSystem.File.Open(file.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (!_files.TryGetValue(file.Path, out var state) || stream.Length < state.Offset)
            {
                _files[file.Path] = state = new FileState(ActivityParsers.Create(file.Tool, file.SessionId));
            }

            stream.Seek(state.Offset, SeekOrigin.Begin);
            state.Offset += LineReader.ReadCompleteLines(stream, line =>
            {
                foreach (var e in state.Parser.Parse(line))
                {
                    tracker.Apply(e);
                    state.Sessions.Add(e.SessionId);
                }
            }, chunkSize, _ => { });
            return state;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return _files.GetValueOrDefault(file.Path); // locked or gone right now: try again next time
        }
    }

    /// <summary>Where reading of one file stopped and which sessions it contains.</summary>
    private sealed class FileState(IActivityParser parser)
    {
        public IActivityParser Parser { get; } = parser;

        public long Offset { get; set; }

        public HashSet<string> Sessions { get; } = new(StringComparer.Ordinal);
    }
}
