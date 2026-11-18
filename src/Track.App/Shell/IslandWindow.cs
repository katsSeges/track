using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Track.Core;
using Track.Core.Display;
using Track.Core.Island;
using Track.Core.Tray;
using Track.Core.Usage;

namespace Track.App.Shell;

/// <summary>
/// The black island at the top center of the screen (mockup round 4). It draws an <see cref="IslandContent"/>;
/// all decisions (when it shows, what it says) come from the tested IslandStateMachine and IslandViewModel.
/// Compact, it never takes focus and never appears in Alt+Tab. Opened, it takes focus so Escape or a
/// click elsewhere closes it. It is only as big as the pill, so clicks beside it reach the app below.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "WPF view; content and behavior are tested in Track.Core.Island.")]
public sealed class IslandWindow : Window
{
    private static readonly Brush Black = Frozen(Color.FromRgb(0, 0, 0));
    private static readonly Brush White = Frozen(Color.FromRgb(0xF5, 0xF5, 0xF7));
    private static readonly Brush Dim = Frozen(Color.FromRgb(0x9A, 0x9A, 0xA1));
    private static readonly Brush Rule = Frozen(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
    private static readonly Brush Outline = Frozen(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
    private static readonly Brush Done = Frozen(Color.FromRgb(0x3C, 0xCF, 0x62));
    private static readonly Brush Waiting = Frozen(Color.FromRgb(0xF5, 0xB7, 0x40));
    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas");
    private static readonly FontFamily Sans = new("Segoe UI Variable Text, Segoe UI");

    private readonly StackPanel _root = new() { Orientation = Orientation.Horizontal };
    private readonly TranslateTransform _slide = new();
    private readonly List<(TextBlock Task, TextBlock Session)> _rowTimers = [];
    private readonly Dictionary<string, FrameworkElement> _named = [];
    private string? _shownKey;
    private TextBlock? _timer;
    private bool _shown;
    private bool _wasOpen;

    public IslandWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        FontSize = 12.5;
        UseLayoutRounding = true;
        // The island is always black: the chart and usage bars look their colors up in the dark theme.
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Track;component/Themes/Dark.xaml") });
        Content = new Border { Padding = new Thickness(10, 0, 10, 14), Child = _root, RenderTransform = _slide };
        SourceInitialized += (_, _) => Native.MakeToolWindow(this, noActivate: true);
        MouseLeftButtonUp += (_, _) => Clicked?.Invoke();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                EscapePressed?.Invoke();
            }
        };
        Deactivated += (_, _) => EscapePressed?.Invoke();
    }

    public event Action? Clicked;

    public event Action? EscapePressed;

    /// <summary>The minimize (–) button in the opened island.</summary>
    public event Action? MinimizeClicked;

    /// <summary>The Now / Usage tab buttons in the opened island (round 5).</summary>
    public event Action<IslandTab>? TabSelected;

    /// <summary>Today / Week / Month on the Usage tab.</summary>
    public event Action<Period>? PeriodSelected;

    /// <summary>A click (or Enter) on a bar of the Usage tab's chart.</summary>
    public event Action<int>? BarSelected;

    /// <summary>The screen area (device-independent pixels) to center the island in.</summary>
    public Rect Screen { get; set; } = SystemParameters.WorkArea;

    /// <summary>Shows, updates or hides the island for this content. Cheap when nothing changed.</summary>
    public void Render(IslandContent content)
    {
        if (content.State == IslandState.Hidden)
        {
            SlideOut();
            return;
        }

        // Rebuild only when something other than the ticking timers changed; otherwise update the timer
        // texts in place, so the waveform keeps running and nothing is reallocated every second.
        var key = ContentKey(content);
        if (key == _shownKey)
        {
            UpdateTimers(content);
        }
        else
        {
            _shownKey = key;
            _timer = null;
            _rowTimers.Clear();
            _named.Clear();
            _root.Children.Clear();
            if (content.State == IslandState.Open)
            {
                _root.Children.Add(Opened(content));
            }
            else
            {
                _root.Children.Add(Compact(content));
                if (content.Bubble.Count > 0)
                {
                    _root.Children.Add(Bubble(content.Bubble));
                }
            }
        }

        var opened = content.State == IslandState.Open;
        Native.SetNoActivate(this, !opened);
        SlideIn();
        // WPF's IsActive can say true while Windows has another app in front, so decide on the switch to
        // "opened" and let BringToFront ask Windows. Only on that switch: never pull focus back afterwards.
        var justOpened = opened && !_wasOpen;
        _wasOpen = opened;
        if (justOpened)
        {
            // The click went to a no-activate window, so a plain Activate() is refused by Windows' focus rules
            // and Escape never arrived (found in the real-input check, 2026-11-18). Bring it to the front explicitly.
            Native.BringToFront(this);
            Activate();
            Keyboard.Focus(this);
        }
    }

    /// <summary>Verification (diag.txt): screen pixels of the opened island's buttons and chart, e.g. "Usage=900,60,180x24".</summary>
    public string ScreenRects() => string.Join(" | ", _named
        .Where(p => p.Value.IsVisible && PresentationSource.FromVisual(p.Value) is not null)
        .Select(p =>
        {
            var a = p.Value.PointToScreen(new Point(0, 0));
            var b = p.Value.PointToScreen(new Point(p.Value.ActualWidth, p.Value.ActualHeight));
            return $"{p.Key}={a.X:0},{a.Y:0},{b.X - a.X:0}x{b.Y - a.Y:0}";
        }));

    /// <summary>Renders the content to a PNG (verification), on a dark-blue "desktop" so the pill is visible.</summary>
    public byte[] Snapshot(IslandContent content)
    {
        _shownKey = null;
        Render(content);
        UpdateLayout();
        var child = (FrameworkElement)Content;
        var width = (int)Math.Ceiling(child.ActualWidth);
        var height = (int)Math.Ceiling(child.ActualHeight);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Frozen(Color.FromRgb(0x5E, 0x75, 0x93)), null, new Rect(0, 0, width, height));
            dc.DrawRectangle(new VisualBrush(child), null, new Rect(0, 0, width, height));
        }

        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var stream = new System.IO.MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    /// <summary>Everything that needs a rebuild: all content except the running timers.</summary>
    private static string ContentKey(IslandContent c) =>
        $"{c.State}|{c.NeedsYou}|{c.CanMinimize}|{c.MainTool}|{(c.State == IslandState.Active ? "" : c.MainText)}|{c.MainExtra}|{string.Join(",", c.Logos)}|{string.Join(",", c.Bubble)}|{c.Counter}|{c.Header}|{c.HeaderRight}|{c.Footer}|{c.Activity}|" +
        string.Join(";", c.Rows.Select(r => $"{r.Tool}{r.Detail}")) + string.Join(";", c.Table) + $"|{c.Tab}|" + UsageKey(c.Usage);

    private static string UsageKey(UsagePanelContent? u) => u is null
        ? ""
        : $"{u.Period}|{u.Label}|{u.ComparisonText}|{u.Chart?.TopText}|" +
          string.Join(";", u.Rows.Append(u.TotalRow).OfType<ToolRowViewModel>().Select(r => $"{r.Name}={r.TokenText}={r.SubText}")) + "|" +
          string.Join(";", u.Chart?.Bars.Select(b => $"{(b.Selected ? "*" : "")}{b.Tooltip}") ?? []);

    private void UpdateTimers(IslandContent c)
    {
        if (_timer is not null)
        {
            _timer.Text = c.MainText;
        }

        for (var i = 0; i < Math.Min(_rowTimers.Count, c.Rows.Count); i++)
        {
            _rowTimers[i].Task.Text = c.Rows[i].TaskTimer;
            _rowTimers[i].Session.Text = c.Rows[i].SessionTimer;
        }
    }

    private void SlideIn()
    {
        if (!IsVisible)
        {
            Show();
        }

        UpdateLayout();
        Left = Screen.Left + ((Screen.Width - ActualWidth) / 2);
        Top = Screen.Top;
        if (!_shown)
        {
            _shown = true;
            Animate(-60, 0, new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut }, null);
        }
    }

    private void SlideOut()
    {
        if (!_shown)
        {
            return;
        }

        _shown = false;
        _shownKey = null;
        Animate(_slide.Y, -60, new QuadraticEase { EasingMode = EasingMode.EaseIn }, () =>
        {
            if (!_shown)
            {
                // Close rather than hide: a hidden WPF window keeps its render resources (~35 MB measured).
                // The controller creates a new one the next time the island shows.
                Close();
            }
        });
    }

    private void Animate(double from, double to, IEasingFunction ease, Action? done)
    {
        if (!SystemParameters.ClientAreaAnimation)
        {
            _slide.Y = to; // reduced motion
            done?.Invoke();
            return;
        }

        var animation = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(380)) { EasingFunction = ease };
        if (done is not null)
        {
            animation.Completed += (_, _) => done();
        }

        _slide.BeginAnimation(TranslateTransform.YProperty, animation);
    }

    private static Border Pill(UIElement child, bool open) => new()
    {
        Background = Black,
        CornerRadius = new CornerRadius(open ? 26 : 19),
        MinHeight = 36,
        Padding = open ? new Thickness(16, 14, 16, 14) : new Thickness(10, 0, 14, 0),
        Margin = new Thickness(0, 6, 0, 0),
        Child = child,
        // A hairline instead of a blurred shadow: a blur effect is redrawn on every waveform frame (measured +0.8 % CPU, +55 MB).
        BorderBrush = Outline,
        BorderThickness = new Thickness(1),
    };

    private UIElement Compact(IslandContent c)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var (tool, i) in c.Logos.Select((t, i) => (t, i)))
        {
            row.Children.Add(Logo(tool, 16, i == 0 ? 0 : -4));
        }

        if (c.State == IslandState.Active && c.MainTool is { } main)
        {
            row.Children.Add(Activity(c.Activity, main));
        }

        if (c.State == IslandState.Done)
        {
            row.Children.Add(Text(c.MainText, White, margin: 9));
            row.Children.Add(c.NeedsYou ? Text("?", Waiting, bold: true, margin: 6) : Text("✓", Done, margin: 6));
            row.Children.Add(Text(c.MainExtra, Dim, mono: true, margin: 6));
        }
        else if (c.State == IslandState.Active)
        {
            _timer = Text(c.MainText, White, mono: true, margin: 9);
            row.Children.Add(_timer);
            row.Children.Add(Text(c.MainExtra, Dim, mono: true, margin: 7));
            row.Children.Add(Text(c.Counter, Dim, margin: 7));
        }
        else
        {
            row.Children.Add(Text(c.MainText, Dim, margin: 2));
            row.Children.Add(Text(c.MainExtra, White, mono: true, margin: 8));
        }

        return Pill(row, open: false);
    }

    private static UIElement Bubble(IReadOnlyList<Tool> tools)
    {
        var logos = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        foreach (var tool in tools)
        {
            logos.Children.Add(Logo(tool, tools.Count > 1 ? 12 : 16, 0));
        }

        return new Border
        {
            Background = Black,
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(18),
            Margin = new Thickness(6, 6, 0, 0),
            Child = logos,
            // A hairline instead of a blurred shadow: a blur effect is redrawn on every waveform frame (measured +0.8 % CPU, +55 MB).
        BorderBrush = Outline,
        BorderThickness = new Thickness(1),
        };
    }

    private UIElement Opened(IslandContent c)
    {
        var panel = new StackPanel { Width = 380 };
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        if (c.CanMinimize)
        {
            // Tucks the island away while agents keep running; the top edge brings it back.
            var minimize = new Button
            {
                Content = new Rectangle { Width = 10, Height = 2, Fill = White, RadiusX = 1, RadiusY = 1 },
                Width = 22,
                Height = 22,
                Margin = new Thickness(10, 0, 0, 0),
                ToolTip = "Minimize until you need it (move the mouse to the top edge to see it again)",
                Cursor = Cursors.Hand,
                Template = MinimizeTemplate(),
            };
            minimize.Click += (_, _) => MinimizeClicked?.Invoke();
            _named["minimize"] = minimize;
            DockPanel.SetDock(minimize, Dock.Right);
            header.Children.Add(minimize);
        }

        var right = Text(c.HeaderRight, Dim, mono: true, size: 11);
        DockPanel.SetDock(right, Dock.Right);
        header.Children.Add(right);
        header.Children.Add(Text(c.Header, White, bold: true));
        panel.Children.Add(header);
        panel.Children.Add(Segments(
            [("Now", c.Tab == IslandTab.Now, () => TabSelected?.Invoke(IslandTab.Now)), ("Usage", c.Tab == IslandTab.Usage, () => TabSelected?.Invoke(IslandTab.Usage))],
            new Thickness(0, 0, 0, 10)));

        if (c.Tab == IslandTab.Usage && c.Usage is { } usage)
        {
            AddUsage(panel, usage);
            return Pill(panel, open: true);
        }

        foreach (var r in c.Rows)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var logo = Logo(r.Tool, 20, 0);
            var name = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
            var title = new StackPanel { Orientation = Orientation.Horizontal };
            title.Children.Add(Text(r.Name, White));
            title.Children.Add(Activity(IslandActivity.Wave, r.Tool));
            name.Children.Add(title);
            name.Children.Add(Text(r.Detail, Dim, mono: true, size: 10.5));
            var timers = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
            var task = new TextBlock { Text = r.TaskTimer, Foreground = White, FontFamily = Mono, FontSize = 15, HorizontalAlignment = HorizontalAlignment.Right };
            var session = Text(r.SessionTimer, Dim, mono: true, size: 10.5);
            _rowTimers.Add((task, session));
            timers.Children.Add(task);
            timers.Children.Add(session);
            Grid.SetColumn(name, 1);
            Grid.SetColumn(timers, 2);
            grid.Children.Add(logo);
            grid.Children.Add(name);
            grid.Children.Add(timers);
            panel.Children.Add(new Border { BorderBrush = Rule, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 8, 0, 8), Child = grid });
        }

        if (c.Rows.Count == 0)
        {
            panel.Children.Add(new Border
            {
                BorderBrush = Rule,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(0, 8, 0, 8),
                Child = Text("Start Claude Code, Codex or Copilot and it shows up here.", Dim, size: 11.5),
            });
        }

        foreach (var t in c.Table)
        {
            panel.Children.Add(new Border
            {
                BorderBrush = Rule,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(0, 5, 0, 5),
                Child = Text($"{t.Name}   today {t.Today} · week {t.Week} · month {t.Month}", Dim, mono: true, size: 11),
            });
        }

        if (c.Footer is not null)
        {
            panel.Children.Add(new Border { BorderBrush = Rule, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 8, 0, 0), Child = Text(c.Footer, Dim, mono: true, size: 11) });
        }

        return Pill(panel, open: true);
    }

    /// <summary>The Usage tab (round 5): period tabs, chart, comparison, then the same rows and total as the tray popover.</summary>
    private void AddUsage(StackPanel panel, UsagePanelContent u)
    {
        panel.Children.Add(Segments(
            [.. new[] { (Period.Today, "Today"), (Period.Week, "Week"), (Period.Month, "Month") }
                .Select(p => (p.Item2, u.Period == p.Item1, (Action)(() => PeriodSelected?.Invoke(p.Item1))))],
            new Thickness(0, 0, 0, 10)));

        if (u.Chart is { } chart)
        {
            var title = new DockPanel { Margin = new Thickness(0, 0, 0, 3) };
            var top = Text(chart.TopText, Dim, mono: true, size: 10.5);
            DockPanel.SetDock(top, Dock.Right);
            title.Children.Add(top);
            title.Children.Add(Text(chart.Title, Dim, mono: true, size: 10.5));
            panel.Children.Add(title);
            var view = new UsageChart { Chart = chart, BarsHeight = 64, SelectCommand = new RelayCommand(i => BarSelected?.Invoke((int)i!)) };
            _named["chart"] = view;
            panel.Children.Add(view);
        }

        if (u.ComparisonText is { } compare)
        {
            panel.Children.Add(new Border { Margin = new Thickness(0, 4, 0, 0), Child = Text(compare, Dim, mono: true, size: 10.5) });
        }

        var label = new DockPanel { Margin = new Thickness(0, 10, 0, 4) };
        var tokens = Text("Tokens", Dim, size: 11);
        DockPanel.SetDock(tokens, Dock.Right);
        label.Children.Add(tokens);
        label.Children.Add(Text(u.Label, Dim, size: 11));
        panel.Children.Add(label);

        foreach (var row in u.Rows.Append(u.TotalRow).OfType<ToolRowViewModel>())
        {
            var total = row.Tool is null;
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var name = new StackPanel { Orientation = Orientation.Horizontal };
            if (row.Tool is { } tool)
            {
                name.Children.Add(Logo(tool, 14, 0));
            }

            name.Children.Add(Text(row.Name, White, margin: total ? 0 : 7, bold: total));
            var big = new TextBlock { Text = row.TokenText, Foreground = White, FontFamily = Mono, FontSize = 13, FontWeight = total ? FontWeights.Bold : FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right };
            var sub = Text(row.SubText, Dim, mono: true, size: 10.5);
            sub.HorizontalAlignment = HorizontalAlignment.Right;
            sub.Margin = new Thickness(0, 2, 0, 0);
            Grid.SetColumn(big, 1);
            Grid.SetRow(sub, 1);
            Grid.SetColumnSpan(sub, 2);
            grid.Children.Add(name);
            grid.Children.Add(big);
            grid.Children.Add(sub);
            if (!total)
            {
                var fill = new BarFill { Fraction = row.BarFraction, Key = row.LogoKey, Height = 3, Margin = new Thickness(0, 5, 0, 0) };
                Grid.SetRow(fill, 2);
                Grid.SetColumnSpan(fill, 2);
                grid.Children.Add(fill);
            }

            panel.Children.Add(new Border { BorderBrush = Rule, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 6, 0, 6), Child = grid });
        }
    }

    /// <summary>A row of tab buttons (Now | Usage, Today | Week | Month). Clicks on them are not clicks on the island.</summary>
    private Border Segments(IReadOnlyList<(string Text, bool Selected, Action Click)> items, Thickness margin)
    {
        var grid = new UniformGrid { Rows = 1, Columns = items.Count };
        foreach (var (text, selected, click) in items)
        {
            var button = new Button
            {
                Content = new TextBlock { Text = text, Foreground = selected ? White : Dim, FontSize = 11.5, FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal },
                Height = 24,
                Cursor = Cursors.Hand,
                Template = SegmentTemplate(selected),
            };
            AutomationProperties.SetName(button, text);
            button.Click += (_, _) => click();
            _named[text] = button;
            grid.Children.Add(button);
        }

        return new Border { Background = Frozen(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)), CornerRadius = new CornerRadius(8), Padding = new Thickness(2), Margin = margin, Child = grid };
    }

    private static ControlTemplate SegmentTemplate(bool selected)
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border), "Seg");
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        border.SetValue(Border.BackgroundProperty, selected ? Frozen(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)) : Brushes.Transparent);
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        template.VisualTree = border;
        if (!selected)
        {
            var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, Frozen(Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF)), "Seg"));
            template.Triggers.Add(hover);
        }

        var focus = new Trigger { Property = IsKeyboardFocusedProperty, Value = true };
        focus.Setters.Add(new Setter(Border.BorderBrushProperty, Frozen(Color.FromRgb(0x4C, 0x9B, 0xFF)), "Seg"));
        template.Triggers.Add(focus);
        return template;
    }

    /// <summary>A round, subtle button: grey disc on hover, accent ring on keyboard focus.</summary>
    private static ControlTemplate MinimizeTemplate()
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border), "Disc");
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(11));
        border.SetValue(Border.BackgroundProperty, Frozen(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        template.VisualTree = border;
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, Frozen(Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF)), "Disc"));
        template.Triggers.Add(hover);
        return template;
    }

    private static TextBlock Text(string? text, Brush brush, bool mono = false, double margin = 0, double size = 12.5, bool bold = false) => new()
    {
        Text = text ?? "",
        Foreground = brush,
        FontFamily = mono ? Mono : Sans,
        FontSize = size,
        FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(text is null ? 0 : margin, 0, 0, 0),
        Visibility = text is null ? Visibility.Collapsed : Visibility.Visible,
    };

    private static FrameworkElement Logo(Tool tool, double size, double overlap)
    {
        var path = new Path
        {
            Data = Logos.For(tool),
            Fill = Frozen((Color)ColorConverter.ConvertFromString(TrayIconModel.BrandColor(tool, TaskbarTheme.Dark))),
            Stretch = Stretch.None,
            RenderTransform = new ScaleTransform(size / 16, size / 16),
        };
        return new Canvas { Width = size, Height = size, Margin = new Thickness(overlap, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Children = { path } };
    }

    /// <summary>Waveform bars or a pulsing dot in the tool's color, like a phone's music indicator.</summary>
    private static FrameworkElement Activity(IslandActivity style, Tool tool)
    {
        var color = Frozen((Color)ColorConverter.ConvertFromString(TrayIconModel.BrandColor(tool, TaskbarTheme.Dark)));
        var animate = SystemParameters.ClientAreaAnimation;
        if (style == IslandActivity.Dot)
        {
            var dot = new Ellipse { Width = 8, Height = 8, Fill = color, Margin = new Thickness(9, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            if (animate)
            {
                var pulse = new DoubleAnimation(1, 0.35, TimeSpan.FromMilliseconds(800)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
                Timeline.SetDesiredFrameRate(pulse, 20);
                dot.BeginAnimation(OpacityProperty, pulse);
            }

            return dot;
        }

        var bars = new StackPanel { Orientation = Orientation.Horizontal, Height = 14, Margin = new Thickness(9, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = style == IslandActivity.None ? Visibility.Collapsed : Visibility.Visible };
        foreach (var delay in new[] { 0.0, -0.25, -0.5, -0.7 })
        {
            var bar = new Rectangle { Width = 3, Height = 6, RadiusX = 1.5, RadiusY = 1.5, Fill = color, Margin = new Thickness(0, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center };
            if (animate)
            {
                var wave = new DoubleAnimation(4, 14, TimeSpan.FromMilliseconds(450))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    BeginTime = TimeSpan.FromSeconds(delay),
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                };
                Timeline.SetDesiredFrameRate(wave, 20); // a phone-like wiggle does not need 60 fps
                bar.BeginAnimation(HeightProperty, wave);
            }

            bars.Children.Add(bar);
        }

        return bars;
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
