namespace Track.Core.Display;

public sealed record ChatTargetOption(string Id, string Label);

/// <summary>A desktop app (found only in its install folders) or a command-line tool (found on PATH, run in a terminal).</summary>
public enum ChatTargetKind
{
    App,
    CommandLine,
}

/// <param name="InstallFolders">For an app: folders under %LOCALAPPDATA% and Program Files that hold it.</param>
public sealed record ChatTargetDefinition(
    string Id,
    string Label,
    string ExecutableName,
    ChatTargetKind Kind,
    IReadOnlyList<string> InstallFolders,
    IReadOnlyList<string> Arguments);

public sealed record ChatLaunchSpec(
    string TargetId,
    string Label,
    string ExecutableName,
    IReadOnlyList<string> Arguments,
    ChatTargetKind Kind = ChatTargetKind.App);

/// <summary>The folders a target's executable is looked for in (from the environment, so it can be tested).</summary>
public sealed record ChatSearchFolders(string? PathVariable, string LocalAppData, string ProgramFiles, string AppData);

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
                App("claude-desktop", "Claude desktop", "Claude.exe", "AnthropicClaude", @"Programs\Claude"),
                Cli("claude-cli", "Claude CLI", "claude.exe"),
            ],
            [Tool.Codex] =
            [
                App("codex-desktop", "Codex app", "Codex.exe", @"Programs\Codex"),
                Cli("codex-cli", "Codex CLI", "codex.exe"),
            ],
            [Tool.Copilot] =
            [
                App("copilot-app", "GitHub Copilot", "GitHub Copilot.exe", @"Programs\GitHub Copilot"),
                App("vscode-chat", "VS Code", "Code.exe", VsCode),
                Cli("copilot-cli", "Copilot CLI", "copilot.exe"),
            ],
            [Tool.Gemini] = [Cli("gemini-cli", "Gemini CLI", "gemini.exe")],
            [Tool.OpenCode] = [Cli("opencode-cli", "OpenCode CLI", "opencode.exe")],
            [Tool.Cline] = [App("cline-vscode", "VS Code", "Code.exe", VsCode)],
            [Tool.Roo] = [App("roo-vscode", "VS Code", "Code.exe", VsCode)],
            [Tool.Ollama] = [Cli("ollama-cli", "Ollama", "ollama.exe", "run")],
        };

    private const string VsCode = @"Programs\Microsoft VS Code|Microsoft VS Code";

    /// <summary>
    /// Where <paramref name="target"/>'s executable may be, in order. An app is looked for only in its install
    /// folders: Windows file names ignore case, so on PATH the Claude and Codex apps would be the CLIs' claude.exe
    /// and codex.exe. A command-line tool is looked for in fully qualified, local PATH folders, then npm's folder.
    /// </summary>
    public static IEnumerable<string> CandidatePaths(ChatTargetDefinition target, ChatSearchFolders folders)
    {
        if (target.Kind == ChatTargetKind.App)
        {
            foreach (var folder in target.InstallFolders)
            {
                yield return Path.Combine(folders.LocalAppData, folder, target.ExecutableName);
                yield return Path.Combine(folders.ProgramFiles, folder, target.ExecutableName);
            }

            yield break;
        }

        foreach (var entry in (folders.PathVariable ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // A relative entry depends on the current folder, and a network (\\server) folder is not this computer's.
            if (Path.IsPathFullyQualified(entry) && !entry.StartsWith(@"\\", StringComparison.Ordinal))
            {
                yield return Path.Combine(entry, target.ExecutableName);
            }
        }

        yield return Path.Combine(folders.AppData, "npm", target.ExecutableName);
    }

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

        return new ChatLaunchSpec(target.Id, target.Label, target.ExecutableName, arguments, target.Kind);
    }

    private static ChatTargetDefinition App(string id, string label, string executable, params string[] installFolders) =>
        new(id, label, executable, ChatTargetKind.App, installFolders.SelectMany(f => f.Split('|')).ToList(), []);

    private static ChatTargetDefinition Cli(string id, string label, string executable, params string[] arguments) =>
        new(id, label, executable, ChatTargetKind.CommandLine, [], arguments);
}
