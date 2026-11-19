using Track.Core.Ingest;
using Track.Core.Tray;

namespace Track.Core.Providers;

public sealed record ProviderColors(string Light, string Dark)
{
    public string For(TaskbarTheme theme) => theme == TaskbarTheme.Light ? Light : Dark;
}

public sealed record ProviderCapabilities(bool HasTokens, bool HasActivity);

public sealed record ProviderInfo(
    Tool Id,
    string Name,
    string LogoKey,
    ProviderColors Colors,
    IReadOnlyList<string> ProcessNames,
    ProviderCapabilities Capabilities,
    bool UsesLegacyVisibility,
    Func<string, IReadOnlyList<LogSource>> Sources);

/// <summary>Canonical provider ordering and metadata used by ingestion, live status and display.</summary>
public static class ProviderCatalog
{
    private static readonly IReadOnlyList<ProviderInfo> Providers = Array.AsReadOnly<ProviderInfo>(
    [
        new(Tool.Claude, "Claude Code", "claude", new("#D97757", "#E08560"), ["claude"], new(true, true), true, ClaudeSources),
        new(Tool.Codex, "Codex", "codex", new("#2B2B2E", "#E4E4EA"), ["codex"], new(true, true), true, CodexSources),
        new(Tool.Copilot, "Copilot", "copilot", new("#7A4FD6", "#A98BFF"), ["copilot-runtime", "copilotapp_proxy", "copilot"], new(true, true), true, CopilotSources),
        new(Tool.Gemini, "Gemini CLI", "gemini", new("#4285F4", "#8AB4F8"), ["gemini"], new(true, true), false, GeminiSources),
        new(Tool.OpenCode, "OpenCode", "opencode", new("#2D6A4F", "#6FCF97"), ["opencode"], new(true, true), false, OpenCodeSources),
        new(Tool.Cline, "Cline", "cline", new("#0B8F87", "#55DAD0"), [], new(true, true), false, ClineSources),
        new(Tool.Roo, "Roo Code", "roo", new("#A74BC8", "#D38AE8"), [], new(true, true), false, RooSources),
        new(Tool.Ollama, "Ollama", "ollama", new("#454545", "#E4E4EA"), ["ollama"], new(false, true), false, NoSources),
    ]);

    private static readonly IReadOnlyList<Tool> Tools = Array.AsReadOnly(Providers.Select(p => p.Id).ToArray());
    private static readonly IReadOnlyList<Tool> LegacyTools = Array.AsReadOnly(Providers.Where(p => p.UsesLegacyVisibility).Select(p => p.Id).ToArray());
    private static readonly IReadOnlyDictionary<Tool, ProviderInfo> ByTool = Providers.ToDictionary(p => p.Id);

    public static IReadOnlyList<ProviderInfo> All => Providers;

    public static IReadOnlyList<Tool> Order => Tools;

    public static IReadOnlyList<Tool> LegacyOrder => LegacyTools;

    public static ProviderInfo Get(Tool tool) => ByTool.TryGetValue(tool, out var provider)
        ? provider
        : throw new ArgumentOutOfRangeException(nameof(tool), tool, "Unknown tool.");

    public static IReadOnlyList<LogSource> LogSources(string home) => Providers
        .SelectMany(provider => provider.Sources(home))
        .ToList();

    private static IReadOnlyList<LogSource> ClaudeSources(string home) =>
    [
        new(Tool.Claude, Path.Combine(home, ".claude", "projects"), "*.jsonl", SessionIdFrom.FileName),
        new(Tool.Claude, Path.Combine(home, ".config", "claude", "projects"), "*.jsonl", SessionIdFrom.FileName),
    ];

    private static IReadOnlyList<LogSource> CodexSources(string home) =>
    [
        new(Tool.Codex, Path.Combine(home, ".codex", "sessions"), "rollout-*.jsonl", SessionIdFrom.FileName),
        new(Tool.Codex, Path.Combine(home, ".codex", "archived_sessions"), "rollout-*.jsonl", SessionIdFrom.FileName),
    ];

    private static IReadOnlyList<LogSource> CopilotSources(string home) =>
    [
        new(Tool.Copilot, Path.Combine(home, ".copilot", "session-state"), "events.jsonl", SessionIdFrom.ParentFolder),
    ];

    private static IReadOnlyList<LogSource> GeminiSources(string home) =>
    [
        new(Tool.Gemini, Path.Combine(home, ".gemini", "tmp"), "session-*.jsonl", SessionIdFrom.FileName, LogReadMode.GeminiJsonLines),
    ];

    private static IReadOnlyList<LogSource> OpenCodeSources(string home)
    {
        var dataRoot = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (string.IsNullOrWhiteSpace(dataRoot))
        {
            dataRoot = Path.Combine(home, ".local", "share");
        }

        var opencodeData = Path.Combine(dataRoot, "opencode");
        var configured = Environment.GetEnvironmentVariable("OPENCODE_DB");
        if (configured == ":memory:")
        {
            return [];
        }

        var databasePath = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(opencodeData, "opencode.db")
            : Path.IsPathRooted(configured)
                ? configured
                : Path.GetFullPath(Path.Combine(opencodeData, configured));
        var pattern = string.IsNullOrWhiteSpace(configured) ? "opencode*.db*" : Path.GetFileName(databasePath) + "*";
        return [new(Tool.OpenCode, Path.GetDirectoryName(databasePath)!, pattern, SessionIdFrom.FileName, LogReadMode.OpenCodeDatabase)];
    }

    private static IReadOnlyList<LogSource> ClineSources(string home) => VscodeTaskSources(home, "saoudrizwan.claude-dev", Tool.Cline);

    private static IReadOnlyList<LogSource> RooSources(string home) => VscodeTaskSources(home, "RooVeterinaryInc.roo-cline", Tool.Roo);

    private static IReadOnlyList<LogSource> VscodeTaskSources(string home, string extensionId, Tool tool) =>
    [
        .. new[] { "Code", "Code - Insiders", "VSCodium" }.Select(product =>
            new LogSource(
                tool,
                Path.Combine(home, "AppData", "Roaming", product, "User", "globalStorage", extensionId, "tasks"),
                "ui_messages.json",
                SessionIdFrom.ParentFolder,
                LogReadMode.JsonDocument)),
    ];

    private static IReadOnlyList<LogSource> NoSources(string _) => [];
}
