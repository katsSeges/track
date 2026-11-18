namespace Track.Core;

/// <summary>The AI coding agents Track reads usage for.</summary>
public enum Tool
{
    Claude,
    Codex,
    Copilot,
}

public static class ToolNames
{
    /// <summary>Name shown in the popover, tooltip and island.</summary>
    public static string Display(Tool tool) => tool switch
    {
        Tool.Claude => "Claude Code",
        Tool.Codex => "Codex",
        Tool.Copilot => "Copilot",
        _ => throw new ArgumentOutOfRangeException(nameof(tool), tool, "Unknown tool."),
    };
}
