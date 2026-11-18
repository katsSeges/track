namespace Track.Core.Parsing;

/// <summary>
/// Turns lines of one log file into usage records. Create one instance per file and feed it
/// the file's lines in order; parsers keep state between lines (Codex totals, Claude duplicates).
/// </summary>
public interface ILogParser
{
    /// <summary>Returns a record for a line that carries new usage, otherwise null. Never throws on bad input.</summary>
    UsageRecord? Parse(string? line);

    /// <summary>State to hand back to <see cref="LogParsers.Create"/> to continue this file after a restart.</summary>
    ParserState SaveState();
}

public static class LogParsers
{
    /// <summary>A parser for one log file, fresh or continuing from saved state.</summary>
    /// <param name="fallbackSessionId">Used when the log itself does not name the session (e.g. the file or folder name).</param>
    public static ILogParser Create(Tool tool, string fallbackSessionId, ParserState? state = null) => tool switch
    {
        Tool.Claude => new ClaudeLogParser(fallbackSessionId, state),
        Tool.Codex => new CodexLogParser(fallbackSessionId, state),
        Tool.Copilot => new CopilotLogParser(fallbackSessionId),
        _ => throw new ArgumentOutOfRangeException(nameof(tool), tool, "Unknown tool."),
    };
}
