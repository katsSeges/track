using System.IO.Abstractions;
using Track.Core.Ingest;
using Track.Core.Live;

namespace Track.Core.Activity;

/// <summary>An agent session that is working right now.</summary>
/// <param name="TurnStartedUtc">When the current task started (the user's last prompt).</param>
/// <param name="TurnEndedUtc">Set when the agent's own turn ended while its sub-agents still run: its timer stops there.</param>
/// <param name="SubAgents">Its sub-agents, running and finished, in display order (parents before their children); null when none.</param>
public sealed record AgentActivity(
    Tool Tool,
    string SessionId,
    DateTimeOffset TurnStartedUtc,
    DateTimeOffset SessionStartedUtc,
    string? Model,
    DateTimeOffset? TurnEndedUtc = null,
    IReadOnlyList<SubAgentActivity>? SubAgents = null)
{
    public IReadOnlyList<SubAgentActivity> Subs => SubAgents ?? [];
}

/// <summary>A sub-agent of a working agent.</summary>
/// <param name="Depth">1 for one started by the main agent, 2 for one started by a sub-agent, and so on.</param>
/// <param name="EndedUtc">When it finished; null while it runs.</param>
/// <param name="OwnSession">It has its own session log and usage (Codex), so its tokens are not in the parent's session.</param>
public sealed record SubAgentActivity(
    Tool Tool,
    string Id,
    string? Name,
    string? Task,
    string? Model,
    int Depth,
    DateTimeOffset StartedUtc,
    DateTimeOffset? EndedUtc,
    long Tokens,
    bool OwnSession = false)
{
    public bool Running => EndedUtc is null;
}

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

        // Sub-agent events keep the session known too (a Codex child thread, or a parent whose prompt was never seen).
        s.LastEvent = e.At > s.LastEvent ? e.At : s.LastEvent;
        if (e.Sub is { } sub)
        {
            ApplySub(s, e.Kind, e.At, sub);
            return;
        }

        s.SessionStart = e.At < s.SessionStart ? e.At : s.SessionStart;
        switch (e.Kind)
        {
            case ActivityKind.TurnStart:
            case ActivityKind.WaitEnd:
                // A new task: the finished sub-agents of the last one are done with.
                s.Subs.Where(p => p.Value.End is not null).Select(p => p.Key).ToList().ForEach(k => s.Subs.Remove(k));
                s.TurnStart = e.At;
                s.Working = true;
                s.Waiting = false;
                s.PendingEnd = null;
                break;
            case ActivityKind.WaitStart:
                s.Working = false;
                s.Waiting = true;
                s.PendingEnd = null;
                s.LastEnd = e.At;
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
                s.Waiting = false;
                s.PendingEnd = null;
                s.LastEnd = e.At;
                break;
            case ActivityKind.ModelSeen:
                s.Model = e.Model;
                break;
        }
    }

    private static void ApplySub(Session s, ActivityKind kind, DateTimeOffset at, SubAgent e)
    {
        if (kind == ActivityKind.SubAgentLaunch)
        {
            s.Launches[e.LaunchId!] = e.Id;
            return;
        }

        if (!s.Subs.TryGetValue(e.Id, out var sub))
        {
            s.Subs[e.Id] = sub = new Sub();
        }

        sub.LastEvent = at > sub.LastEvent ? at : sub.LastEvent;
        sub.Name = e.Name ?? sub.Name;
        sub.Task = e.Task ?? sub.Task;
        sub.Model = e.Model ?? sub.Model;
        sub.ParentId = e.ParentId ?? sub.ParentId;
        sub.LaunchId = e.LaunchId ?? sub.LaunchId;
        sub.OwnSession |= e.OwnSession;
        switch (kind)
        {
            case ActivityKind.SubAgentStart:
                if (sub.Start is null || sub.End is not null || (sub.PendingEnd is { } pending && at - pending >= StepGrace))
                {
                    sub.Start = at; // a new run, or a finished one given more work: its timer starts again
                }

                sub.End = null;
                sub.PendingEnd = null;
                break;
            case ActivityKind.SubAgentStepEnd:
                sub.PendingEnd = sub.End is null ? at : null;
                break;
            case ActivityKind.SubAgentEnd:
                sub.End ??= at;
                sub.PendingEnd = null;
                break;
            case ActivityKind.SubAgentTokens:
                sub.Tokens += e.Tokens;
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

        // A Codex sub-agent's thread (it may hold its own sub-agents) is never an agent of its own.
        var subThreads = _sessions.SelectMany(p => p.Value.Subs.Where(x => x.Value.OwnSession).Select(x => (p.Key.Item1, x.Key))).ToHashSet();
        var result = new List<AgentActivity>();
        var candidates = _sessions.Where(p => !subThreads.Contains(p.Key)).ToList();
        for (var i = 0; i < candidates.Count; i++)
        {
            var (key, session) = candidates[i];
            var written = lastWrite.TryGetValue(key, out var w) ? w : (DateTimeOffset?)null;
            var marked = IsWorking(key.Item1, session, now, written ?? session.LastEvent, processAlive);
            var subs = SubAgents(key.Item1, session, now, processAlive(key.Item1));
            var subsRunning = subs.Any(x => x.Running);
            if (marked)
            {
                result.Add(Activity(key, session, session.TurnStart, null, subs));
            }
            else if (subsRunning)
            {
                // Its own turn ended but sub-agents still run in the background: it stays, its timer stopped at that end.
                var start = session.TurnStart == default ? subs.Min(x => x.StartedUtc) : session.TurnStart;
                result.Add(Activity(key, session, start, session.LastEnd ?? start, subs));
            }
            else if (StillWritten(session, now, written))
            {
                // Carried on after an end marker: the task timer starts at that end (the prompt that led to it is unknown).
                result.Add(Activity(key, session, session.Working ? session.TurnStart : session.LastEnd ?? session.SessionStart, null, subs));
            }
            else
            {
                // Not shown: its finished sub-agents leave with it.
                session.Subs.Where(p => p.Value.End is not null).Select(p => p.Key).ToList().ForEach(k => session.Subs.Remove(k));
            }
        }

        return result.OrderBy(a => a.TurnStartedUtc).ThenBy(a => a.Tool).ToList();
    }

    private static AgentActivity Activity((Tool, string) key, Session s, DateTimeOffset turnStart, DateTimeOffset? turnEnd, List<SubAgentActivity> subs) =>
        new(key.Item1, key.Item2, turnStart, s.SessionStart, s.Model, turnEnd, subs.Count > 0 ? subs : null);

    /// <summary>
    /// The session's sub-agents as a tree in start order, children right under their parent. A Codex sub-agent's own
    /// sub-agents live under its thread id (their parent thread) and are attached here as its children.
    /// </summary>
    private List<SubAgentActivity> SubAgents(Tool tool, Session session, DateTimeOffset now, bool processAlive)
    {
        var all = new Dictionary<string, (Sub Sub, string? Parent)>(StringComparer.Ordinal);
        void Collect(Session s, string? owner, int guard)
        {
            foreach (var (id, sub) in s.Subs.Where(p => p.Value.Start is not null))
            {
                var parent = sub.ParentId ?? (sub.LaunchId is { } l && s.Launches.TryGetValue(l, out var launcher) ? launcher : owner);
                all[id] = (sub, parent);
                if (sub.OwnSession && guard < 8 && _sessions.TryGetValue((tool, id), out var own))
                {
                    Collect(own, id, guard + 1);
                }
            }
        }

        Collect(session, null, 0);
        var list = new List<SubAgentActivity>();
        void Add(string? parent, int depth)
        {
            foreach (var (id, (sub, _)) in all.Where(p => p.Value.Parent == parent || (depth == 1 && p.Value.Parent is { } x && !all.ContainsKey(x)))
                         .OrderBy(p => p.Value.Sub.Start).ToList())
            {
                if (list.Any(a => a.Id == id))
                {
                    continue;
                }

                list.Add(new SubAgentActivity(tool, id, sub.Name, sub.Task, sub.Model, depth, sub.Start!.Value, EndOf(sub, now, processAlive), sub.Tokens, sub.OwnSession));
                Add(id, depth + 1);
            }
        }

        Add(null, 1);
        return list;
    }

    /// <summary>When the sub-agent finished: its end marker, an unfollowed step end, or its last line once it went silent (crashed).</summary>
    private static DateTimeOffset? EndOf(Sub sub, DateTimeOffset now, bool processAlive)
    {
        if (sub.End is { } end)
        {
            return end;
        }

        if (sub.PendingEnd is { } pending && now - pending >= StepGrace)
        {
            return pending;
        }

        var silent = now - sub.LastEvent;
        return silent < StaleAfter || (silent < DeadAfter && processAlive) ? null : sub.LastEvent;
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

    /// <summary>Sessions waiting for the user's answer (Copilot's <c>ask_user</c>); they are not working.</summary>
    public IReadOnlySet<(Tool Tool, string SessionId)> Waiting() =>
        _sessions.Where(p => p.Value.Waiting).Select(p => p.Key).ToHashSet();

    /// <summary>
    /// The tray's rule: the log was written within the active window, and not just as the tail of an ended turn.
    /// Only real log writes count (no write seen → no).
    /// </summary>
    private static bool StillWritten(Session s, DateTimeOffset now, DateTimeOffset? lastWrite)
    {
        var end = s.Working ? s.PendingEnd : s.LastEnd;
        return !s.Waiting && lastWrite is { } w && now - w <= LiveStatusDetector.ActiveWindow && (end is not { } e || w - e > EndSettle);
    }

    private sealed class Session
    {
        public DateTimeOffset SessionStart { get; set; }

        public DateTimeOffset TurnStart { get; set; }

        public DateTimeOffset LastEvent { get; set; }

        public DateTimeOffset? PendingEnd { get; set; }

        public DateTimeOffset? LastEnd { get; set; }

        public bool Working { get; set; }

        public bool Waiting { get; set; }

        public string? Model { get; set; }

        public Dictionary<string, Sub> Subs { get; } = new(StringComparer.Ordinal);

        /// <summary>Claude Code: tool call id → the sub-agent that made the call (to nest the one it launched).</summary>
        public Dictionary<string, string> Launches { get; } = new(StringComparer.Ordinal);
    }

    private sealed class Sub
    {
        public DateTimeOffset? Start { get; set; }

        public DateTimeOffset? End { get; set; }

        public DateTimeOffset? PendingEnd { get; set; }

        public DateTimeOffset LastEvent { get; set; }

        public string? Name { get; set; }

        public string? Task { get; set; }

        public string? Model { get; set; }

        public string? ParentId { get; set; }

        public string? LaunchId { get; set; }

        public bool OwnSession { get; set; }

        public long Tokens { get; set; }
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
                    if (e.Sub is null)
                    {
                        // A sub-agent's writes are not its parent's: they must not keep the parent "still being written".
                        state.Sessions.Add(e.SessionId);
                    }
                }
            }, chunkSize, _ => { });
            ReadSubAgentMeta(file, state);
            return state;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return _files.GetValueOrDefault(file.Path); // locked or gone right now: try again next time
        }
    }

    /// <summary>
    /// Claude Code writes each sub-agent to <c>&lt;session&gt;/subagents/agent-&lt;id&gt;.jsonl</c> with an
    /// <c>agent-&lt;id&gt;.meta.json</c> beside it: its type, task, model, the tool call that launched it, and
    /// <c>finished</c> once it is done (its log has no end marker of its own: the last message ends with a tool call).
    /// The meta file is read again whenever it changes, so a sub-agent given more work and finishing again is seen too.
    /// A finished sub-agent ends at its log's last write.
    /// </summary>
    private void ReadSubAgentMeta(LogFile file, FileState state)
    {
        var name = Path.GetFileNameWithoutExtension(file.Path);
        var folder = Path.GetDirectoryName(file.Path);
        if (file.Tool != Tool.Claude || !name.StartsWith("agent-", StringComparison.Ordinal) || Path.GetFileName(folder) != "subagents")
        {
            return;
        }

        var metaPath = Path.Combine(folder!, name + ".meta.json");
        if (!fileSystem.File.Exists(metaPath) || fileSystem.File.GetLastWriteTimeUtc(metaPath) is var written && written == state.MetaWritten)
        {
            return;
        }

        try
        {
            var meta = Parsing.JsonLine.Parse(fileSystem.File.ReadAllText(metaPath));
            var session = Path.GetFileName(Path.GetDirectoryName(folder))!;
            var id = name["agent-".Length..];
            tracker.Apply(new ActivityEvent(Tool.Claude, session, ActivityKind.SubAgentInfo, file.LastWriteUtc, Sub: new SubAgent(
                id,
                Name: Parsing.JsonLine.String(Parsing.JsonLine.Get(meta, "agentType")),
                Task: Parsing.JsonLine.String(Parsing.JsonLine.Get(meta, "description")),
                Model: Parsing.JsonLine.String(Parsing.JsonLine.Get(meta, "pickedEffortModel")),
                LaunchId: Parsing.JsonLine.String(Parsing.JsonLine.Get(meta, "toolUseId")))));
            if (Parsing.JsonLine.Get(meta, "finished") is { ValueKind: System.Text.Json.JsonValueKind.True })
            {
                tracker.Apply(new ActivityEvent(Tool.Claude, session, ActivityKind.SubAgentEnd, file.LastWriteUtc, Sub: new SubAgent(id)));
            }

            state.MetaWritten = written;
        }
        catch (IOException)
        {
            // being written right now: try again next time
        }
    }

    /// <summary>Where reading of one file stopped and which sessions it contains.</summary>
    private sealed class FileState(IActivityParser parser)
    {
        public IActivityParser Parser { get; } = parser;

        /// <summary>When the Claude sub-agent meta file was last read (read again when it changes).</summary>
        public DateTime MetaWritten { get; set; }

        public long Offset { get; set; }

        public HashSet<string> Sessions { get; } = new(StringComparer.Ordinal);
    }
}
