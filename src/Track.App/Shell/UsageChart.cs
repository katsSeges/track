using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Track.Core;
using Track.Core.Display;
using Track.Core.Providers;

namespace Track.App.Shell;

/// <summary>
/// Draws a <see cref="ChartViewModel"/> (round 5): bars stacked by tool in the theme's tool colors, a dashed top
/// line, a base line and the axis labels. Hover (or Left/Right with the keyboard) shows the bar's tooltip; a click
/// (or Enter) runs <see cref="SelectCommand"/> with the bar index. All numbers and texts come from Track.Core.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "WPF drawing; the chart model and every text it shows are tested in Track.Core.")]
public sealed class UsageChart : FrameworkElement
{
    public static readonly DependencyProperty ChartProperty = DependencyProperty.Register(
        nameof(Chart), typeof(ChartViewModel), typeof(UsageChart), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((UsageChart)d).OnChartChanged()));

    public static readonly DependencyProperty SelectCommandProperty = DependencyProperty.Register(
        nameof(SelectCommand), typeof(ICommand), typeof(UsageChart));

    public static readonly DependencyProperty BarsHeightProperty = DependencyProperty.Register(
        nameof(BarsHeight), typeof(double), typeof(UsageChart), new FrameworkPropertyMetadata(64.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    private const double AxisHeight = 13;
    private static readonly Dictionary<Tool, string> BrushKeys = ProviderCatalog.Order.ToDictionary(tool => tool, tool => tool.ToString());

    // Always above the chart, starting at the bar: placed at the mouse, a tooltip near the screen edge (the popover
    // sits at the right edge) was pushed left over other bars and swallowed the next click (real-mouse check 2026-11-18).
    private readonly ToolTip _tip = new() { Placement = PlacementMode.Top, HasDropShadow = true, IsHitTestVisible = false };
    private int _hover = -1;
    private int _focus = -1;

    public UsageChart()
    {
        Focusable = true;
        FocusVisualStyle = null;
        ToolTip = _tip;
        ToolTipService.SetInitialShowDelay(this, 0);
        ToolTipService.SetBetweenShowDelay(this, 0);
        ToolTipService.SetShowDuration(this, int.MaxValue);
        _tip.Opened += (_, _) => UpdateTip(_hover >= 0 ? _hover : _focus);
        Cursor = Cursors.Hand;
    }

    public ChartViewModel? Chart
    {
        get => (ChartViewModel?)GetValue(ChartProperty);
        set => SetValue(ChartProperty, value);
    }

    public ICommand? SelectCommand
    {
        get => (ICommand?)GetValue(SelectCommandProperty);
        set => SetValue(SelectCommandProperty, value);
    }

    /// <summary>Height of the bar area in DIPs (64 = "medium" from round 5); the axis adds 13.</summary>
    public double BarsHeight
    {
        get => (double)GetValue(BarsHeightProperty);
        set => SetValue(BarsHeightProperty, value);
    }

    protected override Size MeasureOverride(Size available) => new(double.IsInfinity(available.Width) ? 300 : available.Width, BarsHeight + AxisHeight);

    protected override void OnRender(DrawingContext dc)
    {
        if (Chart is not { } chart || chart.Bars.Count == 0 || ActualWidth <= 0)
        {
            return;
        }

        // A transparent background makes the whole column of every bar clickable and hoverable. Without it WPF only
        // hit-tests painted pixels, so a click above a short bar did nothing (real-mouse check 2026-11-18).
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));

        var line = Brush("Line");
        var muted = Brush("Muted");
        var h = BarsHeight;
        var (slot, width) = Slots(chart.Bars.Count);

        dc.DrawLine(new Pen(line, 1) { DashStyle = new DashStyle([2, 3], 0) }, new Point(0, 0.5), new Point(ActualWidth, 0.5));
        dc.DrawLine(new Pen(line, 1), new Point(0, h + 0.5), new Point(ActualWidth, h + 0.5));

        foreach (var bar in chart.Bars)
        {
            var x = (bar.Index * slot) + ((slot - width) / 2);
            if (bar.Future)
            {
                dc.DrawRoundedRectangle(line, null, new Rect(x, h - 2, width, 2), 1, 1);
                continue;
            }

            var pixels = ChartViewModel.SegmentPixels(bar, h);
            var bottom = h;
            dc.PushOpacity(bar.Current || bar.Selected ? 1 : 0.85);
            for (var i = 0; i < bar.Segments.Count; i++)
            {
                bottom -= pixels[i];
                dc.DrawRoundedRectangle(Brush(BrushKeys[bar.Segments[i].Tool]), null, new Rect(x, bottom, width, pixels[i]), 1.5, 1.5);
            }

            dc.Pop();
            if (bar.Selected || bar.Index == _focus && IsKeyboardFocused)
            {
                dc.DrawRoundedRectangle(null, new Pen(Brush("Accent"), 1.5), new Rect(x - 1.5, Math.Min(bottom, h - 4) - 1.5, width + 3, h - Math.Min(bottom, h - 4) + 3), 2.5, 2.5);
            }
        }

        var typeface = new Typeface(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        foreach (var label in chart.Axis)
        {
            var text = new FormattedText(label.Text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 9, muted, dpi);
            var cx = Math.Clamp(label.Position * slot, text.Width / 2, ActualWidth - (text.Width / 2));
            dc.DrawText(text, new Point(cx - (text.Width / 2), h + 2));
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var index = IndexAt(e.GetPosition(this));
        if (index != _hover)
        {
            _hover = index;
            UpdateTip(index);
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        _hover = -1;
        _tip.IsOpen = false;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        var index = IndexAt(e.GetPosition(this));
        if (index >= 0)
        {
            SelectCommand?.Execute(index);
            e.Handled = true; // a click on a bar is not a click on the island around it
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (Chart is not { } chart)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Left:
            case Key.Right:
                _focus = Math.Clamp((_focus < 0 ? chart.Bars.Count(b => !b.Future) : _focus) + (e.Key == Key.Left ? -1 : 1), 0, chart.Bars.Count - 1);
                UpdateTip(_focus);
                _tip.IsOpen = true;
                InvalidateVisual();
                e.Handled = true;
                break;
            case Key.Enter when _focus >= 0:
                SelectCommand?.Execute(_focus);
                e.Handled = true;
                break;
        }
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        _focus = -1;
        _tip.IsOpen = false;
        InvalidateVisual();
    }

    private void OnChartChanged()
    {
        if (_tip.IsOpen)
        {
            UpdateTip(_hover >= 0 ? _hover : _focus);
        }
    }

    private int IndexAt(Point p)
    {
        if (Chart is not { } chart || p.Y > BarsHeight + AxisHeight || p.X < 0)
        {
            return -1;
        }

        var index = (int)(p.X / Slots(chart.Bars.Count).Slot);
        return index < chart.Bars.Count ? index : -1;
    }

    private (double Slot, double Width) Slots(int count)
    {
        var slot = ActualWidth / count;
        var gap = count > 10 ? 2 : 6;
        return (slot, Math.Max(1, slot - gap));
    }

    /// <summary>The tooltip: first line bold, then one line per tool with a dot in the tool's color.</summary>
    private void UpdateTip(int index)
    {
        if (Chart is not { } chart || index < 0 || index >= chart.Bars.Count)
        {
            _tip.IsOpen = false;
            return;
        }

        var block = new TextBlock { FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"), FontSize = 11, LineHeight = 16 };
        var lines = chart.Bars[index].Tooltip.Split('\n');
        block.Inlines.Add(new Run(lines[0]) { FontWeight = FontWeights.SemiBold });
        foreach (var text in lines.Skip(1))
        {
            block.Inlines.Add(new LineBreak());
            var tool = BrushKeys.FirstOrDefault(k => text.StartsWith($"● {ToolNames.Display(k.Key)}", StringComparison.Ordinal));
            if (tool.Value is not null)
            {
                block.Inlines.Add(new Run("● ") { Foreground = Brush(tool.Value) });
                block.Inlines.Add(new Run(text[2..]));
            }
            else
            {
                block.Inlines.Add(new Run(text));
            }
        }

        _tip.Content = block;
        _tip.PlacementTarget = this;
        _tip.HorizontalOffset = (index * ActualWidth / chart.Bars.Count) - 6;
        _tip.VerticalOffset = -4;
        _tip.IsOpen = true;
    }

    private Brush Brush(string key) => (Brush)(TryFindResource(key) ?? Brushes.Gray);
}
