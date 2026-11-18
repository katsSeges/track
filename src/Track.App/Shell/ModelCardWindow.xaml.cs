using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Media;
using Track.Core.Display;

namespace Track.App.Shell;

/// <summary>The per-model breakdown card shown next to the popover. It never takes focus.</summary>
[ExcludeFromCodeCoverage(Justification = "WPF view; content comes from the tested ModelBreakdownViewModel.")]
public partial class ModelCardWindow : Window
{
    public ModelCardWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            Native.MakeToolWindow(this, noActivate: true);
            Surface.Background = (Brush)FindResource(Native.ApplyBackdrop(this, Dark) ? "PopBg" : "PopSolid");
        };
    }

    public bool Dark { get; set; }

    /// <summary>Shows the card to the left of the popover, level with the row; above it when there is no room.</summary>
    public void ShowFor(ModelBreakdownViewModel card, Window popover, FrameworkElement row)
    {
        DataContext = card;
        Logo.Key = card.Tool.ToString().ToLowerInvariant();
        Share.Shares = card.Rows.Select(r => r.Share).ToList();
        Share.Key = Logo.Key;
        Empty.Visibility = card.Rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        Show();
        UpdateLayout();
        var area = SystemParameters.WorkArea;
        var rowTop = row.TranslatePoint(new Point(0, 0), popover).Y + popover.Top;
        if (popover.Left - Width - 8 >= area.Left)
        {
            Left = popover.Left - Width - 8;
            Top = Math.Clamp(rowTop - 8, area.Top + 8, area.Bottom - ActualHeight - 8);
        }
        else
        {
            Left = popover.Left + popover.Width - Width;
            Top = popover.Top - ActualHeight - 8;
        }
    }
}

/// <summary>Stacked bar of model shares, in the tool's color with decreasing strength.</summary>
[ExcludeFromCodeCoverage(Justification = "WPF control; checked by screenshots.")]
public sealed class ShareBar : FrameworkElement
{
    private static readonly double[] Shades = [1, 0.62, 0.36, 0.22];

    private static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        "Fill", typeof(Brush), typeof(ShareBar), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    private IReadOnlyList<double> _shares = [];
    private string? _key;

    public IReadOnlyList<double> Shares
    {
        get => _shares;
        set
        {
            _shares = value;
            InvalidateVisual();
        }
    }

    public string? Key
    {
        get => _key;
        set
        {
            _key = value;
            if (Logos.FromKey(value) is { } tool)
            {
                SetResourceReference(FillProperty, tool.ToString());
            }
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        var x = 0.0;
        var fill = (Brush)GetValue(FillProperty);
        for (var i = 0; i < _shares.Count; i++)
        {
            var width = Math.Max(0, (ActualWidth * _shares[i]) - 1);
            dc.PushOpacity(Shades[Math.Min(i, Shades.Length - 1)]);
            dc.DrawRoundedRectangle(fill, null, new Rect(x, 0, width, ActualHeight), 2, 2);
            dc.Pop();
            x += width + 1;
        }
    }
}
