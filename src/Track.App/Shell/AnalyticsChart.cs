using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Track.Core.Display;
using Track.Core.Providers;
using Track.Core.Usage;

namespace Track.App.Shell;

internal enum AnalyticsChartMetric
{
    Tokens,
    Cost,
}

/// <summary>
/// Tokens or estimated cost per day, stacked by provider, drawn like the popover's chart: rounded tops, gaps between
/// days, a light grid with its values, and a hover card with each provider's share of the day.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "WPF drawing only; analytics values, ranges and aggregates are tested in Track.Core.")]
internal sealed class AnalyticsChart : FrameworkElement
{
    private const double AxisHeight = 20;
    private const double AxisWidth = 44;
    private const double ChartHeight = 200;
    private readonly ToolTip _card = new() { Placement = PlacementMode.Mouse, HorizontalOffset = 14, VerticalOffset = 8 };
    private AnalyticsData? _data;
    private AnalyticsChartMetric _metric = AnalyticsChartMetric.Tokens;
    private int _hover = -1;

    public AnalyticsChart()
    {
        Height = ChartHeight + AxisHeight;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        ToolTip = _card;
        ToolTipService.SetInitialShowDelay(this, 0);
        ToolTipService.SetBetweenShowDelay(this, 0);
        ToolTipService.SetShowDuration(this, int.MaxValue);
        MouseMove += (_, e) => Hover(e.GetPosition(this));
        MouseLeave += (_, _) => Hover(null);
    }

    public AnalyticsData? Data
    {
        get => _data;
        set
        {
            _data = value;
            _hover = -1;
            InvalidateVisual();
        }
    }

    public AnalyticsChartMetric Metric
    {
        get => _metric;
        set
        {
            _metric = value;
            InvalidateVisual();
        }
    }

    protected override Size MeasureOverride(Size available) =>
        new(double.IsInfinity(available.Width) ? 800 : available.Width, ChartHeight + AxisHeight);

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        if (Data is not { Days.Count: > 0 } data || ActualWidth <= AxisWidth)
        {
            return;
        }

        var top = NiceTop(data.Days.Select(day => Total(data, day)).DefaultIfEmpty(0).Max());
        var line = new Pen(Brush("Line"), 1);
        line.Freeze();
        var muted = Brush("Muted");
        foreach (var fraction in new[] { 1.0, 0.5, 0.0 })
        {
            var y = Math.Round(ChartHeight - fraction * (ChartHeight - 6)) + 0.5;
            dc.DrawLine(line, new Point(AxisWidth, y), new Point(ActualWidth, y));
            var label = Text(Label(top * fraction), muted, 10);
            dc.DrawText(label, new Point(AxisWidth - 8 - label.Width, y - label.Height / 2));
        }

        var (slot, width) = Slots(data.Days.Count);
        var labelStep = Math.Max(1, (int)Math.Ceiling(data.Days.Count / 10.0));
        for (var index = 0; index < data.Days.Count; index++)
        {
            var day = data.Days[index];
            var x = AxisWidth + index * slot + (slot - width) / 2;
            var bottom = ChartHeight;
            var stack = data.Providers.Where(tool => Value(day.Providers[tool]) > 0).ToList();
            for (var i = 0; i < stack.Count && top > 0; i++)
            {
                var height = Value(day.Providers[stack[i]]) / top * (ChartHeight - 6);
                bottom -= height;
                // Only the top segment of a day gets rounded corners, like the popover's bars.
                var radius = i == stack.Count - 1 ? Math.Min(3, width / 2) : 0;
                dc.DrawRoundedRectangle(Brush(stack[i].ToString()), null, new Rect(x, bottom, width, Math.Max(height, 0.5)), radius, radius);
            }

            if (index == _hover && stack.Count > 0)
            {
                var focus = new Pen(Brush("Accent"), 1.5);
                dc.DrawRoundedRectangle(null, focus, new Rect(x - 2, bottom - 2, width + 4, ChartHeight - bottom + 2), 3.5, 3.5);
            }

            if (index % labelStep == 0 || index == data.Days.Count - 1 && data.Days.Count <= 14)
            {
                var label = Text(DayLabel(day.Date, data.Days.Count), muted, 10.5);
                dc.DrawText(label, new Point(Math.Clamp(x + (width - label.Width) / 2, AxisWidth, ActualWidth - label.Width), ChartHeight + 5));
            }
        }
    }

    private void Hover(Point? point)
    {
        var index = -1;
        if (point is { } p && Data is { Days.Count: > 0 } data && p.X >= AxisWidth && p.Y <= ChartHeight)
        {
            index = Math.Clamp((int)((p.X - AxisWidth) / Slots(data.Days.Count).Slot), 0, data.Days.Count - 1);
        }

        if (index != _hover)
        {
            _hover = index;
            InvalidateVisual();
        }

        _card.IsOpen = index >= 0 && Data is { } shown && Total(shown, shown.Days[index]) > 0;
        if (_card.IsOpen)
        {
            _card.Content = Card(Data!, Data!.Days[index]);
        }
    }

    private UIElement Card(AnalyticsData data, AnalyticsDay day)
    {
        var card = new StackPanel { MinWidth = 160 };
        card.Children.Add(new TextBlock
        {
            Text = $"{day.Date.ToString("ddd d MMM", CultureInfo.InvariantCulture)} · {Label(Total(data, day))}",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4),
        });
        foreach (var tool in data.Providers.Where(tool => Value(day.Providers[tool]) > 0).Reverse())
        {
            var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
            var swatch = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            swatch.SetResourceReference(Border.BackgroundProperty, tool.ToString());
            var value = new TextBlock { Text = Label(Value(day.Providers[tool])), Margin = new Thickness(16, 0, 0, 0) };
            DockPanel.SetDock(swatch, Dock.Left);
            DockPanel.SetDock(value, Dock.Right);
            row.Children.Add(swatch);
            row.Children.Add(value);
            row.Children.Add(new TextBlock { Text = ProviderCatalog.Get(tool).Name });
            card.Children.Add(row);
        }

        return card;
    }

    private (double Slot, double Width) Slots(int count)
    {
        var slot = (ActualWidth - AxisWidth) / count;
        var gap = count > 45 ? 2 : count > 14 ? 5 : Math.Min(28, slot * 0.35);
        return (slot, Math.Max(1, slot - gap));
    }

    private double Total(AnalyticsData data, AnalyticsDay day) => data.Providers.Sum(tool => Value(day.Providers[tool]));

    private double Value(UsageTotals totals) => Metric == AnalyticsChartMetric.Tokens ? totals.Tokens.Total : (double)totals.KnownCost;

    private string Label(double value) => Metric == AnalyticsChartMetric.Tokens
        ? Format.Tokens((long)Math.Round(value))
        : value == 0 ? "$0" : value < 10 ? $"${value.ToString("0.00", CultureInfo.InvariantCulture)}" : $"${value.ToString("0", CultureInfo.InvariantCulture)}";

    /// <summary>A round top for the scale (1, 2 or 5 times a power of ten), so the grid values read well.</summary>
    private static double NiceTop(double maximum)
    {
        if (maximum <= 0)
        {
            return 1;
        }

        var power = Math.Pow(10, Math.Floor(Math.Log10(maximum)));
        foreach (var step in new[] { 1, 2, 5, 10 })
        {
            if (step * power >= maximum)
            {
                return step * power;
            }
        }

        return 10 * power;
    }

    private static string DayLabel(DateOnly date, int days) => days <= 14
        ? date.ToString("ddd d", CultureInfo.InvariantCulture)
        : date.ToString("d MMM", CultureInfo.InvariantCulture);

    private FormattedText Text(string text, Brush brush, double size) => new(
        text,
        CultureInfo.InvariantCulture,
        FlowDirection.LeftToRight,
        new Typeface(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
        size,
        brush,
        VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private Brush Brush(string key) => (Brush)(TryFindResource(key) ?? Brushes.Gray);
}
