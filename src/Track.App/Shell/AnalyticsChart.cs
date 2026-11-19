using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Track.Core;
using Track.Core.Display;
using Track.Core.Providers;

namespace Track.App.Shell;

internal enum AnalyticsChartMetric
{
    Tokens,
    Cost,
}

[ExcludeFromCodeCoverage(Justification = "WPF drawing only; analytics values, ranges and aggregates are tested in Track.Core.")]
internal sealed class AnalyticsChart : FrameworkElement
{
    private const double AxisHeight = 18;
    private AnalyticsData? _data;

    public AnalyticsChart(AnalyticsChartMetric metric)
    {
        Metric = metric;
        Height = 156;
        MinHeight = 156;
        HorizontalAlignment = HorizontalAlignment.Stretch;
    }

    public AnalyticsChartMetric Metric { get; }

    public AnalyticsData? Data
    {
        get => _data;
        set
        {
            _data = value;
            InvalidateVisual();
        }
    }

    protected override Size MeasureOverride(Size available) =>
        new(double.IsInfinity(available.Width) ? 800 : available.Width, 156);

    protected override void OnRender(DrawingContext dc)
    {
        if (Data is not { Days.Count: > 0 } data || ActualWidth <= 0)
        {
            return;
        }

        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        var chartHeight = ActualHeight - AxisHeight;
        var values = data.Days.Select(day => data.Providers.Sum(tool => Value(day.Providers[tool]))).ToList();
        var maximum = values.DefaultIfEmpty(0).Max();
        var slot = ActualWidth / data.Days.Count;
        var barWidth = Math.Max(1, slot - (data.Days.Count > 45 ? 2 : 5));
        var line = Brush("Line");
        var muted = Brush("Muted");
        dc.DrawLine(new Pen(line, 1), new Point(0, chartHeight + 0.5), new Point(ActualWidth, chartHeight + 0.5));

        for (var index = 0; index < data.Days.Count; index++)
        {
            var x = (index * slot) + ((slot - barWidth) / 2);
            var bottom = chartHeight;
            foreach (var tool in data.Providers)
            {
                var value = Value(data.Days[index].Providers[tool]);
                if (value <= 0 || maximum <= 0)
                {
                    continue;
                }

                var height = value / maximum * (chartHeight - 3);
                bottom -= height;
                dc.DrawRoundedRectangle(Brush(tool.ToString()), null, new Rect(x, bottom, barWidth, height), 1.5, 1.5);
            }

            var labelStep = Math.Max(1, data.Days.Count / 8);
            if (index % labelStep == 0 || index == data.Days.Count - 1)
            {
                var label = data.Days[index].Date.ToString("MM-dd", CultureInfo.InvariantCulture);
                var text = new FormattedText(
                    label,
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Cascadia Mono, Consolas"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                    9,
                    muted,
                    VisualTreeHelper.GetDpi(this).PixelsPerDip);
                dc.DrawText(text, new Point(Math.Max(0, x + (barWidth - text.Width) / 2), chartHeight + 3));
            }
        }
    }

    private double Value(Track.Core.Usage.UsageTotals totals) => Metric == AnalyticsChartMetric.Tokens
        ? totals.Tokens.Total
        : (double)totals.KnownCost;

    private Brush Brush(string key) => (Brush)(TryFindResource(key) ?? Brushes.Gray);
}
