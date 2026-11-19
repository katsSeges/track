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
            var terminal = IsCommandLineTarget(target.TargetId) ? FindExecutable("wt.exe") : null;
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
        AddArguments(start, ["-w", "0", "new-tab", "--title", $"Track - {target.Label}", "--", executable, .. target.Arguments]);
        return start;
    }

    private static void AddArguments(ProcessStartInfo start, IEnumerable<string> arguments)
    {
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
    }

    private static bool IsCommandLineTarget(string targetId) =>
        targetId.EndsWith("-cli", StringComparison.Ordinal) || targetId == "ollama-cli";

    private static IReadOnlyDictionary<string, string> AvailableExecutables(Tool tool) =>
        ChatTargetResolver.TargetsFor(tool)
            .Select(target => (target.Id, Path: FindExecutable(target.ExecutableName)))
            .Where(item => item.Path is not null)
            .ToDictionary(item => item.Id, item => item.Path!, StringComparer.Ordinal);

    private static string? FindExecutable(string executableName)
    {
        foreach (var directory in CandidateDirectories())
        {
            var candidate = Path.Combine(directory, executableName);
            if (File.Exists(candidate) && IsTrustedLocalPath(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    private static IEnumerable<string> CandidateDirectories()
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(path))
        {
            foreach (var entry in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (Path.IsPathFullyQualified(entry) && IsTrustedLocalPath(entry))
                {
                    yield return Path.GetFullPath(entry);
                }
            }
        }

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        foreach (var directory in new[]
        {
            Path.Combine(local, "Programs", "Microsoft VS Code"),
            Path.Combine(local, "Programs", "Claude"),
            Path.Combine(local, "Programs", "Ollama"),
            Path.Combine(local, "Microsoft", "WindowsApps"),
            Path.Combine(appData, "npm"),
        })
        {
            if (Directory.Exists(directory))
            {
                yield return directory;
            }
        }
    }

    private static bool IsTrustedLocalPath(string path)
    {
        if (path.StartsWith(@"\\", StringComparison.Ordinal) || !Path.IsPathFullyQualified(path))
        {
            return false;
        }

        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return TrustedRoots().Any(root =>
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                return false;
            }

            var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static IEnumerable<string> TrustedRoots()
    {
        yield return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }
}
