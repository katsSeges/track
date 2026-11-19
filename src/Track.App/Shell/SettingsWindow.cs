using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Track.Core;
using Track.Core.Display;
using Track.Core.Live;
using Track.Core.Providers;
using Track.Core.Settings;
using TrackTheme = Track.Core.Settings.ThemeMode;

namespace Track.App.Shell;

/// <summary>
/// Settings in the tray's own style (design canvas boards "Settings · General / Providers / Alerts"): one segmented
/// control for the tabs like the popover's Today/Week/Month, grouped rows with hairlines, switches, small segmented
/// controls and color wells. Every change applies (and is saved) at once. A tab is built when it is shown, so it
/// always reflects the current settings; nothing runs while the window is closed.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "WPF Settings UI; state, parsing and status texts are tested in Track.Core (SettingsViewModel, SettingsText).")]
internal sealed class SettingsWindow : Window
{
    private static readonly string Version =
        typeof(SettingsWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "";

    private readonly Func<Tool, (LiveStatus? Live, DateTimeOffset? LastUsed)> _providerState;
    private readonly Border _surface = new() { Padding = new Thickness(16, 6, 16, 12) };
    private readonly ContentControl _page = new() { Focusable = false };
    private readonly HashSet<Tool> _expanded = [];
    private readonly RadioButton[] _tabButtons = new RadioButton[3];
    private SettingsViewModel _viewModel;
    private string _hotkeyStatus = "";
    private ColorPopover? _colorPopover;

    public SettingsWindow(TrackSettings settings, Func<Tool, (LiveStatus? Live, DateTimeOffset? LastUsed)> providerState)
    {
        _providerState = providerState;
        _viewModel = new SettingsViewModel(settings);
        _viewModel.SettingsChanged += OnSettingsChanged;
        Title = "Track Settings";
        Width = 480;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.CanMinimize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Brushes.Transparent;
        FontSize = 12;
        SetResourceReference(FontFamilyProperty, "Sans");
        SetResourceReference(ForegroundProperty, "Fg");
        Typography.SetNumeralAlignment(this, FontNumeralAlignment.Tabular);
        Content = _surface;
        SourceInitialized += (_, _) => ApplyBackdrop();
        _surface.Child = Layout();
        ShowTab(_viewModel.SelectedTab);
    }

    public event EventHandler<TrackSettings>? SettingsChanged;

    public bool Dark { get; set; }

    /// <summary>Settings changed elsewhere (the tray menu): show them.</summary>
    public void ApplySettings(TrackSettings settings)
    {
        var tab = _viewModel.SelectedTab;
        _viewModel.SettingsChanged -= OnSettingsChanged;
        _viewModel = new SettingsViewModel(settings) { SelectedTab = tab };
        _viewModel.SettingsChanged += OnSettingsChanged;
        ShowTab(tab);
    }

    public void SelectTab(SettingsTab tab) => ShowTab(tab);

    public void SetHotkeyStatus(string status)
    {
        _hotkeyStatus = status;
        if (_viewModel.SelectedTab == SettingsTab.General)
        {
            ShowTab(SettingsTab.General);
        }
    }

    public void ExpandProvider(Tool tool)
    {
        _expanded.Add(tool);
        ShowTab(SettingsTab.Providers);
    }

    /// <summary>Render mode: draws the color popover of <paramref name="tool"/>'s light color to a PNG.</summary>
    public void SaveColorPopoverForRender(Tool tool, string path)
    {
        var provider = ProviderCatalog.Get(tool);
        var popover = new ColorPopover(this, $"{provider.Name} · Light", ColorsFor(provider).Light, provider.Colors.Light, _ => { });
        var surface = popover.Surface;
        surface.Margin = new Thickness(0); // the margin only makes room for the shadow in a real popup
        surface.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        surface.Arrange(new Rect(surface.DesiredSize));
        surface.UpdateLayout();
        SaveElement(surface, path);
    }

    public void ApplyBackdrop()
    {
        if (!IsLoaded && new System.Windows.Interop.WindowInteropHelper(this).Handle == IntPtr.Zero)
        {
            return;
        }

        var supported = Native.ApplyBackdrop(this, Dark);
        _surface.SetResourceReference(Border.BackgroundProperty, supported ? "PopBg" : "PopSolid");
    }

    private UIElement Layout()
    {
        var tabs = new UniformGrid { Columns = 3 };
        string[] names = ["General", "Providers", "Alerts"];
        for (var i = 0; i < names.Length; i++)
        {
            var tab = (SettingsTab)i;
            var button = new RadioButton { Content = names[i], GroupName = "SettingsTab", Style = Res<Style>("Segment") };
            button.Checked += (_, _) =>
            {
                if (_viewModel.SelectedTab != tab || _page.Content is null)
                {
                    ShowTab(tab);
                }
            };
            _tabButtons[i] = button;
            tabs.Children.Add(button);
        }

        var tabTrack = new Border { CornerRadius = new CornerRadius(7), Padding = new Thickness(2), Margin = new Thickness(0, 0, 0, 14), Child = tabs };
        tabTrack.SetResourceReference(Border.BackgroundProperty, "RowBg");

        var footer = new DockPanel { Margin = new Thickness(4, 14, 4, 0) };
        var version = Footnote($"Track {Version}");
        version.Margin = new Thickness(0);
        DockPanel.SetDock(version, Dock.Right);
        footer.Children.Add(version);
        var applies = Footnote("Changes apply right away.");
        applies.Margin = new Thickness(0);
        footer.Children.Add(applies);

        var root = new DockPanel();
        DockPanel.SetDock(tabTrack, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(tabTrack);
        root.Children.Add(footer);
        root.Children.Add(new ScrollViewer
        {
            Content = _page,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = Math.Max(300, SystemParameters.WorkArea.Height - 220),
            Focusable = false,
        });
        return root;
    }

    private void ShowTab(SettingsTab tab)
    {
        _viewModel.SelectedTab = tab;
        _tabButtons[(int)tab].IsChecked = true;
        _page.Content = tab switch
        {
            SettingsTab.General => GeneralTab(),
            SettingsTab.Providers => ProvidersTab(),
            _ => AlertsTab(),
        };
    }

    // ----- General -----

    private UIElement GeneralTab()
    {
        var settings = _viewModel.Settings;
        var page = new StackPanel();

        page.Children.Add(Section("Appearance",
            Group(Row("Theme", null, Segments(
                [(TrackTheme.System, "System"), (TrackTheme.Light, "Light"), (TrackTheme.Dark, "Dark")],
                settings.Appearance.Theme, _viewModel.SetTheme, 190, "Theme"))),
            "Track's windows use the system blue and the acrylic backdrop, like the popover and the island."));

        page.Children.Add(Section("General", Group(
            Row("Start with Windows", null, Switch(settings.StartWithWindows, _viewModel.SetStartWithWindows, "Start with Windows")),
            Row("Notifications", "When an agent finishes or a budget is reached",
                Switch(settings.Behavior.NotificationsEnabled, _viewModel.SetNotificationsEnabled, "Notifications")))));

        var behavior = settings.Behavior;
        var shortcut = new StackPanel { Orientation = Orientation.Horizontal };
        var field = new Button { Style = Res<Style>("ShortcutField"), Content = KeyChips(behavior.HotkeyGesture), IsEnabled = behavior.HotkeyEnabled };
        AutomationProperties.SetName(field, $"Shortcut {behavior.HotkeyGesture}. Press to record a new one");
        var record = new Button { Content = "Record", Style = Res<Style>("LinkButton"), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, IsEnabled = behavior.HotkeyEnabled };
        var note = Footnote(_hotkeyStatus.Length > 0
            ? _hotkeyStatus
            : "Press Record, then the keys (with Ctrl, Alt or Win). Esc cancels.");
        if (_hotkeyStatus.Length > 0)
        {
            note.SetResourceReference(TextBlock.ForegroundProperty, "WarningText");
        }

        void StartRecording()
        {
            field.Content = Footnote("Press the keys…");
            ((TextBlock)field.Content).Margin = new Thickness(2, 0, 6, 0);
            field.Focus();
            Keyboard.Focus(field);
        }

        field.Click += (_, _) => StartRecording();
        record.Click += (_, _) => StartRecording();
        field.PreviewKeyDown += (_, e) =>
        {
            if (field.Content is not TextBlock)
            {
                return;
            }

            e.Handled = true;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Escape)
            {
                field.Content = KeyChips(_viewModel.Settings.Behavior.HotkeyGesture);
                return;
            }

            if (Gesture(key, Keyboard.Modifiers) is not { } gesture)
            {
                return; // only modifiers so far, or a key Track cannot register
            }

            try
            {
                _viewModel.SetHotkey(true, gesture);
                field.Content = KeyChips(gesture);
                note.Text = "Saved. If another app owns these keys, Track says so here.";
                note.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            }
            catch (ArgumentException error)
            {
                note.Text = error.Message;
                note.SetResourceReference(TextBlock.ForegroundProperty, "ErrorText");
            }
        };
        field.LostKeyboardFocus += (_, _) =>
        {
            if (field.Content is TextBlock)
            {
                field.Content = KeyChips(_viewModel.Settings.Behavior.HotkeyGesture);
            }
        };
        shortcut.Children.Add(field);
        shortcut.Children.Add(record);

        var enabled = Switch(behavior.HotkeyEnabled, on =>
        {
            _viewModel.SetHotkey(on, _viewModel.Settings.Behavior.HotkeyGesture);
            field.IsEnabled = record.IsEnabled = on;
        }, "Use a keyboard shortcut to open the popover");
        page.Children.Add(Section("Keyboard shortcut", Group(
            Row("Open the popover", null, enabled),
            Row("Shortcut", null, shortcut)), null, note));
        return page;
    }

    private static string? Gesture(Key key, ModifierKeys modifiers)
    {
        var name = key switch
        {
            >= Key.A and <= Key.Z => key.ToString(),
            >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(),
            >= Key.F1 and <= Key.F24 => key.ToString(),
            Key.Space => "Space",
            Key.Tab => "Tab",
            Key.Home => "Home",
            Key.End => "End",
            Key.Insert => "Insert",
            Key.Delete => "Delete",
            Key.Up => "Up",
            Key.Down => "Down",
            Key.Left => "Left",
            Key.Right => "Right",
            Key.PageUp => "PageUp",
            Key.PageDown => "PageDown",
            _ => null,
        };
        if (name is null)
        {
            return null;
        }

        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(name);
        var gesture = string.Join("+", parts);
        return HotkeyGestureParser.TryParse(gesture, out _) ? gesture : null;
    }

    private static UIElement KeyChips(string gesture)
    {
        var chips = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var part in gesture.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            chips.Children.Add(new Border
            {
                Style = Res<Style>("KeyChip"),
                Child = new TextBlock { Text = part, FontSize = 11, FontWeight = FontWeights.Medium },
            });
        }

        return chips;
    }

    // ----- Providers -----

    private UIElement ProvidersTab()
    {
        var settings = _viewModel.Settings;
        var rows = new List<UIElement>();
        foreach (var provider in ProviderCatalog.All)
        {
            rows.Add(ProviderRow(provider, settings));
        }

        var reset = new Button { Content = "Reset colors", Style = Res<Style>("LinkButton"), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12, 6, 4, 0) };
        reset.Click += (_, _) =>
        {
            _viewModel.ResetProviderColors();
            ShowTab(SettingsTab.Providers);
        };
        var note = Footnote("Auto shows a provider while it runs or once it has usage in the last 90 days. Off also stops reading its logs.");
        var foot = new DockPanel();
        DockPanel.SetDock(reset, Dock.Right);
        foot.Children.Add(reset);
        foot.Children.Add(note);

        var caption = new DockPanel();
        var modes = Caption("Auto · On · Off");
        modes.FontWeight = FontWeights.Normal;
        DockPanel.SetDock(modes, Dock.Right);
        caption.Children.Add(modes);
        caption.Children.Add(Caption("Show in tray, popover and island"));

        var page = new StackPanel();
        page.Children.Add(caption);
        page.Children.Add(Group([.. rows]));
        page.Children.Add(foot);
        return page;
    }

    private UIElement ProviderRow(ProviderInfo provider, TrackSettings settings)
    {
        var mode = settings.Providers.ModeFor(provider.Id);
        var (live, lastUsed) = _providerState(provider.Id);
        var expanded = _expanded.Contains(provider.Id);

        var name = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        name.Children.Add(new TextBlock { Text = provider.Name, FontWeight = FontWeights.Medium });
        var status = Footnote(SettingsText.ProviderStatus(provider, mode, live, lastUsed, DateTimeOffset.UtcNow));
        status.Margin = new Thickness(0, 1, 0, 0);
        name.Children.Add(status);

        var chevron = new Button
        {
            Style = Res<Style>("HeaderIconButton"),
            Width = 22,
            Height = 22,
            Margin = new Thickness(8, 0, 0, 0),
            Content = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M 3.5 1.5 L 7 5 L 3.5 8.5"),
                Stroke = Res<Brush>("Muted"),
                StrokeThickness = 1.3,
                Width = 10,
                Height = 10,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new RotateTransform(expanded ? 90 : 0),
            },
            ToolTip = expanded ? "Hide colors and chat app" : "Colors and chat app",
        };
        ((System.Windows.Shapes.Path)chevron.Content).SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Muted");
        AutomationProperties.SetName(chevron, $"{provider.Name} details");
        chevron.Click += (_, _) =>
        {
            if (!_expanded.Remove(provider.Id))
            {
                _expanded.Add(provider.Id);
            }

            ShowTab(SettingsTab.Providers);
        };

        var line = new DockPanel { MinHeight = 44 };
        var logo = new LogoIcon { Key = provider.LogoKey, Active = true, Width = 14, Height = 14, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(logo, Dock.Left);
        DockPanel.SetDock(chevron, Dock.Right);
        var segments = Segments(
            [(ProviderMode.Auto, "Auto"), (ProviderMode.On, "On"), (ProviderMode.Off, "Off")],
            mode,
            value =>
            {
                _viewModel.SetProviderMode(provider.Id, value);
                status.Text = SettingsText.ProviderStatus(provider, value, live, lastUsed, DateTimeOffset.UtcNow);
            },
            132,
            $"{provider.Name} display");
        segments.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(segments, Dock.Right);
        line.Children.Add(logo);
        line.Children.Add(chevron);
        line.Children.Add(segments);
        line.Children.Add(name);

        var block = new StackPanel();
        block.Children.Add(line);
        if (expanded)
        {
            block.Children.Add(ProviderDetails(provider, settings));
        }

        return block;
    }

    private UIElement ProviderDetails(ProviderInfo provider, TrackSettings settings)
    {
        var colors = ColorsFor(provider);
        var wells = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        wells.Children.Add(WellLabel("Light"));
        wells.Children.Add(Well(provider, "Light", colors.Light, provider.Colors.Light, value => _viewModel.SetProviderColors(provider.Id, value, ColorsFor(provider).Dark)));
        wells.Children.Add(WellLabel("Dark"));
        wells.Children.Add(Well(provider, "Dark", colors.Dark, provider.Colors.Dark, value => _viewModel.SetProviderColors(provider.Id, ColorsFor(provider).Light, value)));

        var targets = ChatTargetResolver.OptionsFor(provider.Id);
        var selected = settings.Providers.ChatTargetFor(provider.Id) ?? "";
        var chat = new ComboBox
        {
            ItemsSource = targets.Select(t => t.Id.Length == 0 ? "Automatic" : t.Label).ToArray(),
            SelectedIndex = Math.Max(0, targets.ToList().FindIndex(t => t.Id == selected)),
            MinWidth = 150,
            FontSize = 11.5,
        };
        AutomationProperties.SetName(chat, $"Open {provider.Name} chat with");
        chat.SelectionChanged += (_, _) =>
        {
            if (chat.SelectedIndex >= 0)
            {
                _viewModel.SetChatTarget(provider.Id, targets[chat.SelectedIndex].Id);
            }
        };

        var inner = new StackPanel();
        inner.Children.Add(InnerRow("Color", wells));
        inner.Children.Add(InnerRow("Open chat with", chat));
        var box = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(24, 0, 0, 10), Child = inner };
        box.SetResourceReference(Border.BackgroundProperty, "RowBg");
        return box;
    }

    private Button Well(ProviderInfo provider, string theme, string hex, string defaultHex, Action<string> apply)
    {
        var well = new Button { Style = Res<Style>("ColorWell"), Background = ColorPopover.Brush(hex), Margin = new Thickness(0, 0, 12, 0), ToolTip = hex };
        AutomationProperties.SetName(well, $"{provider.Name} {theme.ToLowerInvariant()} theme color {hex}");
        well.Click += (_, _) =>
        {
            _colorPopover?.SetCurrentValue(Popup.IsOpenProperty, false);
            _colorPopover = new ColorPopover(well, $"{provider.Name} · {theme}", hex, defaultHex, value =>
            {
                apply(value);
                well.Background = ColorPopover.Brush(value);
                well.ToolTip = value;
            });
            _colorPopover.IsOpen = true;
        };
        return well;
    }

    private ProviderColorOverride ColorsFor(ProviderInfo provider) =>
        _viewModel.Settings.Appearance.ProviderColors.TryGetValue(provider.Id, out var custom)
            ? custom
            : new ProviderColorOverride(provider.Colors.Light, provider.Colors.Dark);

    // ----- Alerts -----

    private UIElement AlertsTab()
    {
        var settings = _viewModel.Settings;
        var budget = settings.Behavior.Budget;
        var page = new StackPanel();

        page.Children.Add(Section(null, Group(
            Row("Budget alerts", "One notification per limit, once per period", Switch(budget.Enabled, _viewModel.SetBudgetEnabled, "Budget alerts")),
            Row("Period", null, Segments(
                [(BudgetPeriod.Day, "Day"), (BudgetPeriod.Month, "Month")],
                budget.Period,
                value =>
                {
                    _viewModel.SetBudgetPeriod(value);
                    ShowTab(SettingsTab.Alerts);
                },
                132,
                "Period")))));

        var header = LimitGrid();
        AddCell(header, Caption(budget.Period == BudgetPeriod.Day ? "Limits today" : "Limits this month"), 0);
        var spendCaption = Caption("Spend");
        spendCaption.HorizontalAlignment = HorizontalAlignment.Right;
        AddCell(header, spendCaption, 1);
        var tokensCaption = Caption("Tokens");
        tokensCaption.HorizontalAlignment = HorizontalAlignment.Right;
        AddCell(header, tokensCaption, 2);
        header.Margin = new Thickness(0, 0, 12, 0);
        page.Children.Add(header);

        var rows = new List<UIElement> { LimitRow(null, "All providers", null, budget.For(null)) };
        foreach (var provider in ProviderCatalog.All.Where(p => p.Capabilities.HasTokens && ShowsLimit(p, settings)))
        {
            rows.Add(LimitRow(provider.Id, provider.Name, provider.LogoKey, budget.For(provider.Id)));
        }

        page.Children.Add(Group([.. rows]));
        page.Children.Add(Footnote("Tokens accept 120k or 1.5M; leave a field empty for no limit. Spend uses known model prices, so unpriced usage is not counted. Providers turned Off are not listed."));

        if (!settings.Behavior.NotificationsEnabled)
        {
            var turnOn = new Button { Content = "Turn on", Style = Res<Style>("LinkButton"), VerticalAlignment = VerticalAlignment.Center };
            turnOn.Click += (_, _) =>
            {
                _viewModel.SetNotificationsEnabled(true);
                ShowTab(SettingsTab.Alerts);
            };
            var strip = new DockPanel();
            DockPanel.SetDock(turnOn, Dock.Right);
            strip.Children.Add(turnOn);
            strip.Children.Add(new TextBlock { Text = "Notifications are off in General, so alerts are not shown.", FontSize = 11, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
            var warning = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 12, 0, 0), Child = strip };
            warning.SetResourceReference(Border.BackgroundProperty, "WarnSoft");
            page.Children.Add(warning);
        }

        return page;
    }

    private bool ShowsLimit(ProviderInfo provider, TrackSettings settings)
    {
        var mode = settings.Providers.ModeFor(provider.Id);
        if (mode == ProviderMode.Off)
        {
            return false;
        }

        var (live, lastUsed) = _providerState(provider.Id);
        return mode == ProviderMode.On
            || lastUsed is not null
            || live is { Working: true }
            || settings.Behavior.Budget.Providers.ContainsKey(provider.Id);
    }

    private UIElement LimitRow(Tool? tool, string name, string? logoKey, BudgetThreshold threshold)
    {
        var grid = LimitGrid();
        grid.MinHeight = 40;
        var label = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (logoKey is not null)
        {
            label.Children.Add(new LogoIcon { Key = logoKey, Active = true, Width = 12, Height = 12, Margin = new Thickness(0, 0, 8, 0) });
        }

        label.Children.Add(new TextBlock { Text = name, FontWeight = tool is null ? FontWeights.SemiBold : FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center });
        AddCell(grid, label, 0);

        var spend = LimitField(SettingsText.FormatSpend(threshold.Spend), $"{name} spend limit in dollars", "$");
        var tokens = LimitField(SettingsText.FormatTokens(threshold.Tokens), $"{name} token limit", null);
        AddCell(grid, spend.Shell, 1);
        AddCell(grid, tokens.Shell, 2);

        var saved = (Spend: spend.Box.Text, Tokens: tokens.Box.Text);
        void Save()
        {
            if ((spend.Box.Text, tokens.Box.Text) == saved)
            {
                return; // unchanged: "123.5k" shown for 123456 must not overwrite it
            }

            var spendOk = SettingsText.TryParseSpend(spend.Box.Text, out var spendLimit);
            var tokensOk = SettingsText.TryParseTokens(tokens.Box.Text, out var tokenLimit);
            Mark(spend.Shell, spendOk, "Enter dollars, for example 75 or 7.50, or leave it empty.");
            Mark(tokens.Shell, tokensOk, "Enter tokens, for example 120k or 1.5M, or leave it empty.");
            if (spendOk && tokensOk)
            {
                _viewModel.SetBudgetThreshold(tool, new BudgetThreshold(spendLimit, tokenLimit));
                saved = (spend.Box.Text, tokens.Box.Text);
            }
        }

        foreach (var box in new[] { spend.Box, tokens.Box })
        {
            box.LostFocus += (_, _) => Save();
            box.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    Save();
                }
            };
        }

        return grid;
    }

    private static (Border Shell, TextBox Box) LimitField(string text, string name, string? prefix)
    {
        var box = new TextBox
        {
            Text = text,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Padding = new Thickness(0),
            TextAlignment = TextAlignment.Right,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        box.Template = PlainTextBox();
        AutomationProperties.SetName(box, name);
        var hint = new TextBlock { Text = "No limit", FontSize = 11.5, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, Visibility = text.Length == 0 ? Visibility.Visible : Visibility.Collapsed };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        box.TextChanged += (_, _) => hint.Visibility = box.Text.Length == 0 && !box.IsKeyboardFocused ? Visibility.Visible : Visibility.Collapsed;
        box.GotKeyboardFocus += (_, _) => hint.Visibility = Visibility.Collapsed;
        box.LostKeyboardFocus += (_, _) => hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var layered = new Grid();
        layered.Children.Add(hint);
        layered.Children.Add(box);

        var content = new DockPanel();
        if (prefix is not null)
        {
            var sign = new TextBlock { Text = prefix, Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
            sign.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            DockPanel.SetDock(sign, Dock.Left);
            content.Children.Add(sign);
        }

        content.Children.Add(layered);
        var shell = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 3, 8, 3), Child = content, VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.IBeam };
        shell.SetResourceReference(Border.BackgroundProperty, "Field");
        shell.SetResourceReference(Border.BorderBrushProperty, "FieldBorder");
        shell.MouseLeftButtonDown += (_, _) => box.Focus();
        box.GotKeyboardFocus += (_, _) => shell.SetResourceReference(Border.BorderBrushProperty, "Accent");
        box.LostKeyboardFocus += (_, _) =>
        {
            if (shell.Tag is not "error")
            {
                shell.SetResourceReference(Border.BorderBrushProperty, "FieldBorder");
            }
        };
        return (shell, box);
    }

    private static ControlTemplate PlainTextBox()
    {
        var host = new FrameworkElementFactory(typeof(ScrollViewer)) { Name = "PART_ContentHost" };
        return new ControlTemplate(typeof(TextBox)) { VisualTree = host };
    }

    private static void Mark(Border shell, bool ok, string message)
    {
        shell.Tag = ok ? null : "error";
        shell.ToolTip = ok ? null : message;
        shell.SetResourceReference(Border.BorderBrushProperty, ok ? "FieldBorder" : "ErrorText");
    }

    private static Grid LimitGrid()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(104) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(104) });
        return grid;
    }

    private static void AddCell(Grid grid, UIElement element, int column)
    {
        Grid.SetColumn(element, column);
        if (column > 0 && element is FrameworkElement framework)
        {
            framework.Margin = new Thickness(8, framework.Margin.Top, 0, framework.Margin.Bottom);
        }

        grid.Children.Add(element);
    }

    // ----- Shared pieces -----

    private void OnSettingsChanged(object? sender, TrackSettings settings) => SettingsChanged?.Invoke(this, settings);

    private static T Res<T>(string key) => (T)Application.Current.FindResource(key);

    private static StackPanel Section(string? caption, UIElement group, string? footnote = null, TextBlock? note = null)
    {
        var section = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        if (caption is not null)
        {
            section.Children.Add(Caption(caption));
        }

        section.Children.Add(group);
        if (footnote is not null)
        {
            section.Children.Add(Footnote(footnote));
        }

        if (note is not null)
        {
            section.Children.Add(note);
        }

        return section;
    }

    /// <summary>A grouped section: its rows separated by hairlines (none after the last).</summary>
    private static Border Group(params UIElement[] rows)
    {
        var stack = new StackPanel();
        for (var i = 0; i < rows.Length; i++)
        {
            var cell = new Border { Child = rows[i], BorderThickness = new Thickness(0, 0, 0, i < rows.Length - 1 ? 1 : 0) };
            cell.SetResourceReference(Border.BorderBrushProperty, "Line");
            stack.Children.Add(cell);
        }

        return new Border { Style = Res<Style>("Group"), Child = stack };
    }

    private static UIElement Row(string label, string? subtitle, FrameworkElement control)
    {
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = label });
        if (subtitle is not null)
        {
            var sub = Footnote(subtitle);
            sub.Margin = new Thickness(0, 1, 0, 0);
            text.Children.Add(sub);
        }

        control.VerticalAlignment = VerticalAlignment.Center;
        control.Margin = new Thickness(12, 0, 0, 0);
        var row = new DockPanel { MinHeight = subtitle is null ? 40 : 48 };
        DockPanel.SetDock(control, Dock.Right);
        row.Children.Add(control);
        row.Children.Add(text);
        return row;
    }

    private static UIElement InnerRow(string label, FrameworkElement control)
    {
        control.VerticalAlignment = VerticalAlignment.Center;
        var row = new DockPanel { MinHeight = 34 };
        DockPanel.SetDock(control, Dock.Right);
        row.Children.Add(control);
        row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        return row;
    }

    private static TextBlock WellLabel(string text)
    {
        var label = Footnote(text);
        label.Margin = new Thickness(0, 0, 6, 0);
        label.VerticalAlignment = VerticalAlignment.Center;
        return label;
    }

    private static ToggleButton Switch(bool on, Action<bool> changed, string name)
    {
        var toggle = new ToggleButton { Style = Res<Style>("Switch"), IsChecked = on };
        AutomationProperties.SetName(toggle, name);
        toggle.Checked += (_, _) => changed(true);
        toggle.Unchecked += (_, _) => changed(false);
        return toggle;
    }

    private static Border Segments<T>(IReadOnlyList<(T Value, string Label)> options, T selected, Action<T> changed, double width, string name)
    {
        var grid = new UniformGrid { Columns = options.Count };
        var group = Guid.NewGuid().ToString("N");
        foreach (var (value, label) in options)
        {
            var button = new RadioButton
            {
                Content = label,
                GroupName = group,
                Style = Res<Style>("SegmentSmall"),
                IsChecked = EqualityComparer<T>.Default.Equals(value, selected),
            };
            button.Checked += (_, _) => changed(value);
            grid.Children.Add(button);
        }

        var track = new Border { Style = Res<Style>("SegmentTrack"), Width = width, Child = grid };
        AutomationProperties.SetName(track, name);
        return track;
    }

    private static TextBlock Caption(string text) => new() { Text = text, Style = Res<Style>("Caption") };

    private static TextBlock Footnote(string text) => new() { Text = text, Style = Res<Style>("Footnote") };

    private static void SaveElement(FrameworkElement element, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var scale = VisualTreeHelper.GetDpi(element).DpiScaleX;
        var bitmap = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Ceiling(element.ActualWidth * scale)),
            Math.Max(1, (int)Math.Ceiling(element.ActualHeight * scale)),
            96 * scale,
            96 * scale,
            PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path);
        encoder.Save(output);
    }
}
