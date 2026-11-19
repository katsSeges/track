using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Track.Core;
using Track.Core.Display;
using Track.Core.Providers;
using Track.Core.Settings;
using ThemeMode = Track.Core.Settings.ThemeMode;

namespace Track.App.Shell;

[ExcludeFromCodeCoverage(Justification = "WPF Settings UI; state transitions and validation are tested in Track.Core.")]
internal sealed class SettingsWindow : Window
{
    private readonly Border _surface = new() { CornerRadius = new CornerRadius(12), Padding = new Thickness(18) };
    private SettingsViewModel _viewModel;
    private TabControl _tabs = null!;
    private TextBlock _materialNote = null!;

    public SettingsWindow(TrackSettings settings)
    {
        _viewModel = new SettingsViewModel(settings);
        _viewModel.SettingsChanged += OnSettingsChanged;
        Title = "Track Settings";
        Width = 560;
        MinWidth = 500;
        SizeToContent = SizeToContent.Height;
        MaxHeight = SystemParameters.WorkArea.Height - 40;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        Background = Brushes.Transparent;
        Foreground = Brushes.Black;
        Content = _surface;
        SourceInitialized += (_, _) =>
        {
            Native.MakeToolWindow(this, noActivate: false);
            ApplyBackdrop();
        };
        BuildTabs();
    }

    public event EventHandler<TrackSettings>? SettingsChanged;

    public bool Dark { get; set; }

    public void ApplySettings(TrackSettings settings)
    {
        var selected = _viewModel.SelectedTab;
        _viewModel.SettingsChanged -= OnSettingsChanged;
        _viewModel = new SettingsViewModel(settings) { SelectedTab = selected };
        _viewModel.SettingsChanged += OnSettingsChanged;
        BuildTabs();
        ApplyBackdrop();
    }

    public void SelectTab(SettingsTab tab)
    {
        _viewModel.SelectedTab = tab;
        _tabs.SelectedIndex = (int)tab;
    }

    public void ApplyBackdrop()
    {
        var supported = Native.ApplyBackdrop(this, Dark, _viewModel.Settings.Appearance.Material);
        _surface.SetResourceReference(Border.BackgroundProperty, supported ? "PopBg" : "PopSolid");
        _materialNote.Text = supported || _viewModel.Settings.Appearance.Material == WindowMaterial.Solid
            ? "Changes apply immediately and are saved automatically."
            : "This material is not supported on this Windows version; Track is using Solid.";
    }

    private void BuildTabs()
    {
        _tabs = new TabControl { SelectedIndex = (int)_viewModel.SelectedTab };
        _tabs.SelectionChanged += (_, _) =>
        {
            if (_tabs.SelectedIndex >= 0 && _tabs.SelectedIndex < Enum.GetValues<SettingsTab>().Length)
            {
                _viewModel.SelectedTab = (SettingsTab)_tabs.SelectedIndex;
            }
        };
        _tabs.Items.Add(new TabItem { Header = "Appearance", Content = AppearanceTab() });
        _tabs.Items.Add(new TabItem { Header = "Providers", Content = ProvidersTab() });
        _tabs.Items.Add(new TabItem { Header = "Behavior", Content = BehaviorTab() });
        _tabs.SetResourceReference(Control.ForegroundProperty, "Fg");
        var root = new StackPanel();
        root.Children.Add(new TextBlock { Text = "Settings", FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, 0, 0, 12) });
        root.Children.Add(_tabs);
        _materialNote = new TextBlock { Margin = new Thickness(3, 10, 3, 0), FontSize = 11 };
        root.Children.Add(_materialNote);
        root.SetResourceReference(Control.ForegroundProperty, "Fg");
        _surface.Child = root;
        _materialNote.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
    }

    private UIElement AppearanceTab()
    {
        var settings = _viewModel.Settings.Appearance;
        var panel = TabPanel();
        panel.Children.Add(Labeled("Theme", Combo(Enum.GetValues<ThemeMode>(), settings.Theme, value => _viewModel.SetTheme(value))));
        panel.Children.Add(Labeled("Window material", Combo(Enum.GetValues<WindowMaterial>(), settings.Material, value =>
        {
            _viewModel.SetMaterial(value);
            ApplyBackdrop();
        })));
        var accent = new TextBox { Text = settings.AccentColor, Width = 110, MaxLength = 7 };
        var accentError = ErrorText();
        accent.LostFocus += (_, _) =>
        {
            try
            {
                _viewModel.SetAccentColor(accent.Text);
                accentError.Text = "";
            }
            catch (ArgumentException error)
            {
                accentError.Text = error.Message;
            }
        };
        panel.Children.Add(Labeled("Accent color", accent));
        panel.Children.Add(accentError);
        panel.Children.Add(new TextBlock { Text = "Provider colors", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 4) });

        foreach (var provider in ProviderCatalog.All)
        {
            var colors = settings.ProviderColors.TryGetValue(provider.Id, out var custom)
                ? custom
                : new ProviderColorOverride(provider.Colors.Light, provider.Colors.Dark);
            var light = ColorBox(colors.Light);
            var dark = ColorBox(colors.Dark);
            var error = ErrorText();
            void SaveColors()
            {
                try
                {
                    _viewModel.SetProviderColors(provider.Id, light.Text, dark.Text);
                    error.Text = "";
                }
                catch (ArgumentException exception)
                {
                    error.Text = exception.Message;
                }
            }

            light.LostFocus += (_, _) => SaveColors();
            dark.LostFocus += (_, _) => SaveColors();
            panel.Children.Add(Labeled(provider.Name, new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    light,
                    new TextBlock { Text = "Light", Margin = new Thickness(4, 5, 12, 0), FontSize = 11 },
                    dark,
                    new TextBlock { Text = "Dark", Margin = new Thickness(4, 5, 0, 0), FontSize = 11 },
                },
            }));
            panel.Children.Add(error);
        }

        var reset = new Button { Content = "Reset provider colors", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0), Padding = new Thickness(10, 4, 10, 4) };
        reset.Click += (_, _) => _viewModel.ResetProviderColors();
        panel.Children.Add(reset);
        return Scroll(panel);
    }

    private UIElement ProvidersTab()
    {
        var panel = TabPanel();
        panel.Children.Add(new TextBlock
        {
            Text = "Auto shows providers with recent usage or live activity. On always shows a provider; Off disables scanning and display.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
        });
        foreach (var provider in ProviderCatalog.All)
        {
            var mode = _viewModel.Settings.Providers.ModeFor(provider.Id);
            var selector = Combo(Enum.GetValues<ProviderMode>(), mode, value => _viewModel.SetProviderMode(provider.Id, value));
            panel.Children.Add(Labeled(provider.Name, selector));
        }

        return Scroll(panel);
    }

    private UIElement BehaviorTab()
    {
        var settings = _viewModel.Settings.Behavior;
        var panel = TabPanel();
        var notifications = new CheckBox { Content = "Enable notifications", IsChecked = settings.NotificationsEnabled, Margin = new Thickness(0, 0, 0, 10) };
        notifications.Checked += (_, _) => _viewModel.SetNotificationsEnabled(true);
        notifications.Unchecked += (_, _) => _viewModel.SetNotificationsEnabled(false);
        panel.Children.Add(notifications);
        panel.Children.Add(new TextBlock
        {
            Text = "This switch controls both agent-event and budget-alert notifications.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16),
        });

        var gesture = new TextBox { Text = settings.HotkeyGesture, Width = 180, MaxLength = 64 };
        var hotkey = new CheckBox { Content = "Enable global popover hotkey", IsChecked = settings.HotkeyEnabled, Margin = new Thickness(0, 0, 0, 8) };
        hotkey.Checked += (_, _) => _viewModel.SetHotkey(true, gesture.Text);
        hotkey.Unchecked += (_, _) => _viewModel.SetHotkey(false, gesture.Text);
        panel.Children.Add(hotkey);
        gesture.LostFocus += (_, _) => _viewModel.SetHotkey(hotkey.IsChecked == true, gesture.Text);
        panel.Children.Add(Labeled("Hotkey", gesture));
        return panel;
    }

    private void OnSettingsChanged(object? sender, TrackSettings settings) => SettingsChanged?.Invoke(this, settings);

    private static StackPanel TabPanel() => new() { Margin = new Thickness(14), Orientation = Orientation.Vertical };

    private static ScrollViewer Scroll(UIElement content) => new()
    {
        Content = content,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        MaxHeight = Math.Max(220, SystemParameters.WorkArea.Height - 220),
    };

    private static UIElement Labeled(string label, UIElement control)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(145) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        text.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
        Grid.SetColumn(control, 1);
        grid.Children.Add(text);
        grid.Children.Add(control);
        return grid;
    }

    private static ComboBox Combo<T>(IEnumerable<T> values, T selected, Action<T> changed)
    {
        var combo = new ComboBox { ItemsSource = values, SelectedItem = selected, MinWidth = 170, Padding = new Thickness(5, 3, 5, 3) };
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is T value)
            {
                changed(value);
            }
        };
        return combo;
    }

    private static TextBox ColorBox(string color) => new()
    {
        Text = color,
        Width = 78,
        MaxLength = 7,
        Padding = new Thickness(3),
    };

    private static TextBlock ErrorText() => new() { Foreground = Brushes.IndianRed, FontSize = 10.5, TextWrapping = TextWrapping.Wrap };
}
