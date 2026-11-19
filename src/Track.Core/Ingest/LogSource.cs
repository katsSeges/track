using Track.Core.Providers;

namespace Track.Core.Ingest;

/// <summary>One folder of agent logs: where to look, which files, and how to name the session.</summary>
/// <param name="SessionFrom">How a file path becomes the session id the parser falls back to.</param>
public sealed record LogSource(Tool Tool, string Root, string Pattern, SessionIdFrom SessionFrom);

public enum SessionIdFrom
{
    /// <summary>File name without extension (Claude <c>&lt;session&gt;.jsonl</c>, Codex <c>rollout-*.jsonl</c>).</summary>
    FileName,

    /// <summary>Name of the folder holding the file (Copilot <c>&lt;session&gt;/events.jsonl</c>).</summary>
    ParentFolder,
}

/// <summary>A log file found by the scanner, with when it was last written.</summary>
public sealed record LogFile(Tool Tool, string Path, string SessionId, DateTimeOffset LastWriteUtc = default);

public static class LogSources
{
    /// <summary>Known log locations for every provider, under the user's home folder.</summary>
    public static IReadOnlyList<LogSource> Default(string home) => ProviderCatalog.LogSources(home);
}
