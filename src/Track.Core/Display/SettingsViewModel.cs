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
        var normalized = ColorPickerMath.NormalizeHex(color);
        Update(settings => settings with { Appearance = settings.Appearance with { AccentColor = normalized } });
    }

    public void SetProviderMode(Tool tool, ProviderMode mode) =>
        Update(settings => settings with { Providers = settings.Providers.WithMode(tool, mode) });

    public void SetChatTarget(Tool tool, string targetId)
    {
        var targets = ChatTargetResolver.TargetsFor(tool);
        if (targetId.Length > 0 && !targets.Any(target => target.Id == targetId))
        {
            throw new ArgumentException("Choose a supported chat target.", nameof(targetId));
        }

        Update(settings => settings with { Providers = settings.Providers.WithChatTarget(tool, targetId) });
    }

    public void SetProviderColors(Tool tool, string light, string dark)
    {
        ProviderCatalog.Get(tool);
        var normalizedLight = ColorPickerMath.NormalizeHex(light);
        var normalizedDark = ColorPickerMath.NormalizeHex(dark);
        var colors = new Dictionary<Tool, ProviderColorOverride>(_settings.Appearance.ProviderColors)
        {
            [tool] = new ProviderColorOverride(normalizedLight, normalizedDark),
        };
        Update(settings => settings with { Appearance = settings.Appearance with { ProviderColors = colors } });
    }

    public void ResetProviderColors() =>
        Update(settings => settings with { Appearance = settings.Appearance with { ProviderColors = [] } });

    public void SetNotificationsEnabled(bool enabled) =>
        Update(settings => settings with { Behavior = settings.Behavior with { NotificationsEnabled = enabled } });

    public void SetHotkey(bool enabled, string gesture) =>
        Update(settings =>
        {
            if (enabled && !HotkeyGestureParser.TryParse(gesture, out _))
            {
                throw new ArgumentException("Use a key combination such as Ctrl+Alt+Shift+T.", nameof(gesture));
            }

            return settings with { Behavior = settings.Behavior with { HotkeyEnabled = enabled, HotkeyGesture = gesture } };
        });

    public void SetBudgetEnabled(bool enabled) =>
        Update(settings => settings with { Behavior = settings.Behavior with { Budget = settings.Behavior.Budget with { Enabled = enabled } } });

    public void SetBudgetPeriod(BudgetPeriod period) =>
        Update(settings => settings with { Behavior = settings.Behavior with { Budget = settings.Behavior.Budget with { Period = period } } });

    public void SetBudgetThreshold(Tool? tool, BudgetThreshold threshold)
    {
        if (tool is { } provider)
        {
            ProviderCatalog.Get(provider);
        }

        if (threshold.Spend is <= 0 || threshold.Tokens is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(threshold), "Budget thresholds must be greater than zero or blank.");
        }

        Update(settings => settings with
        {
            Behavior = settings.Behavior with
            {
                Budget = settings.Behavior.Budget.WithThreshold(tool, threshold),
            },
        });
    }

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

}
