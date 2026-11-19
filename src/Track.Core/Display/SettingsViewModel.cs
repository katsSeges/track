using System.Globalization;
using Track.Core.Providers;
using Track.Core.Settings;

namespace Track.Core.Display;

public enum SettingsTab
{
    Appearance,
    Providers,
    Behavior,
}

/// <summary>Plain .NET state and commands for the three Settings tabs.</summary>
public sealed class SettingsViewModel : ObservableObject
{
    private TrackSettings _settings;
    private SettingsTab _selectedTab;

    public SettingsViewModel(TrackSettings settings) => _settings = settings;

    public event EventHandler<TrackSettings>? SettingsChanged;

    public TrackSettings Settings => _settings;

    public SettingsTab SelectedTab
    {
        get => _selectedTab;
        set => Set(ref _selectedTab, value);
    }

    public void SetTheme(ThemeMode theme) =>
        Update(settings => settings with { Appearance = settings.Appearance with { Theme = theme } });

    public void SetMaterial(WindowMaterial material) =>
        Update(settings => settings with { Appearance = settings.Appearance with { Material = material } });

    public void SetAccentColor(string color)
    {
        ValidateColor(color, nameof(color));
        Update(settings => settings with { Appearance = settings.Appearance with { AccentColor = color.ToUpperInvariant() } });
    }

    public void SetProviderMode(Tool tool, ProviderMode mode) =>
        Update(settings => settings with { Providers = settings.Providers.WithMode(tool, mode) });

    public void SetProviderColors(Tool tool, string light, string dark)
    {
        ProviderCatalog.Get(tool);
        ValidateColor(light, nameof(light));
        ValidateColor(dark, nameof(dark));
        var colors = new Dictionary<Tool, ProviderColorOverride>(_settings.Appearance.ProviderColors)
        {
            [tool] = new ProviderColorOverride(light.ToUpperInvariant(), dark.ToUpperInvariant()),
        };
        Update(settings => settings with { Appearance = settings.Appearance with { ProviderColors = colors } });
    }

    public void ResetProviderColors() =>
        Update(settings => settings with { Appearance = settings.Appearance with { ProviderColors = [] } });

    public void SetNotificationsEnabled(bool enabled) =>
        Update(settings => settings with { Behavior = settings.Behavior with { NotificationsEnabled = enabled } });

    public void SetHotkey(bool enabled, string gesture) =>
        Update(settings => settings with { Behavior = settings.Behavior with { HotkeyEnabled = enabled, HotkeyGesture = gesture } });

    private void Update(Func<TrackSettings, TrackSettings> update)
    {
        var changed = update(_settings);
        if (changed == _settings)
        {
            return;
        }

        _settings = changed;
        Raise(nameof(Settings));
        SettingsChanged?.Invoke(this, changed);
    }

    private static void ValidateColor(string color, string parameter)
    {
        if (color.Length != 7
            || color[0] != '#'
            || !uint.TryParse(color.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
        {
            throw new ArgumentException("Use a six-digit hex color such as #AABBCC.", parameter);
        }
    }
}
