using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Track.Core;
using Track.Core.Display;
using Track.Core.Providers;
using Track.Core.Settings;

namespace Track.App.Shell;

[ExcludeFromCodeCoverage(Justification = "WPF analytics UI; range and aggregate behavior is covered in Track.Core.")]
internal sealed class AnalyticsWindow : Window
{
    private readonly AnalyticsBuilder _builder;
    private readonly Func<AnalyticsRange, IEnumerable<Tool>, AnalyticsData> _build;
    private readonly Func<IReadOnlyList<Tool>> _visibleProviders;
    private readonly Border _surface = new() { CornerRadius = new CornerRadius(12), Padding = new Thickness(16) };
    private readonly StackPanel _rangeSelector = new() { Orientation = Orientation.Horizontal };
    private readonly StackPanel _dateControls = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 2) };
    private readonly List<RadioButton> _rangeOptions = [];
    private readonly TextBlock _startLabel = new() { Text = "From", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0), FontSize = 11 };
    private readonly DatePicker _start = new() { Width = 150, FontSize = 10, Margin = new Thickness(0, 0, 8, 0), ToolTip = "Start date" };
    private readonly TextBlock _endLabel = new() { Text = "To", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0), FontSize = 11 };
    private readonly DatePicker _end = new() { Width = 150, FontSize = 10, Margin = new Thickness(0, 0, 8, 0), ToolTip = "End date" };
    private readonly TextBlock _status = new() { Margin = new Thickness(0, 4, 0, 6), FontSize = 11 };
    private readonly StackPanel _content = new();
    private readonly AnalyticsChart _tokenChart = new(AnalyticsChartMetric.Tokens);
    private readonly AnalyticsChart _costChart = new(AnalyticsChartMetric.Cost);
    private AnalyticsRange _range;
    private AnalyticsData? _data;
    private bool _dark;
    private WindowMaterial _material;
    private int _selectedPreset;

    public AnalyticsWindow(
        AnalyticsBuilder builder,
        Func<AnalyticsRange, IEnumerable<Tool>, AnalyticsData> build,
        Func<IReadOnlyList<Tool>> visibleProviders,
        bool dark,
        WindowMaterial material)
    {
        _builder = builder;
        _build = build;
        _visibleProviders = visibleProviders;
        _dark = dark;
        _material = material;
        _range = builder.LastDays(7);
        Title = "Track Analytics";
        Width = 1040;
        Height = 820;
        MinWidth = 800;
        MinHeight = 560;
        MaxHeight = SystemParameters.WorkArea.Height - 30;
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
        BuildContent();
        RefreshData();
    }

    public void ApplyAppearance(bool dark, WindowMaterial material)
    {
        _dark = dark;
        _material = material;
        _tokenChart.InvalidateVisual();
        _costChart.InvalidateVisual();
        if (IsLoaded)
        {
            ApplyBackdrop();
        }
    }

    public void ApplyBackdrop()
    {
        var supported = Native.ApplyBackdrop(this, _dark, _material);
        _surface.SetResourceReference(Border.BackgroundProperty, supported ? "PopBg" : "PopSolid");
        _surface.SetResourceReference(Border.BorderBrushProperty, "Line");
    }

    public void PrepareForRender()
    {
        MaxHeight = double.PositiveInfinity;
        SizeToContent = SizeToContent.Height;
        if (_surface.Child is ScrollViewer scroll)
        {
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        }
    }

    public void SaveRangeSelectorForRender(string path)
    {
        SaveElement(_rangeSelector, path);
    }

    public void SaveCustomRangeForRender(string windowPath, string calendarPath)
    {
        _selectedPreset = 3;
        _rangeOptions[_selectedPreset].IsChecked = true;
        UpdateDateInputs();
        UpdateLayout();
        SaveWindowForRender(windowPath);
        _start.ApplyTemplate();
        _start.IsDropDownOpen = true;
        UpdateLayout();
        SavePopupForRender(_start, calendarPath);
        _start.IsDropDownOpen = false;
    }

    public void RefreshData()
    {
        _data = _build(_range, _visibleProviders());
        _tokenChart.Data = _data;
        _costChart.Data = _data;
        RebuildContent();
    }

    private void BuildContent()
    {
        var labels = new[] { "7 days", "30 days", "90 days", "Custom" };
        for (var index = 0; index < labels.Length; index++)
        {
            var selected = index;
            var option = new RadioButton
            {
                Content = labels[index],
                GroupName = "AnalyticsRange",
                IsChecked = index == _selectedPreset,
                Style = (Style)FindResource("Segment"),
                Padding = new Thickness(9, 4, 9, 4),
                ToolTip = index == 3 ? "Choose a custom date range" : $"Show the last {labels[index]}",
            };
            option.Checked += (_, _) =>
            {
                _selectedPreset = selected;
                UpdateDateInputs();
            };
            _rangeOptions.Add(option);
            _rangeSelector.Children.Add(option);
        }

        var rangeGroup = new Border { CornerRadius = new CornerRadius(7), Padding = new Thickness(2) };
        rangeGroup.SetResourceReference(Border.BackgroundProperty, "RowBg");
        rangeGroup.Child = _rangeSelector;
        var apply = new Button { Content = "Apply range", Padding = new Thickness(10, 4, 10, 4) };
        apply.SetResourceReference(Control.StyleProperty, "TrackPrimaryButton");
        apply.Click += (_, _) => ApplyRange();
        var export = new Button { Content = "Export CSV…", Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(8, 0, 0, 0) };
        export.SetResourceReference(Control.StyleProperty, "LinkButton");
        export.Click += (_, _) => ExportCsv();
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        actions.Children.Add(apply);
        actions.Children.Add(export);
        var rangeControls = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        rangeControls.Children.Add(rangeGroup);
        rangeControls.Children.Add(actions);
        _dateControls.Children.Add(_startLabel);
        _dateControls.Children.Add(_start);
        _dateControls.Children.Add(_endLabel);
        _dateControls.Children.Add(_end);
        _start.SetResourceReference(Control.FontFamilyProperty, "Mono");
        _end.SetResourceReference(Control.FontFamilyProperty, "Mono");
        _status.SetResourceReference(TextBlock.FontFamilyProperty, "Mono");
        _status.FontSize = 10;

        var root = new StackPanel();
        var title = new DockPanel();
        var heading = new TextBlock { Text = "Analytics", FontSize = 18, FontWeight = FontWeights.SemiBold };
        heading.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
        title.Children.Add(heading);
        root.Children.Add(title);
        root.Children.Add(rangeControls);
        root.Children.Add(_dateControls);
        root.Children.Add(_status);
        root.Children.Add(_content);
        root.SetResourceReference(Control.ForegroundProperty, "Fg");
        _status.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        _surface.Child = new ScrollViewer
        {
            Content = root,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        _surface.SetResourceReference(Border.BackgroundProperty, "PopSolid");
        UpdateDateInputs();
    }

    private void RebuildContent()
    {
        if (_data is not { } data)
        {
            return;
        }

        _content.Children.Clear();
        _status.Text = data.Total.Tokens.Total == 0 ? "No token usage in this range." : $"{data.Range.StartDate:yyyy-MM-dd} to {data.Range.EndDateInclusive:yyyy-MM-dd}";
        _content.Children.Add(SummaryRow(data));
        _content.Children.Add(SectionTitle("Tokens per day · stacked by provider"));
        _content.Children.Add(_tokenChart);
        _content.Children.Add(ProviderLegend(data));
        _content.Children.Add(SectionTitle("Estimated cost per day · known prices only"));
        _content.Children.Add(_costChart);
        _content.Children.Add(SectionTitle("Model breakdown"));
        _content.Children.Add(ModelRows(data));
        _content.Children.Add(SectionTitle("Busiest hours · local time"));
        _content.Children.Add(Heatmap(data));
        _content.Children.Add(SectionTitle("Top sessions"));
        _content.Children.Add(new TextBlock
        {
            Text = "A session is included when its first-to-last observed interval overlaps the selected range. Token totals are full-session totals; the first-to-last span is observed span, not active duration. Older 1.1.0 snapshots retained only seven days of session history, so older sessions may be missing until the new history window fills.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
            FontSize = 11,
        });
        _content.Children.Add(SessionRows(data));
        _tokenChart.Data = data;
        _costChart.Data = data;
    }

    private UIElement SummaryRow(AnalyticsData data)
    {
        var row = new UniformGrid { Columns = 5, Margin = new Thickness(0, 8, 0, 8) };
        var cards = new[]
        {
            SummaryCard("Tokens", Format.Tokens(data.Total.Tokens.Total)),
            SummaryCard("Estimated cost", Format.Cost(data.Total)),
            SummaryCard("Cache hit rate", data.CacheHitRate is { } hit ? hit.ToString("P1", CultureInfo.InvariantCulture) : "—"),
            SummaryCard("Sessions", data.SessionCount.ToString(CultureInfo.InvariantCulture)),
            SummaryCard("Median observed span", FormatSpan(data.MedianObservedSpan)),
        };
        for (var index = 0; index < cards.Length; index++)
        {
            var item = new Border { Child = cards[index], BorderThickness = index == 0 ? new Thickness(0) : new Thickness(1, 0, 0, 0) };
            item.SetResourceReference(Border.BorderBrushProperty, "Line");
            row.Children.Add(item);
        }

        return row;
    }

    private static UIElement SummaryCard(string label, string value)
    {
        var panel = new StackPanel { Margin = new Thickness(8, 4, 8, 4), MinWidth = 110 };
        var heading = new TextBlock { Text = label, FontSize = 10, TextWrapping = TextWrapping.Wrap };
        heading.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        heading.SetResourceReference(TextBlock.FontFamilyProperty, "Mono");
        panel.Children.Add(heading);
        var metric = new TextBlock { Text = value, FontSize = 13, FontWeight = FontWeights.Medium, Margin = new Thickness(0, 2, 0, 0) };
        metric.SetResourceReference(TextBlock.FontFamilyProperty, "Mono");
        panel.Children.Add(metric);
        return panel;
    }

    private TextBlock SectionTitle(string text)
    {
        var title = new TextBlock
        {
            Text = text,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(2, 9, 0, 4),
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
        return title;
    }

    private UIElement ModelRows(AnalyticsData data)
    {
        var panel = RowsPanel();
        AddTableHeader(panel, "Provider", "Model", "Tokens", "Estimated cost");
        foreach (var model in data.Models)
        {
            AddTableRow(panel, ProviderCatalog.Get(model.Tool).Name, model.Model, Format.Tokens(model.Totals.Tokens.Total), Format.Cost(model.Totals));
        }

        if (data.Models.Count == 0)
        {
            panel.Children.Add(EmptyRow("No models in this range."));
        }

        return panel;
    }

    private UIElement SessionRows(AnalyticsData data)
    {
        var panel = RowsPanel();
        AddTableHeader(panel, "Provider", "Session", "Full-session tokens", "Observed span");
        foreach (var session in data.TopSessions)
        {
            AddTableRow(panel, ProviderCatalog.Get(session.Tool).Name, session.SessionId,
                Format.Tokens(session.Usage.Tokens.Total), FormatSpan(session.ObservedSpan));
        }

        if (data.TopSessions.Count == 0)
        {
            panel.Children.Add(EmptyRow("No sessions overlap this range."));
        }

        return panel;
    }

    private UIElement Heatmap(AnalyticsData data)
    {
        var root = new StackPanel();
        var hours = new UniformGrid { Columns = 24, Margin = new Thickness(48, 0, 0, 2) };
        for (var hour = 0; hour < 24; hour++)
        {
            hours.Children.Add(new TextBlock
            {
                Text = hour % 6 == 0 ? hour.ToString("00", CultureInfo.InvariantCulture) : "",
                FontSize = 8,
                HorizontalAlignment = HorizontalAlignment.Center,
            });
        }

        root.Children.Add(hours);
        var cells = new UniformGrid { Columns = 24, Rows = 7 };
        var maximum = data.Heatmap.Max(cell => cell.Tokens);
        var accent = (TryFindResource("Accent") as SolidColorBrush)?.Color ?? Colors.DodgerBlue;
        for (var day = 0; day < 7; day++)
        {
            for (var hour = 0; hour < 24; hour++)
            {
                var cell = data.Heatmap.Single(item => item.DayOfWeekMondayFirst == day && item.Hour == hour);
                var intensity = maximum == 0 ? 0 : (double)cell.Tokens / maximum;
                var alpha = (byte)Math.Clamp(24 + (int)(231 * intensity), 24, 255);
                var border = new Border
                {
                    Height = 13,
                    Margin = new Thickness(1),
                    CornerRadius = new CornerRadius(2),
                    Background = new SolidColorBrush(Color.FromArgb(alpha, accent.R, accent.G, accent.B)),
                    ToolTip = $"{DayName(day)} {hour:00}:00 · {Format.Tokens(cell.Tokens)} tokens",
                };
                cells.Children.Add(border);
            }
        }

        var labeled = new Grid();
        labeled.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
        labeled.ColumnDefinitions.Add(new ColumnDefinition());
        var days = new StackPanel();
        for (var day = 0; day < 7; day++)
        {
            days.Children.Add(new TextBlock { Text = DayName(day), Height = 15, FontSize = 9, VerticalAlignment = VerticalAlignment.Center });
        }

        Grid.SetColumn(days, 0);
        Grid.SetColumn(cells, 1);
        labeled.Children.Add(days);
        labeled.Children.Add(cells);
        root.Children.Add(labeled);
        return root;
    }

    private UIElement ProviderLegend(AnalyticsData data)
    {
        var legend = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(3, 0, 0, 4) };
        foreach (var tool in data.Providers)
        {
            var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 0) };
            item.Children.Add(new Border
            {
                Width = 9,
                Height = 9,
                CornerRadius = new CornerRadius(5),
                Background = (Brush)(TryFindResource(tool.ToString()) ?? Brushes.Gray),
                Margin = new Thickness(0, 0, 4, 0),
            });
            item.Children.Add(new TextBlock { Text = ProviderCatalog.Get(tool).Name, FontSize = 10 });
            legend.Children.Add(item);
        }

        return legend;
    }

    private void AddTableHeader(Panel panel, string first, string second, string third, string fourth) =>
        AddTableRow(panel, first, second, third, fourth, bold: true);

    private void AddTableRow(Panel panel, string first, string second, string third, string fourth, bool bold = false)
    {
        var row = new Grid { Margin = new Thickness(2, 2, 2, 2) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.25, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.25, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.25, GridUnitType.Star) });
        var values = new[] { first, second, third, fourth };
        for (var index = 0; index < values.Length; index++)
        {
            var text = new TextBlock
            {
                Text = values[index],
                FontWeight = bold ? FontWeights.Medium : FontWeights.Normal,
                FontSize = bold ? 10 : 11,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(5, 3, 8, 3),
                ToolTip = values[index],
            };
            if (bold || index >= 2)
            {
                text.SetResourceReference(TextBlock.FontFamilyProperty, "Mono");
                if (bold)
                {
                    text.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
                }
            }
            Grid.SetColumn(text, index);
            row.Children.Add(text);
        }

        var line = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Child = row };
        line.SetResourceReference(Border.BorderBrushProperty, "Line");
        panel.Children.Add(line);
    }

    private void ApplyRange()
    {
        try
        {
            _range = _selectedPreset switch
            {
                0 => _builder.LastDays(7),
                1 => _builder.LastDays(30),
                2 => _builder.LastDays(90),
                _ => _builder.CustomRange(
                    ParseDate(_start.SelectedDate, "start"),
                    ParseDate(_end.SelectedDate, "end")),
            };
            RefreshData();
        }
        catch (ArgumentException error)
        {
            _status.Text = error.Message;
        }
    }

    private void UpdateDateInputs()
    {
        var custom = _selectedPreset == 3;
        _startLabel.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
        _endLabel.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
        _start.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
        _end.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
        _dateControls.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
        if (custom)
        {
            if (string.IsNullOrWhiteSpace(_start.Text))
            {
                _start.SelectedDate = _range.StartDate.ToDateTime(TimeOnly.MinValue);
            }

            if (string.IsNullOrWhiteSpace(_end.Text))
            {
                _end.SelectedDate = _range.EndDateInclusive.ToDateTime(TimeOnly.MinValue);
            }
        }
    }

    private static DateOnly ParseDate(DateTime? value, string label) =>
        value is { } date
            ? DateOnly.FromDateTime(date)
            : throw new ArgumentException($"Choose the {label} date.");

    private void SaveWindowForRender(string path)
    {
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var width = Math.Max(1, (int)Math.Ceiling(_surface.ActualWidth * scale));
        var height = Math.Max(1, (int)Math.Ceiling(_surface.ActualHeight * scale));
        var bitmap = new RenderTargetBitmap(width, height, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(_surface);
        SaveBitmap(bitmap, path);
    }

    private static void SavePopupForRender(ComboBox selector, string path)
    {
        if (selector.Template.FindName("PART_Popup", selector) is Popup { Child: FrameworkElement content } popup)
        {
            content.Measure(new Size(380, 360));
            content.Arrange(new Rect(new Point(0, 0), content.DesiredSize));
            content.UpdateLayout();
            SaveElement(content, path);
        }
    }

    private static void SavePopupForRender(DatePicker picker, string path)
    {
        if (picker.Template.FindName("PART_Popup", picker) is Popup { Child: FrameworkElement content } popup)
        {
            content.Measure(new Size(380, 360));
            content.Arrange(new Rect(new Point(0, 0), content.DesiredSize));
            content.UpdateLayout();
            SaveElement(content, path);
        }
    }

    private static void SaveElement(FrameworkElement element, string path)
    {
        var scale = VisualTreeHelper.GetDpi(element).DpiScaleX;
        var width = Math.Max(1, (int)Math.Ceiling(element.ActualWidth * scale));
        var height = Math.Max(1, (int)Math.Ceiling(element.ActualHeight * scale));
        var bitmap = new RenderTargetBitmap(width, height, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(element);
        SaveBitmap(bitmap, path);
    }

    private static void SaveBitmap(RenderTargetBitmap bitmap, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path);
        encoder.Save(output);
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
            _status.Text = $"Exported {dialog.FileName}";
        }
        catch (IOException error)
        {
            _status.Text = $"Could not export CSV: {error.Message}";
        }
        catch (UnauthorizedAccessException error)
        {
            _status.Text = $"Could not export CSV: {error.Message}";
        }
    }

    private static StackPanel RowsPanel() => new() { Margin = new Thickness(0, 0, 0, 4) };

    private static TextBlock EmptyRow(string text) => new() { Text = text, Margin = new Thickness(4, 4, 0, 4), FontSize = 11 };

    private static string FormatSpan(TimeSpan? span) => span switch
    {
        null => "—",
        { } value when value.TotalDays >= 1 => $"{(int)value.TotalDays} d {value.Hours} h",
        { } value when value.TotalHours >= 1 => $"{(int)value.TotalHours} h {value.Minutes} min",
        { } value => $"{value.TotalMinutes:0} min",
    };

    private static string DayName(int mondayFirstIndex) => CultureInfo.CurrentCulture.DateTimeFormat.GetDayName(
        (DayOfWeek)((mondayFirstIndex + 1) % 7));
}
