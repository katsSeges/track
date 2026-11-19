using System.IO.Abstractions;
using System.Text.Json;
using System.Text.Json.Serialization;
using Track.Core.Island;
using Track.Core.Usage;

namespace Track.Core.Settings;

/// <summary>Where the logos sit inside the 16 px tray icon (round 2).</summary>
public enum IconLayout
{
    Triangle,
    Row,
    RunningOnly,
}

/// <summary>How an idle tool is drawn in the tray icon (round 2).</summary>
public enum IdleLook
{
    GreyMark,
    Dot,
    Ring,
}

/// <summary>What the tray icon shows when no tool is running (round 2).</summary>
public enum NothingRunningLook
{
    AllGrey,
    TrackMark,
}

public enum ProviderMode
{
    Auto,
    On,
    Off,
}

public enum ThemeMode
{
    System,
    Light,
    Dark,
}

public enum WindowMaterial
{
    Acrylic,
    Mica,
    MicaAlt,
    Solid,
}

public sealed record ProviderColorOverride(string Light, string Dark);

public sealed record AppearanceSettings
{
    public ThemeMode Theme { get; init; } = ThemeMode.System;

    public WindowMaterial Material { get; init; } = WindowMaterial.Acrylic;

    public string AccentColor { get; init; } = "#0A66D8";

    public Dictionary<Tool, ProviderColorOverride> ProviderColors { get; init; } = [];

    public bool Equals(AppearanceSettings? other) =>
        other is not null
        && Theme == other.Theme
        && Material == other.Material
        && AccentColor == other.AccentColor
        && ProviderColors.Count == other.ProviderColors.Count
        && ProviderColors.All(pair => other.ProviderColors.TryGetValue(pair.Key, out var value) && pair.Value == value);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Theme);
        hash.Add(Material);
        hash.Add(AccentColor);
        foreach (var pair in ProviderColors.OrderBy(pair => pair.Key))
        {
            hash.Add(pair.Key);
            hash.Add(pair.Value);
        }

        return hash.ToHashCode();
    }
}

public sealed record BehaviorSettings
{
    public bool NotificationsEnabled { get; init; }

    public bool HotkeyEnabled { get; init; } = true;

    public string HotkeyGesture { get; init; } = "Ctrl+Alt+Shift+T";
}

public sealed record ProviderSettings
{
    public ProviderMode Claude { get; set; } = ProviderMode.Auto;

    public ProviderMode Codex { get; set; } = ProviderMode.Auto;

    public ProviderMode Copilot { get; set; } = ProviderMode.Auto;

    public ProviderMode Gemini { get; set; } = ProviderMode.Auto;

    public ProviderMode OpenCode { get; set; } = ProviderMode.Auto;

    public ProviderMode Cline { get; set; } = ProviderMode.Auto;

    public ProviderMode Roo { get; set; } = ProviderMode.Auto;

    public ProviderMode Ollama { get; set; } = ProviderMode.Off;

    public ProviderMode ModeFor(Tool tool) => tool switch
    {
        Tool.Claude => Claude,
        Tool.Codex => Codex,
        Tool.Copilot => Copilot,
        Tool.Gemini => Gemini,
        Tool.OpenCode => OpenCode,
        Tool.Cline => Cline,
        Tool.Roo => Roo,
        Tool.Ollama => Ollama,
        _ => throw new ArgumentOutOfRangeException(nameof(tool), tool, "Unknown tool."),
    };

    public ProviderSettings WithMode(Tool tool, ProviderMode mode) => tool switch
    {
        Tool.Claude => this with { Claude = mode },
        Tool.Codex => this with { Codex = mode },
        Tool.Copilot => this with { Copilot = mode },
        Tool.Gemini => this with { Gemini = mode },
        Tool.OpenCode => this with { OpenCode = mode },
        Tool.Cline => this with { Cline = mode },
        Tool.Roo => this with { Roo = mode },
        Tool.Ollama => this with { Ollama = mode },
        _ => throw new ArgumentOutOfRangeException(nameof(tool), tool, "Unknown tool."),
    };
}

/// <summary>Everything the user can change. Defaults are the choices from mockup rounds 1–4.</summary>
public sealed record TrackSettings
{
    public bool StartWithWindows { get; set; } = true;

    public Period LastPeriod { get; set; } = Period.Today;

    public int RefreshSeconds { get; set; } = 30;

    public IconLayout IconLayout { get; set; } = IconLayout.Triangle;

    public IdleLook IdleLook { get; set; } = IdleLook.GreyMark;

    public NothingRunningLook NothingRunningLook { get; set; } = NothingRunningLook.AllGrey;

    public bool RowLogos { get; set; } = true;

    public VisibilitySettings Visibility { get; set; } = new();

    public ProviderSettings Providers { get; set; } = new();

    public IslandSettings Island { get; set; } = new();

    public AppearanceSettings Appearance { get; set; } = new();

    public BehaviorSettings Behavior { get; set; } = new();

    /// <summary>The tray popover's Hide / Show chart toggle (user request 2026-11-18). False = chart shown.</summary>
    public bool PopoverChartHidden { get; set; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true, WriteIndented = true)]
[JsonSerializable(typeof(TrackSettings))]
internal sealed partial class SettingsJson : JsonSerializerContext;

/// <summary>
/// Reads and writes <c>%LOCALAPPDATA%\Track\settings.json</c>. A missing or broken file gives the
/// defaults; missing fields keep their defaults, so older files keep working.
/// </summary>
public sealed class SettingsStore(IFileSystem fileSystem, string path)
{
    public static string DefaultPath(string localAppData) => Path.Combine(localAppData, "Track", "settings.json");

    public TrackSettings Load()
    {
        try
        {
            return fileSystem.File.Exists(path)
                ? JsonSerializer.Deserialize(fileSystem.File.ReadAllText(path), SettingsJson.Default.TrackSettings) ?? new()
                : new();
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return new();
        }
    }

    /// <summary>Returns false if the file could not be written.</summary>
    public bool Save(TrackSettings settings)
    {
        var temp = path + ".tmp";
        try
        {
            fileSystem.Directory.CreateDirectory(fileSystem.Path.GetDirectoryName(path)!);
            fileSystem.File.WriteAllText(temp, JsonSerializer.Serialize(settings, SettingsJson.Default.TrackSettings));
            fileSystem.File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

/// <summary>The "start with Windows" entry (HKCU Run key in the real app).</summary>
public interface IStartupRegistry
{
    /// <summary>The command registered to start Track, or null when there is none.</summary>
    string? Get();

    void Set(string command);

    void Remove();
}

/// <summary>Keeps the startup entry in line with the setting, touching the registry only when it differs.</summary>
public sealed class StartupManager(IStartupRegistry registry, string exePath)
{
    public string Command => $"\"{exePath}\"";

    public void Apply(bool startWithWindows)
    {
        var current = registry.Get();
        if (startWithWindows && current != Command)
        {
            registry.Set(Command);
        }
        else if (!startWithWindows && current is not null)
        {
            registry.Remove();
        }
    }
}
