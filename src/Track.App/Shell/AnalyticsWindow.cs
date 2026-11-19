using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
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
    private readonly Border _surface = new() { CornerRadius = new CornerRadius(12), Padding = new Thickness(18) };
    private readonly ComboBox _preset = new() { Width = 130, Margin = new Thickness(0, 0, 8, 0) };
    private readonly DatePicker _start = new() { Width = 140, Margin = new Thickness(0, 0, 8, 0) };
    private readonly DatePicker _end = new() { Width = 140, Margin = new Thickness(0, 0, 8, 0) };
    private readonly TextBlock _status = new() { Margin = new Thickness(0, 4, 0, 6), FontSize = 11 };
    private readonly StackPanel _content = new();
    private readonly AnalyticsChart _tokenChart = new(AnalyticsChartMetric.Tokens);
    private readonly AnalyticsChart _costChart = new(AnalyticsChartMetric.Cost);
    private AnalyticsRange _range;
    private AnalyticsData? _data;
    private bool _dark;
    private WindowMaterial _material;

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
        Foreground = Brushes.Black;
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

    public void RefreshData()
    {
        _data = _build(_range, _visibleProviders());
        _tokenChart.Data = _data;
        _costChart.Data = _data;
        RebuildContent();
    }

    private void BuildContent()
    {
        _preset.Items.Add("Last 7 days");
        _preset.Items.Add("Last 30 days");
        _preset.Items.Add("Last 90 days");
        _preset.Items.Add("Custom");
        _preset.SelectedIndex = 0;
        _preset.SelectionChanged += (_, _) => UpdateDateInputs();
        var apply = new Button { Content = "Apply range", Padding = new Thickness(10, 4, 10, 4) };
        apply.Click += (_, _) => ApplyRange();
        var export = new Button { Content = "Export CSV…", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(8, 0, 0, 0) };
        export.Click += (_, _) => ExportCsv();
        var rangeControls = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 4) };
        rangeControls.Children.Add(_preset);
        rangeControls.Children.Add(_start);
        rangeControls.Children.Add(_end);
        rangeControls.Children.Add(apply);
        rangeControls.Children.Add(export);

        var root = new StackPanel();
        var title = new DockPanel();
        title.Children.Add(new TextBlock { Text = "Analytics", FontSize = 22, FontWeight = FontWeights.SemiBold });
        root.Children.Add(title);
        root.Children.Add(rangeControls);
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
        row.Children.Add(SummaryCard("Tokens", Format.Tokens(data.Total.Tokens.Total)));
        row.Children.Add(SummaryCard("Estimated cost", Format.Cost(data.Total)));
        row.Children.Add(SummaryCard("Cache hit rate", data.CacheHitRate is { } hit ? hit.ToString("P1", CultureInfo.InvariantCulture) : "—"));
        row.Children.Add(SummaryCard("Sessions", data.SessionCount.ToString(CultureInfo.InvariantCulture)));
        row.Children.Add(SummaryCard("Median observed span", FormatSpan(data.MedianObservedSpan)));
        return row;
    }

    private static UIElement SummaryCard(string label, string value)
    {
        var panel = new StackPanel { Margin = new Thickness(4), MinWidth = 110 };
        panel.Children.Add(new TextBlock { Text = label, FontSize = 10, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = value, FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 2, 0, 0) });
        return panel;
    }

    private static TextBlock SectionTitle(string text) => new()
    {
        Text = text,
        FontSize = 14,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(2, 14, 0, 3),
    };

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
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(4, 2, 8, 2),
                ToolTip = values[index],
            };
            Grid.SetColumn(text, index);
            row.Children.Add(text);
        }

        panel.Children.Add(row);
    }

    private void ApplyRange()
    {
        try
        {
            _range = _preset.SelectedIndex switch
            {
                0 => _builder.LastDays(7),
                1 => _builder.LastDays(30),
                2 => _builder.LastDays(90),
                _ => _builder.CustomRange(
                    DateOnly.FromDateTime(_start.SelectedDate ?? throw new ArgumentException("Choose a start date.")),
                    DateOnly.FromDateTime(_end.SelectedDate ?? throw new ArgumentException("Choose an end date."))),
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
        var custom = _preset.SelectedIndex == 3;
        _start.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
        _end.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
        if (custom)
        {
            _start.SelectedDate ??= _range.StartDate.ToDateTime(TimeOnly.MinValue);
            _end.SelectedDate ??= _range.EndDateInclusive.ToDateTime(TimeOnly.MinValue);
        }
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
