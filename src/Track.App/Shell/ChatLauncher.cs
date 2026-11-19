using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Track.Core;
using Track.Core.Display;

namespace Track.App.Shell;

[ExcludeFromCodeCoverage(Justification = "Windows process launching is App-only; catalog resolution and argument boundaries are tested in Track.Core.")]
internal static class ChatLauncher
{
    public static string TooltipFor(Tool tool, string? overrideId)
    {
        var available = AvailableExecutables(tool);
        var result = ChatTargetResolver.Resolve(tool, overrideId, available.Keys.ToHashSet(StringComparer.Ordinal));
        return result.Target is { } target
            ? $"Click to open {target.Label}"
            : result.Message ?? "No compatible chat app was found.";
    }

    public static ChatTargetResolution Launch(Tool tool, string? overrideId, string? lastModel)
    {
        var available = AvailableExecutables(tool);
        var result = ChatTargetResolver.Resolve(tool, overrideId, available.Keys.ToHashSet(StringComparer.Ordinal), lastModel);
        if (result.Target is not { } target)
        {
            return result;
        }

        try
        {
            var executable = available[target.TargetId];
            var terminal = target.Kind == ChatTargetKind.CommandLine ? FindTerminal() : null;
            var start = terminal is null
                ? new ProcessStartInfo(executable) { UseShellExecute = true }
                : TerminalStartInfo(terminal, executable, target);
            if (terminal is null)
            {
                AddArguments(start, target.Arguments);
            }

            if (Process.Start(start) is null)
            {
                return new ChatTargetResolution(null, $"Windows could not start {target.Label}.");
            }

            return result;
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException or IOException or ArgumentException)
        {
            return new ChatTargetResolution(null, $"Could not open {target.Label}: {error.Message}");
        }
    }

    private static ProcessStartInfo TerminalStartInfo(string terminal, string executable, ChatLaunchSpec target)
    {
        var start = new ProcessStartInfo(terminal) { UseShellExecute = false };
        // Windows Terminal splits its command line at ';' (even after --): escape it inside arguments.
        AddArguments(start, ["-w", "0", "new-tab", "--title", $"Track - {target.Label}", "--", executable,
            .. target.Arguments.Select(argument => argument.Replace(";", @"\;", StringComparison.Ordinal))]);
        return start;
    }

    private static void AddArguments(ProcessStartInfo start, IEnumerable<string> arguments)
    {
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
    }

    private static ChatSearchFolders SearchFolders() => new(
        Environment.GetEnvironmentVariable("PATH"),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));

    private static IReadOnlyDictionary<string, string> AvailableExecutables(Tool tool)
    {
        var folders = SearchFolders();
        return ChatTargetResolver.TargetsFor(tool)
            .Select(target => (target.Id, Path: ChatTargetResolver.CandidatePaths(target, folders).FirstOrDefault(File.Exists)))
            .Where(item => item.Path is not null)
            .ToDictionary(item => item.Id, item => item.Path!, StringComparer.Ordinal);
    }

    private static string? FindTerminal() =>
        ChatTargetResolver.CandidatePaths(
                new ChatTargetDefinition("terminal", "Windows Terminal", "wt.exe", ChatTargetKind.CommandLine, [], []),
                SearchFolders())
            .FirstOrDefault(File.Exists);
}
