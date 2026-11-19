namespace Track.Core;

/// <summary>The AI coding agents Track reads usage for.</summary>
public enum Tool
{
    Claude,
    Codex,
    Copilot,
    Gemini,
    OpenCode,
    Cline,
    Roo,
    Ollama,
}

public static class ToolNames
{
    /// <summary>Name shown in the popover, tooltip and island.</summary>
    public static string Display(Tool tool) => Providers.ProviderCatalog.Get(tool).Name;
}
