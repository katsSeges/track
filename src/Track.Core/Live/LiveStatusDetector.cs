using System.IO.Abstractions;
using System.Text.RegularExpressions;
using Track.Core.Ingest;
using Track.Core.Usage;

namespace Track.Core.Live;

/// <summary>What an agent is doing right now.</summary>
/// <param name="Working">A log of this tool was written within <see cref="LiveStatusDetector.ActiveWindow"/>: a model is answering.</param>
/// <param name="ActiveSessions">Sessions whose log was written within the active window.</param>
/// <param name="OpenSessions">Sessions held open by a live app process (Copilot <c>inuse.&lt;pid&gt;.hold</c> files; 0 for others).</param>
/// <param name="ProcessRunning">The tool's app or CLI process is running (Claude's desktop app counts, so this alone does not mean working).</param>
/// <param name="LastModel">Model used most in the latest hour with usage.</param>
/// <param name="LastActivityUtc">Newest log write within the last day, or null.</param>
/// <param name="SubAgents">Running sub-agents of this tool's working sessions (they count as sessions in the tray).</param>
public sealed record LiveStatus(
    Tool Tool,
    bool Working,
    int ActiveSessions,
    int OpenSessions,
    bool ProcessRunning,
    string? LastModel,
    DateTimeOffset? LastActivityUtc,
    int SubAgents = 0);

/// <summary>Checks every few seconds which agents are working, from log write times and processes.</summary>
public sealed partial class LiveStatusDetector(
    IFileSystem fileSystem,
    LogScanner scanner,
    string copilotSessionRoot,
    IProcessList processes,
    UsageAggregator usage,
    TimeProvider time)
{
    public static readonly TimeSpan ActiveWindow = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan ActivityLookback = TimeSpan.FromDays(1);

    public static readonly IReadOnlyDictionary<Tool, string[]> ProcessNames = new Dictionary<Tool, string[]>
    {
        [Tool.Claude] = ["claude"],
        [Tool.Codex] = ["codex"],
        [Tool.Copilot] = ["copilot-runtime", "copilotapp_proxy", "copilot"],
    };

    /// <param name="recent">Log files written in the last day (from <see cref="LogScanCache"/>); scanned here when null.</param>
    public IReadOnlyDictionary<Tool, LiveStatus> Detect(IReadOnlyList<LogFile>? recent = null)
    {
        var now = time.GetUtcNow();
        recent = (recent ?? scanner.Scan(now - ActivityLookback)).Where(f => f.LastWriteUtc >= now - ActivityLookback).ToList();
        var openCopilot = OpenCopilotSessions();

        return Enum.GetValues<Tool>().ToDictionary(tool => tool, tool =>
        {
            var files = recent.Where(f => f.Tool == tool).ToList();
            var active = files.Where(f => now - f.LastWriteUtc <= ActiveWindow).Select(f => f.SessionId).Distinct().Count();
            return new LiveStatus(
                tool,
                Working: active > 0,
                ActiveSessions: active,
                OpenSessions: tool == Tool.Copilot ? openCopilot : 0,
                ProcessRunning: processes.AnyRunning(ProcessNames[tool]),
                LastModel: usage.LatestModel(tool),
                LastActivityUtc: files.Count > 0 ? files.Max(f => f.LastWriteUtc) : null);
        });
    }

    /// <summary>
    /// The live status as the tray and popover show it, taken from the same working agents the island shows, so the
    /// two never disagree: a tool is working when at least one of its sessions is (an agent whose background
    /// sub-agents still run included), and the session and running sub-agent counts are theirs.
    /// </summary>
    public static IReadOnlyDictionary<Tool, LiveStatus> WithAgents(IReadOnlyDictionary<Tool, LiveStatus> live, IReadOnlyList<Activity.AgentActivity> working) =>
        live.ToDictionary(p => p.Key, p =>
        {
            var mine = working.Where(a => a.Tool == p.Key).ToList();
            return p.Value with { Working = mine.Count > 0, ActiveSessions = mine.Count, SubAgents = mine.Sum(a => a.Subs.Count(s => s.Running)) };
        });

    /// <summary>Copilot sessions with an <c>inuse.&lt;pid&gt;.hold</c> file whose process is alive.</summary>
    private int OpenCopilotSessions()
    {
        try
        {
            if (!fileSystem.Directory.Exists(copilotSessionRoot))
            {
                return 0;
            }

            // Hold files sit directly in each session folder; no need to walk checkpoints and file copies.
            return fileSystem.Directory.EnumerateDirectories(copilotSessionRoot)
                .SelectMany(dir => fileSystem.Directory.EnumerateFiles(dir, "inuse.*.hold", SearchOption.TopDirectoryOnly))
                .Where(path => HoldFile().Match(fileSystem.Path.GetFileName(path)) is { Success: true } m
                               && int.TryParse(m.Groups[1].Value, out var pid)
                               && processes.IsAlive(pid))
                .Select(path => fileSystem.Path.GetDirectoryName(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    [GeneratedRegex(@"^inuse\.(\d+)\.hold$", RegexOptions.IgnoreCase)]
    private static partial Regex HoldFile();
}
