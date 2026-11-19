using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Track.Core;
using Track.Core.Display;
using Track.Core.Providers;
using Track.Core.Settings;
using ThemeMode = Track.Core.Settings.ThemeMode;

namespace Track.App.Shell;

[ExcludeFromCodeCoverage(Justification = "WPF Settings UI; state transitions and validation are tested in Track.Core.")]
internal sealed class SettingsWindow : Window
{
    private readonly Border _surface = new() { CornerRadius = new CornerRadius(12), Padding = new Thickness(16) };
    private SettingsViewModel _viewModel;
    private TabControl _tabs = null!;
    private TextBlock _materialNote = null!;
    private TextBlock _hotkeyStatus = null!;
    private ColorPickerEditor? _accentEditor;
    private ComboBox? _providerModeSelector;

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
        SetResourceReference(Control.ForegroundProperty, "Fg");
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

    public void SaveColorPickerForRender(string path, string color) => _accentEditor?.SavePopupForRender(path, color);

    public void SaveProviderDropdownForRender(string path)
    {
        if (_providerModeSelector is null)
        {
            return;
        }

        _viewModel.SelectedTab = SettingsTab.Providers;
        _tabs.SelectedIndex = (int)SettingsTab.Providers;
        _providerModeSelector.ApplyTemplate();
        _providerModeSelector.IsDropDownOpen = true;
        UpdateLayout();
        if (_providerModeSelector.Template.FindName("PART_Popup", _providerModeSelector) is Popup { Child: FrameworkElement popupContent } popup)
        {
            popupContent.Measure(new Size(400, 300));
            popupContent.Arrange(new Rect(new Point(0, 0), popupContent.DesiredSize));
            popupContent.UpdateLayout();
            SaveElement(popupContent, path);
        }
    }

    public void SetHotkeyStatus(string status) => _hotkeyStatus.Text = status;

    public void ApplyBackdrop()
    {
        var supported = Native.ApplyBackdrop(this, Dark, _viewModel.Settings.Appearance.Material);
        _surface.SetResourceReference(Border.BackgroundProperty, supported ? "PopBg" : "PopSolid");
        _surface.SetResourceReference(Border.BorderBrushProperty, "Line");
        _materialNote.Text = supported || _viewModel.Settings.Appearance.Material == WindowMaterial.Solid
            ? "Changes apply immediately and are saved automatically."
            : "This material is not supported on this Windows version; Track is using Solid.";
    }

    private void BuildTabs()
    {
        _accentEditor = null;
        _providerModeSelector = null;
        _tabs = new TabControl { SelectedIndex = (int)_viewModel.SelectedTab };
        _tabs.SetResourceReference(Control.BackgroundProperty, "PopSolid");
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
        var title = new TextBlock { Text = "Settings", FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, 0, 0, 10) };
        title.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
        root.Children.Add(title);
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
        panel.Children.Add(SectionHeading("Appearance", "Choose how Track follows Windows and how its windows feel."));
        var theme = Combo(Enum.GetValues<ThemeMode>(), settings.Theme, value => _viewModel.SetTheme(value));
        var material = Combo(Enum.GetValues<WindowMaterial>(), settings.Material, value =>
        {
            _viewModel.SetMaterial(value);
            ApplyBackdrop();
        });
        _accentEditor = new ColorPickerEditor("Accent", settings.AccentColor, new AppearanceSettings().AccentColor, _viewModel.SetAccentColor);
        var appearanceFields = new StackPanel();
        appearanceFields.Children.Add(FormRow("Theme", theme));
        appearanceFields.Children.Add(FormRow("Window material", material));
        appearanceFields.Children.Add(FormRow("Accent color", _accentEditor));
        panel.Children.Add(appearanceFields);
        panel.Children.Add(PreferenceDivider());

        var providerColors = PreferenceGroup("Provider colors");
        var colorsNote = MutedText("Light and dark colors are independent. Use a swatch to open the color wheel.");
        colorsNote.TextWrapping = TextWrapping.Wrap;
        colorsNote.Margin = new Thickness(0, 0, 0, 6);
        providerColors.Children.Add(colorsNote);
        panel.Children.Add(providerColors);
        var columns = new Grid { Margin = new Thickness(2, 0, 2, 4) };
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(136) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(136) });
        AddGridText(columns, "Provider", 0);
        AddGridText(columns, "Light", 1, muted: true);
        AddGridText(columns, "Dark", 2, muted: true);
        panel.Children.Add(columns);
        var colorRows = new StackPanel();
        var providers = ProviderCatalog.All;
        for (var index = 0; index < providers.Count; index++)
        {
            var provider = providers[index];
            var colors = settings.ProviderColors.TryGetValue(provider.Id, out var custom)
                ? custom
                : new ProviderColorOverride(provider.Colors.Light, provider.Colors.Dark);
            var row = new Grid { Margin = new Thickness(2, 2, 2, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(136) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(136) });
            AddGridText(row, provider.Name, 0);
            var light = new ColorPickerEditor("Light", colors.Light, provider.Colors.Light, value =>
                _viewModel.SetProviderColors(provider.Id, value, colors.Dark), showLabel: false);
            var dark = new ColorPickerEditor("Dark", colors.Dark, provider.Colors.Dark, value =>
                _viewModel.SetProviderColors(provider.Id, colors.Light, value), showLabel: false);
            Grid.SetColumn(light, 1);
            Grid.SetColumn(dark, 2);
            row.Children.Add(light);
            row.Children.Add(dark);
            colorRows.Children.Add(row);
        }

        panel.Children.Add(colorRows);
        var reset = new Button
        {
            Content = "Reset provider colors",
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(2, 5, 0, 0),
            Style = (Style)FindResource("LinkButton"),
        };
        reset.Click += (_, _) => _viewModel.ResetProviderColors();
        panel.Children.Add(reset);
        return Scroll(panel);
    }

    private UIElement ProvidersTab()
    {
        var panel = TabPanel();
        panel.Children.Add(SectionHeading("Providers", "Auto follows recent usage or live status. On always shows the provider; Off disables scanning and display."));
        var header = ProviderRowGrid();
        AddGridText(header, "Provider", 0, muted: true);
        AddGridText(header, "Display", 1, muted: true);
        AddGridText(header, "Open chat with", 3, muted: true);
        panel.Children.Add(header);
        var providers = ProviderCatalog.All;
        for (var index = 0; index < providers.Count; index++)
        {
            var provider = providers[index];
            var mode = _viewModel.Settings.Providers.ModeFor(provider.Id);
            var selector = Combo(Enum.GetValues<ProviderMode>(), mode, value => _viewModel.SetProviderMode(provider.Id, value), minWidth: 0);
            selector.ToolTip = $"{provider.Name} display mode";
            if (_providerModeSelector is null)
            {
                _providerModeSelector = selector;
            }

            var targets = ChatTargetResolver.OptionsFor(provider.Id);
            var selectedTarget = _viewModel.Settings.Providers.ChatTargetFor(provider.Id) ?? "";
            var targetSelector = new ComboBox
            {
                ItemsSource = targets.Select(target => target.Label).ToArray(),
                SelectedItem = targets.FirstOrDefault(target => target.Id == selectedTarget)?.Label ?? targets[0].Label,
                MinWidth = 0,
                Padding = new Thickness(5, 3, 5, 3),
                ToolTip = $"{provider.Name} chat target",
            };
            targetSelector.SelectionChanged += (_, _) =>
            {
                if (targetSelector.SelectedItem is string label
                    && targets.FirstOrDefault(target => target.Label == label) is { } target)
                {
                    _viewModel.SetChatTarget(provider.Id, target.Id);
                }
            };
            var row = ProviderRowGrid();
            var identity = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            identity.Children.Add(new LogoIcon { Key = provider.LogoKey, Width = 14, Height = 14, Margin = new Thickness(1, 0, 8, 0) });
            var name = new TextBlock { Text = provider.Name, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
            name.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
            identity.Children.Add(name);
            Grid.SetColumn(identity, 0);
            Grid.SetColumn(selector, 1);
            Grid.SetColumn(targetSelector, 3);
            row.Children.Add(identity);
            row.Children.Add(selector);
            row.Children.Add(targetSelector);
            panel.Children.Add(row);
            if (index < providers.Count - 1)
            {
                panel.Children.Add(PreferenceDivider());
            }
        }

        return Scroll(panel);
    }

    private UIElement BehaviorTab()
    {
        var settings = _viewModel.Settings.Behavior;
        var panel = TabPanel();
        panel.Children.Add(SectionHeading("Behavior", "Notifications, shortcut and usage limits."));

        var notificationGroup = PreferenceGroup("Notifications");
        var notifications = new CheckBox { Content = "Notifications", IsChecked = settings.NotificationsEnabled, Margin = new Thickness(0, 0, 0, 4), ToolTip = "Show agent-event and budget-alert notifications." };
        notifications.Checked += (_, _) => _viewModel.SetNotificationsEnabled(true);
        notifications.Unchecked += (_, _) => _viewModel.SetNotificationsEnabled(false);
        notificationGroup.Children.Add(notifications);
        notificationGroup.Children.Add(MutedText("Controls both agent-event and budget-alert notifications."));
        panel.Children.Add(notificationGroup);
        panel.Children.Add(PreferenceDivider());

        var gesture = new TextBox { Text = settings.HotkeyGesture, Width = 180, MaxLength = 64 };
        var hotkeyError = ErrorText();
        var hotkey = new CheckBox { Content = "Enable global shortcut", IsChecked = settings.HotkeyEnabled, Margin = new Thickness(0, 0, 0, 4) };
        void SaveHotkey(bool enabled)
        {
            try
            {
                _viewModel.SetHotkey(enabled, gesture.Text);
                hotkeyError.Text = "";
            }
            catch (ArgumentException error)
            {
                hotkeyError.Text = error.Message;
            }
        }

        hotkey.Checked += (_, _) => SaveHotkey(true);
        hotkey.Unchecked += (_, _) => SaveHotkey(false);
        var hotkeyGroup = PreferenceGroup("Keyboard shortcut");
        hotkeyGroup.Children.Add(hotkey);
        gesture.LostFocus += (_, _) => SaveHotkey(hotkey.IsChecked == true);
        hotkeyGroup.Children.Add(FormRow("Shortcut", gesture));
        hotkeyGroup.Children.Add(hotkeyError);
        _hotkeyStatus = ErrorText();
        _hotkeyStatus.SetResourceReference(TextBlock.ForegroundProperty, "WarningText");
        hotkeyGroup.Children.Add(_hotkeyStatus);
        panel.Children.Add(hotkeyGroup);
        panel.Children.Add(PreferenceDivider());

        var budget = settings.Budget;
        var enabled = new CheckBox { Content = "Budget alerts", IsChecked = budget.Enabled, Margin = new Thickness(0, 0, 0, 4) };
        enabled.Checked += (_, _) => _viewModel.SetBudgetEnabled(true);
        enabled.Unchecked += (_, _) => _viewModel.SetBudgetEnabled(false);
        var budgetGroup = PreferenceGroup("Spend and token budgets");
        budgetGroup.Children.Add(enabled);
        budgetGroup.Children.Add(FormRow("Period", Combo(Enum.GetValues<BudgetPeriod>(), budget.Period, value => _viewModel.SetBudgetPeriod(value))));
        var budgetNote = new TextBlock
        {
            Text = "Blank limits are off. Spend alerts use known model prices; unpriced usage can make the total a lower bound.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 8),
            FontSize = 11,
        };
        budgetNote.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        budgetGroup.Children.Add(budgetNote);
        budgetGroup.Children.Add(BudgetHeader());
        budgetGroup.Children.Add(BudgetEditor("Total", null, budget.For(null)));
        foreach (var provider in ProviderCatalog.All)
        {
            budgetGroup.Children.Add(BudgetEditor(provider.Name, provider.Id, budget.For(provider.Id)));
        }

        panel.Children.Add(budgetGroup);
        return Scroll(panel);
    }

    private UIElement BudgetEditor(string label, Tool? tool, BudgetThreshold threshold)
    {
        var spend = new TextBox
        {
            Text = threshold.Spend?.ToString("0.##", CultureInfo.CurrentCulture) ?? "",
            MinWidth = 72,
            MinHeight = 26,
            Padding = new Thickness(7, 2, 7, 2),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ToolTip = "Spend limit",
        };
        var tokens = new TextBox
        {
            Text = threshold.Tokens?.ToString(CultureInfo.CurrentCulture) ?? "",
            MinWidth = 72,
            MinHeight = 26,
            Padding = new Thickness(7, 2, 7, 2),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ToolTip = "Token limit",
        };
        var error = ErrorText();
        void Save()
        {
            decimal? spendLimit = null;
            long? tokenLimit = null;
            if (spend.Text.Length > 0)
            {
                if (!decimal.TryParse(spend.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var value))
                {
                    error.Text = "Enter a positive spend amount or leave it blank.";
                    return;
                }

                spendLimit = value;
            }

            if (tokens.Text.Length > 0)
            {
                if (!long.TryParse(tokens.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var value))
                {
                    error.Text = "Enter a positive token count or leave it blank.";
                    return;
                }

                tokenLimit = value;
            }

            try
            {
                _viewModel.SetBudgetThreshold(tool, new BudgetThreshold(spendLimit, tokenLimit));
                error.Text = "";
            }
            catch (ArgumentOutOfRangeException exception)
            {
                error.Text = exception.Message;
            }
        }

        spend.LostFocus += (_, _) => Save();
        tokens.LostFocus += (_, _) => Save();
        var row = BudgetGridRow();
        var name = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 12, 0) };
        name.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
        Grid.SetColumn(name, 0);
        Grid.SetColumn(spend, 1);
        Grid.SetColumn(tokens, 3);
        row.Children.Add(name);
        row.Children.Add(spend);
        row.Children.Add(tokens);

        var content = new StackPanel();
        content.Children.Add(row);
        content.Children.Add(error);
        return content;
    }

    private static UIElement BudgetHeader()
    {
        var header = BudgetGridRow();
        AddGridText(header, "Provider", 0, muted: true);
        AddGridText(header, "Spend", 1, muted: true);
        AddGridText(header, "Tokens", 3, muted: true);
        return header;
    }

    private static Grid BudgetGridRow()
    {
        var row = new Grid { Margin = new Thickness(0, 1, 0, 1), MinHeight = 26 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        return row;
    }

    private static StackPanel PreferenceGroup(string title)
    {
        var group = new StackPanel { Margin = new Thickness(2, 0, 2, 4) };
        group.Children.Add(GroupHeading(title));
        return group;
    }

    private static Border PreferenceDivider()
    {
        var divider = new Border { Height = 1, Margin = new Thickness(2, 2, 2, 10) };
        divider.SetResourceReference(Border.BackgroundProperty, "Line");
        return divider;
    }

    private void OnSettingsChanged(object? sender, TrackSettings settings) => SettingsChanged?.Invoke(this, settings);

    private static StackPanel TabPanel() => new() { Margin = new Thickness(2, 0, 2, 0), Orientation = Orientation.Vertical };

    private static ScrollViewer Scroll(UIElement content) => new()
    {
        Content = content,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        MaxHeight = Math.Max(220, SystemParameters.WorkArea.Height - 250),
    };

    private static UIElement SectionHeading(string title, string description)
    {
        var panel = new StackPanel { Margin = new Thickness(2, 0, 2, 8) };
        var heading = new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold };
        heading.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
        var note = new TextBlock { Text = description, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) };
        note.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        panel.Children.Add(heading);
        panel.Children.Add(note);
        return panel;
    }

    private static TextBlock GroupHeading(string text)
    {
        var heading = new TextBlock { Text = text, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) };
        heading.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
        return heading;
    }

    private static Grid FormRow(string label, UIElement control)
    {
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(135) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        text.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
        Grid.SetColumn(control, 1);
        grid.Children.Add(text);
        grid.Children.Add(control);
        return grid;
    }

    private static void AddGridText(Grid grid, string text, int column, bool muted = false)
    {
        var label = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, FontSize = 11, FontWeight = FontWeights.SemiBold };
        label.SetResourceReference(TextBlock.ForegroundProperty, muted ? "Muted" : "Fg");
        Grid.SetColumn(label, column);
        grid.Children.Add(label);
    }

    private static TextBlock MutedText(string text)
    {
        var label = new TextBlock { Text = text, Margin = new Thickness(5, 5, 12, 0), FontSize = 11 };
        label.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        return label;
    }

    private static Grid ProviderRowGrid()
    {
        var row = new Grid { Margin = new Thickness(2, 2, 2, 2), MinHeight = 30 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.35, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.85, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.7, GridUnitType.Star) });
        return row;
    }

    private static ComboBox Combo<T>(IEnumerable<T> values, T selected, Action<T> changed, double minWidth = 170)
    {
        var combo = new ComboBox { ItemsSource = values, SelectedItem = selected, MinWidth = minWidth, Padding = new Thickness(5, 3, 5, 3) };
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is T value)
            {
                changed(value);
            }
        };
        return combo;
    }

    private static TextBlock ErrorText()
    {
        var text = new TextBlock { FontSize = 10.5, TextWrapping = TextWrapping.Wrap };
        text.SetResourceReference(TextBlock.ForegroundProperty, "ErrorText");
        return text;
    }

    private static void SaveElement(FrameworkElement element, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var scale = VisualTreeHelper.GetDpi(element).DpiScaleX;
        var width = Math.Max(1, (int)Math.Ceiling(element.ActualWidth * scale));
        var height = Math.Max(1, (int)Math.Ceiling(element.ActualHeight * scale));
        var bitmap = new RenderTargetBitmap(width, height, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path);
        encoder.Save(output);
    }
}
