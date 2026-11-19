namespace Track.Core.Display;

public sealed record ChatTargetOption(string Id, string Label);

public sealed record ChatTargetDefinition(
    string Id,
    string Label,
    string ExecutableName,
    IReadOnlyList<string> Arguments);

public sealed record ChatLaunchSpec(
    string TargetId,
    string Label,
    string ExecutableName,
    IReadOnlyList<string> Arguments);

public sealed record ChatTargetResolution(ChatLaunchSpec? Target, string? Message)
{
    public bool Found => Target is not null;
}

/// <summary>Resolves provider overrides against a fixed catalog of launch targets and separately passed arguments.</summary>
public static class ChatTargetResolver
{
    private static readonly IReadOnlyDictionary<Tool, IReadOnlyList<ChatTargetDefinition>> Targets =
        new Dictionary<Tool, IReadOnlyList<ChatTargetDefinition>>
        {
            [Tool.Claude] =
            [
                Target("claude-desktop", "Claude desktop", "Claude.exe"),
                Target("claude-cli", "Claude CLI", "claude.exe"),
            ],
            [Tool.Codex] =
            [
                Target("codex-desktop", "Codex app", "Codex.exe"),
                Target("codex-cli", "Codex CLI", "codex.exe"),
            ],
            [Tool.Copilot] =
            [
                Target("copilot-app", "GitHub Copilot", "GitHub Copilot.exe"),
                Target("vscode-chat", "VS Code chat", "Code.exe", "--command", "workbench.action.chat.open"),
                Target("copilot-cli", "Copilot CLI", "copilot.exe"),
            ],
            [Tool.Gemini] = [Target("gemini-cli", "Gemini CLI", "gemini.exe")],
            [Tool.OpenCode] = [Target("opencode-cli", "OpenCode CLI", "opencode.exe")],
            [Tool.Cline] = [Target("cline-vscode", "VS Code", "Code.exe", "--command", "workbench.action.chat.open")],
            [Tool.Roo] = [Target("roo-vscode", "VS Code", "Code.exe", "--command", "workbench.action.chat.open")],
            [Tool.Ollama] = [Target("ollama-cli", "Ollama", "ollama.exe", "run")],
        };

    public static IReadOnlyList<ChatTargetDefinition> TargetsFor(Tool tool) =>
        Targets.TryGetValue(tool, out var targets)
            ? targets
            : throw new ArgumentOutOfRangeException(nameof(tool), tool, "Unknown tool.");

    public static IReadOnlyList<ChatTargetOption> OptionsFor(Tool tool) =>
        [new ChatTargetOption("", "Auto"), .. TargetsFor(tool).Select(target => new ChatTargetOption(target.Id, target.Label))];

    public static ChatTargetResolution Resolve(
        Tool tool,
        string? overrideId,
        IReadOnlySet<string> availableTargetIds,
        string? lastModel = null)
    {
        var targets = TargetsFor(tool);
        if (!string.IsNullOrWhiteSpace(overrideId))
        {
            var selected = targets.FirstOrDefault(target => target.Id == overrideId);
            if (selected is null)
            {
                return new ChatTargetResolution(null, "The selected chat target is unsupported. Choose Auto or another target in Settings.");
            }

            return availableTargetIds.Contains(selected.Id)
                ? new ChatTargetResolution(ToSpec(selected, tool, lastModel), null)
                : new ChatTargetResolution(null, $"{selected.Label} was not found. Choose Auto or another target in Settings.");
        }

        var detected = targets.FirstOrDefault(target => availableTargetIds.Contains(target.Id));
        return detected is null
            ? new ChatTargetResolution(null, "No compatible chat app was found. Install one or choose a target in Settings.")
            : new ChatTargetResolution(ToSpec(detected, tool, lastModel), null);
    }

    private static ChatLaunchSpec ToSpec(ChatTargetDefinition target, Tool tool, string? lastModel)
    {
        var arguments = target.Arguments.ToList();
        if (tool == Tool.Ollama && !string.IsNullOrWhiteSpace(lastModel))
        {
            arguments.Add(lastModel);
        }

        return new ChatLaunchSpec(target.Id, target.Label, target.ExecutableName, arguments);
    }

    private static ChatTargetDefinition Target(string id, string label, string executable, params string[] arguments) =>
        new(id, label, executable, arguments);
}
