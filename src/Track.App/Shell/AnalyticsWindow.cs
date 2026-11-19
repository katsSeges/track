using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Track.Core;
using Track.Core.Display;
using Track.Core.Providers;

namespace Track.App.Shell;

/// <summary>
/// Analytics in the tray's own style (design canvas board "Analytics"): the range is a segmented control that
/// applies at once, the numbers sit in one grouped row, a single chart switches between tokens and cost, and the
/// tables and heatmap are grouped sections. Data is built only while the window is open.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "WPF analytics UI; range and aggregate behavior is covered in Track.Core.")]
internal sealed class AnalyticsWindow : Window
{
    private static readonly string[] DayNames = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];

    private readonly AnalyticsBuilder _builder;
    private readonly Func<AnalyticsRange, IEnumerable<Tool>, AnalyticsData> _build;
    private readonly Func<IReadOnlyList<Tool>> _visibleProviders;
    private readonly Border _surface = new() { Padding = new Thickness(20, 6, 20, 16) };
    private readonly Border _rangeTrack = new() { CornerRadius = new CornerRadius(7), Padding = new Thickness(2), Width = 320 };
    private readonly List<RadioButton> _rangeOptions = [];
    private readonly StackPanel _custom = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
    private readonly DatePicker _start = new() { Width = 130, Margin = new Thickness(6, 0, 12, 0) };
    private readonly DatePicker _end = new() { Width = 130, Margin = new Thickness(6, 0, 0, 0) };
    private readonly TextBlock _rangeText = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
    private readonly TextBlock _status = new() { FontSize = 11, Margin = new Thickness(4, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _content = new();
    private readonly AnalyticsChart _chart = new();
    private AnalyticsRange _range;
    private AnalyticsData? _data;
    private bool _dark;
    private int _preset;

    public AnalyticsWindow(
        AnalyticsBuilder builder,
        Func<AnalyticsRange, IEnumerable<Tool>, AnalyticsData> build,
        Func<IReadOnlyList<Tool>> visibleProviders,
        bool dark)
    {
        _builder = builder;
        _build = build;
        _visibleProviders = visibleProviders;
        _dark = dark;
        _range = builder.LastDays(7);
        Title = "Track Analytics";
        Width = 880;
        Height = Math.Min(980, SystemParameters.WorkArea.Height - 40);
        MinWidth = 720;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Brushes.Transparent;
        FontSize = 12;
        SetResourceReference(FontFamilyProperty, "Sans");
        SetResourceReference(ForegroundProperty, "Fg");
        Typography.SetNumeralAlignment(this, FontNumeralAlignment.Tabular);
        Content = _surface;
        SourceInitialized += (_, _) => ApplyBackdrop();
        Closed += (_, _) =>
        {
            // Nothing is kept once the window closes: the data and chart go with it.
            _data = null;
            _chart.Data = null;
        };
        BuildContent();
        RefreshData();
    }

    public void ApplyAppearance(bool dark)
    {
        _dark = dark;
        if (IsLoaded)
        {
            ApplyBackdrop();
            RebuildContent(); // the heatmap's accent shades are computed, so they follow the theme
        }
    }

    public void ApplyBackdrop()
    {
        var supported = Native.ApplyBackdrop(this, _dark);
        _surface.SetResourceReference(Border.BackgroundProperty, supported ? "PopBg" : "PopSolid");
    }

    public void PrepareForRender()
    {
        Height = double.NaN;
        SizeToContent = SizeToContent.Height;
        if (_surface.Child is DockPanel { Children: [.., ScrollViewer scroll] })
        {
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        }
    }

    public void SaveCustomRangeForRender(string windowPath, string calendarPath)
    {
        _rangeOptions[3].IsChecked = true;
        UpdateLayout();
        SaveElement(_surface, windowPath);
        _start.ApplyTemplate();
        _start.IsDropDownOpen = true;
        UpdateLayout();
        if (_start.Template.FindName("PART_Popup", _start) is Popup { Child: FrameworkElement calendar })
        {
            calendar.Measure(new Size(380, 360));
            calendar.Arrange(new Rect(calendar.DesiredSize));
            calendar.UpdateLayout();
            SaveElement(calendar, calendarPath);
        }

        _start.IsDropDownOpen = false;
    }

    public void SaveWindowForRender(string path) => SaveElement(_surface, path);

    public void RefreshData()
    {
        _data = _build(_range, _visibleProviders());
        RebuildContent();
    }

    private void BuildContent()
    {
        var ranges = new UniformGrid { Columns = 4 };
        string[] labels = ["7 days", "30 days", "90 days", "Custom"];
        for (var i = 0; i < labels.Length; i++)
        {
            var preset = i;
            var option = new RadioButton { Content = labels[i], GroupName = "AnalyticsRange", Style = Res<Style>("Segment"), IsChecked = i == _preset };
            option.Checked += (_, _) => SelectPreset(preset);
            _rangeOptions.Add(option);
            ranges.Children.Add(option);
        }

        _rangeTrack.Child = ranges;
        _rangeTrack.SetResourceReference(Border.BackgroundProperty, "RowBg");
        AutomationProperties.SetName(_rangeTrack, "Range");
        _rangeText.SetResourceReference(TextBlock.ForegroundProperty, "Muted");

        var export = new Button { Content = "Export CSV…", Style = Res<Style>("LinkButton"), VerticalAlignment = VerticalAlignment.Center };
        export.Click += (_, _) => ExportCsv();
        var bar = new DockPanel();
        DockPanel.SetDock(export, Dock.Right);
        bar.Children.Add(export);
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        left.Children.Add(_rangeTrack);
        left.Children.Add(_rangeText);
        bar.Children.Add(left);

        _custom.Children.Add(Muted("From"));
        _custom.Children.Add(_start);
        _custom.Children.Add(Muted("To"));
        _custom.Children.Add(_end);
        _custom.Visibility = Visibility.Collapsed;
        _start.SelectedDateChanged += (_, _) => ApplyCustom();
        _end.SelectedDateChanged += (_, _) => ApplyCustom();
        AutomationProperties.SetName(_start, "Start date");
        AutomationProperties.SetName(_end, "End date");
        _status.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        _status.Visibility = Visibility.Collapsed;

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        header.Children.Add(bar);
        header.Children.Add(_custom);
        header.Children.Add(_status);

        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        root.Children.Add(new ScrollViewer
        {
            Content = _content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false,
        });
        _surface.Child = root;
    }

    private void SelectPreset(int preset)
    {
        _preset = preset;
        _custom.Visibility = preset == 3 ? Visibility.Visible : Visibility.Collapsed;
        if (preset == 3)
        {
            _start.SelectedDate ??= _range.StartDate.ToDateTime(TimeOnly.MinValue);
            _end.SelectedDate ??= _range.EndDateInclusive.ToDateTime(TimeOnly.MinValue);
            ApplyCustom();
            return;
        }

        _range = _builder.LastDays(preset switch { 0 => 7, 1 => 30, _ => 90 });
        SetStatus("");
        RefreshData();
    }

    private void ApplyCustom()
    {
        if (_preset != 3 || _start.SelectedDate is not { } start || _end.SelectedDate is not { } end)
        {
            return;
        }

        try
        {
            _range = _builder.CustomRange(DateOnly.FromDateTime(start), DateOnly.FromDateTime(end));
            SetStatus("");
            RefreshData();
        }
        catch (ArgumentException error)
        {
            SetStatus(error.Message);
        }
    }

    private void RebuildContent()
    {
        if (_data is not { } data)
        {
            return;
        }

        _rangeText.Text = $"{data.Range.StartDate.ToString("ddd d MMM", CultureInfo.InvariantCulture)} – {data.Range.EndDateInclusive.ToString("ddd d MMM yyyy", CultureInfo.InvariantCulture)}";
        _content.Children.Clear();
        _content.Children.Add(Stats(data));
        _content.Children.Add(ChartSection(data));

        var tables = new Grid { Margin = new Thickness(0, 0, 0, 16) };
        tables.ColumnDefinitions.Add(new ColumnDefinition());
        tables.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        tables.ColumnDefinitions.Add(new ColumnDefinition());
        var models = Table("Models", "Model", "Cost",
            data.Models.Take(8).Select(m => (m.Tool, m.Model, Format.Tokens(m.Totals.Tokens.Total), Format.Cost(m.Totals))),
            "No models in this range.");
        var sessions = Table("Top sessions", "Session", "Span",
            data.TopSessions.Take(8).Select(s => (s.Tool, s.SessionId, Format.Tokens(s.Usage.Tokens.Total), Span(s.ObservedSpan))),
            "No sessions in this range.");
        Grid.SetColumn(sessions, 2);
        tables.Children.Add(models);
        tables.Children.Add(sessions);
        _content.Children.Add(tables);

        _content.Children.Add(Heatmap(data));
        _content.Children.Add(Footnote("Costs are estimates from known model prices. A session counts in full if any of it falls in the range; span is first to last activity, not time worked. Per-session history before 1.3.0 was kept for 7 days."));
    }

    private UIElement Stats(AnalyticsData data)
    {
        var total = data.Total;
        (string Label, string Value, string Note)[] tiles =
        [
            ("Tokens", Format.Tokens(total.Tokens.Total), $"in {Format.Tokens(total.InTokens)} · out {Format.Tokens(total.OutTokens)}"),
            ("Estimated cost", Format.Cost(total), total.UnpricedTokens > 0 ? "some usage unpriced" : "known prices"),
            ("Cache hits", data.CacheHitRate is { } hit ? hit.ToString("P0", CultureInfo.InvariantCulture) : "—", "of input tokens"),
            ("Sessions", data.SessionCount.ToString(CultureInfo.InvariantCulture), data.Providers.Count == 1 ? "1 provider" : $"across {data.Providers.Count} providers"),
            ("Median span", Span(data.MedianObservedSpan), "first to last activity"),
        ];
        var grid = new UniformGrid { Columns = tiles.Length };
        for (var i = 0; i < tiles.Length; i++)
        {
            var tile = new StackPanel { Margin = new Thickness(14, 12, 14, 12) };
            tile.Children.Add(Muted(tiles[i].Label));
            tile.Children.Add(new TextBlock { Text = tiles[i].Value, FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 3, 0, 2) });
            var note = Muted(tiles[i].Note);
            note.FontSize = 10.5;
            note.TextTrimming = TextTrimming.CharacterEllipsis;
            tile.Children.Add(note);
            var cell = new Border { Child = tile, BorderThickness = new Thickness(i == 0 ? 0 : 1, 0, 0, 0) };
            cell.SetResourceReference(Border.BorderBrushProperty, "Line");
            grid.Children.Add(cell);
        }

        return new Border { Style = Res<Style>("Group"), Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 16), Child = grid };
    }

    private UIElement ChartSection(AnalyticsData data)
    {
        _chart.Data = data;
        var metric = new UniformGrid { Columns = 2 };
        foreach (var (value, label) in new[] { (AnalyticsChartMetric.Tokens, "Tokens"), (AnalyticsChartMetric.Cost, "Cost") })
        {
            var option = new RadioButton { Content = label, GroupName = "AnalyticsMetric", Style = Res<Style>("SegmentSmall"), IsChecked = _chart.Metric == value };
            option.Checked += (_, _) => _chart.Metric = value;
            metric.Children.Add(option);
        }

        var metricTrack = new Border { Style = Res<Style>("SegmentTrack"), Width = 140, Child = metric };
        AutomationProperties.SetName(metricTrack, "Measure");
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(metricTrack, Dock.Right);
        head.Children.Add(metricTrack);
        head.Children.Add(new TextBlock { Text = "Per day", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });

        var legend = new WrapPanel { Margin = new Thickness(44, 8, 0, 0) };
        foreach (var tool in data.Providers)
        {
            var swatch = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center };
            swatch.SetResourceReference(Border.BackgroundProperty, tool.ToString());
            var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 14, 0) };
            item.Children.Add(swatch);
            item.Children.Add(Muted(ProviderCatalog.Get(tool).Name));
            legend.Children.Add(item);
        }

        // The chart is kept between rebuilds (it holds the Tokens/Cost choice): take it out of the old section first.
        (_chart.Parent as Panel)?.Children.Remove(_chart);
        var body = new StackPanel { Margin = new Thickness(0, 12, 0, 12) };
        body.Children.Add(head);
        body.Children.Add(data.Total.Tokens.Total == 0 ? Muted("No token usage in this range.") : _chart);
        body.Children.Add(legend);
        return new Border { Style = Res<Style>("Group"), Padding = new Thickness(14, 0, 14, 0), Margin = new Thickness(0, 0, 0, 16), Child = body };
    }

    private Border Table(string title, string nameHeader, string lastHeader, IEnumerable<(Tool Tool, string Name, string Tokens, string Last)> rows, string empty)
    {
        var body = new StackPanel { Margin = new Thickness(0, 12, 0, 8) };
        body.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        body.Children.Add(TableRow(null, nameHeader, "Tokens", lastHeader, header: true));
        var any = false;
        foreach (var row in rows)
        {
            any = true;
            body.Children.Add(TableRow(row.Tool, row.Name, row.Tokens, row.Last, header: false));
        }

        if (!any)
        {
            body.Children.Add(Muted(empty));
        }

        return new Border { Style = Res<Style>("Group"), Padding = new Thickness(14, 0, 14, 0), Child = body };
    }

    private static UIElement TableRow(Tool? tool, string name, string tokens, string last, bool header)
    {
        var grid = new Grid { MinHeight = header ? 20 : 30 };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(68) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        var label = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (tool is { } provider)
        {
            var swatch = new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 0, 7, 0) };
            swatch.SetResourceReference(Border.BackgroundProperty, provider.ToString());
            label.Children.Add(swatch);
        }

        label.Children.Add(Cell(name, header, TextAlignment.Left, provider: tool));
        var tokensCell = Cell(tokens, header, TextAlignment.Right);
        var lastCell = Cell(last, header, TextAlignment.Right);
        if (!header)
        {
            lastCell.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        }

        Grid.SetColumn(tokensCell, 1);
        Grid.SetColumn(lastCell, 2);
        grid.Children.Add(label);
        grid.Children.Add(tokensCell);
        grid.Children.Add(lastCell);
        var line = new Border { Child = grid, BorderThickness = new Thickness(0, header ? 0 : 1, 0, 0) };
        line.SetResourceReference(Border.BorderBrushProperty, "Line");
        return line;
    }

    private static TextBlock Cell(string text, bool header, TextAlignment alignment, Tool? provider = null)
    {
        var cell = new TextBlock
        {
            Text = text,
            TextAlignment = alignment,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontSize = header ? 11 : 12,
            MaxWidth = alignment == TextAlignment.Left ? 230 : double.PositiveInfinity,
            ToolTip = provider is { } tool ? $"{ProviderCatalog.Get(tool).Name} · {text}" : null,
        };
        if (header)
        {
            cell.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        }

        return cell;
    }

    private UIElement Heatmap(AnalyticsData data)
    {
        var accent = (TryFindResource("Accent") as SolidColorBrush)?.Color ?? Color.FromRgb(0x0A, 0x66, 0xD8);
        var maximum = Math.Max(1, data.Heatmap.Max(cell => cell.Tokens));
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        for (var hour = 0; hour < 24; hour++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition());
        }

        for (var row = 0; row < 8; row++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(row == 0 ? 16 : 17) });
        }

        for (var hour = 0; hour < 24; hour += 6)
        {
            var label = Muted(hour.ToString("00", CultureInfo.InvariantCulture));
            label.FontSize = 10;
            Grid.SetColumn(label, hour + 1);
            Grid.SetColumnSpan(label, 3);
            grid.Children.Add(label);
        }

        foreach (var cell in data.Heatmap)
        {
            // Shades of the accent: a faint base so empty hours still show the grid, darker for more tokens.
            var share = (double)cell.Tokens / maximum;
            var alpha = (byte)(cell.Tokens == 0 ? 14 : 40 + 215 * share);
            var square = new Border
            {
                CornerRadius = new CornerRadius(3),
                Margin = new Thickness(1.5),
                Background = new SolidColorBrush(Color.FromArgb(alpha, accent.R, accent.G, accent.B)),
                ToolTip = $"{DayNames[cell.DayOfWeekMondayFirst]} {cell.Hour:00}:00 · {Format.Tokens(cell.Tokens)} tokens",
            };
            Grid.SetRow(square, cell.DayOfWeekMondayFirst + 1);
            Grid.SetColumn(square, cell.Hour + 1);
            grid.Children.Add(square);
        }

        for (var day = 0; day < 7; day++)
        {
            var name = Muted(DayNames[day]);
            name.FontSize = 10.5;
            name.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRow(name, day + 1);
            grid.Children.Add(name);
        }

        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var hint = Muted("Local time · darker is more tokens");
        DockPanel.SetDock(hint, Dock.Right);
        head.Children.Add(hint);
        head.Children.Add(new TextBlock { Text = "Busiest hours", FontWeight = FontWeights.SemiBold });
        var body = new StackPanel { Margin = new Thickness(0, 12, 0, 12) };
        body.Children.Add(head);
        body.Children.Add(grid);
        return new Border { Style = Res<Style>("Group"), Padding = new Thickness(14, 0, 14, 0), Child = body };
    }

    private void ExportCsv()
    {
        if (_data is not { } data)
        {
            return;
        }

        var dialog = new SaveFileDialog { Filter = "CSV files (*.csv)|*.csv", FileName = $"track-analytics-{data.Range.StartDate:yyyyMMdd}-{data.Range.EndDateInclusive:yyyyMMdd}.csv" };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            File.WriteAllText(dialog.FileName, AnalyticsCsv.Write(data), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            SetStatus($"Exported to {dialog.FileName}");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            SetStatus($"Could not export: {error.Message}");
        }
    }

    private void SetStatus(string text)
    {
        _status.Text = text;
        _status.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private static T Res<T>(string key) => (T)Application.Current.FindResource(key);

    private static TextBlock Muted(string text)
    {
        var block = new TextBlock { Text = text, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
        block.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        return block;
    }

    private static TextBlock Footnote(string text) => new() { Text = text, Style = Res<Style>("Footnote"), Margin = new Thickness(4, 12, 4, 0) };

    private static string Span(TimeSpan? span) => span switch
    {
        null => "—",
        { TotalDays: >= 1 } value => $"{(int)value.TotalDays} d {value.Hours} h",
        { TotalHours: >= 1 } value => $"{(int)value.TotalHours} h {value.Minutes:00}",
        { } value => $"{value.TotalMinutes:0} min",
    };

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
