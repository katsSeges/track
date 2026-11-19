using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Track.Core;
using Track.Core.Settings;

namespace Track.App.Shell;

/// <summary>The popover above the tray icon. All content comes from <c>PopoverViewModel</c>.</summary>
[ExcludeFromCodeCoverage(Justification = "WPF view; behavior lives in the tested PopoverViewModel, looks are checked by screenshots.")]
public partial class PopoverWindow : Window
{
    private readonly DispatcherTimer _hoverDelay = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private FrameworkElement? _pendingRow;

    public PopoverWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            Native.MakeToolWindow(this, noActivate: false);
            ApplyBackdrop();
        };
        Deactivated += (_, _) =>
        {
            if (!Pinned)
            {
                HidePopover();
            }
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                HidePopover();
            }
        };
        _hoverDelay.Tick += (_, _) =>
        {
            _hoverDelay.Stop();
            if (_pendingRow is { Tag: Tool tool } row)
            {
                RowHovered?.Invoke(tool, row);
            }
        };
    }

    /// <summary>Raised 150 ms after the pointer rests on a tool row (or at once on keyboard focus).</summary>
    public event Action<Tool, FrameworkElement>? RowHovered;

    public event Action? SettingsRequested;

    /// <summary>Raised when the pointer or focus leaves a row, or the popover hides.</summary>
    public event Action? RowLeft;

    /// <summary>Keeps the popover open when it loses focus (used for verification screenshots).</summary>
    public bool Pinned { get; set; }

    public bool Dark { get; set; }

    public WindowMaterial Material { get; set; } = WindowMaterial.Acrylic;

    public void ApplyBackdrop()
    {
        var supported = Native.ApplyBackdrop(this, Dark, Material);
        Surface.Background = (Brush)FindResource(supported ? "PopBg" : "PopSolid");
    }

    /// <summary>Opens above the tray, right edge near the pointer, inside the work area.</summary>
    public void ShowNear(Point cursorPixels)
    {
        Show();
        UpdateLayout();
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var area = SystemParameters.WorkArea;
        var x = (cursorPixels.X / scale) - (Width / 2);
        Left = Math.Clamp(x, area.Left + 12, area.Right - Width - 12);
        Top = area.Bottom - ActualHeight - 12;
        Activate();
    }

    public void HidePopover()
    {
        _hoverDelay.Stop();
        RowLeft?.Invoke();
        Hide();
    }

    private void Row_MouseEnter(object sender, MouseEventArgs e)
    {
        _pendingRow = (FrameworkElement)sender;
        _hoverDelay.Stop();
        _hoverDelay.Start();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        SettingsRequested?.Invoke();
        e.Handled = true;
    }

    private void Row_MouseLeave(object sender, MouseEventArgs e)
    {
        _hoverDelay.Stop();
        _pendingRow = null;
        RowLeft?.Invoke();
    }

    private void Row_GotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: Tool tool } row)
        {
            RowHovered?.Invoke(tool, row);
        }
    }

    private void Row_LostFocus(object sender, KeyboardFocusChangedEventArgs e) => RowLeft?.Invoke();
}

/// <summary>Collapsed for null, empty text and a count of 0.</summary>
[ExcludeFromCodeCoverage(Justification = "XAML converter.")]
public sealed class NullToCollapsedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null || (value is string s && s.Length == 0) || (value is int n && n == 0) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Binds a radio button to one value of <c>SelectedPeriod</c>.</summary>
[ExcludeFromCodeCoverage(Justification = "XAML converter.")]
public sealed class PeriodConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => Equals(value, parameter);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? parameter! : Binding.DoNothing;
}

/// <summary>The thin usage bar under a row, filled in the tool's color.</summary>
[ExcludeFromCodeCoverage(Justification = "WPF control; checked by screenshots.")]
public sealed class BarFill : FrameworkElement
{
    public static readonly DependencyProperty FractionProperty = DependencyProperty.Register(
        nameof(Fraction), typeof(double), typeof(BarFill), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty KeyProperty = DependencyProperty.Register(
        nameof(Key), typeof(string), typeof(BarFill), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((BarFill)d).OnKeyChanged()));

    private static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        "Fill", typeof(Brush), typeof(BarFill), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly DependencyProperty TrackProperty = DependencyProperty.Register(
        "Track", typeof(Brush), typeof(BarFill), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

    public BarFill() => SetResourceReference(TrackProperty, "RowBg");

    public double Fraction
    {
        get => (double)GetValue(FractionProperty);
        set => SetValue(FractionProperty, value);
    }

    public string? Key
    {
        get => (string?)GetValue(KeyProperty);
        set => SetValue(KeyProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var radius = ActualHeight / 2;
        dc.DrawRoundedRectangle((Brush)GetValue(TrackProperty), null, new Rect(0, 0, ActualWidth, ActualHeight), radius, radius);
        var width = ActualWidth * Math.Clamp(Fraction, 0, 1);
        if (width > 0)
        {
            dc.DrawRoundedRectangle((Brush)GetValue(FillProperty), null, new Rect(0, 0, Math.Max(width, ActualHeight), ActualHeight), radius, radius);
        }
    }

    private void OnKeyChanged()
    {
        if (Logos.FromKey(Key) is { } tool)
        {
            SetResourceReference(FillProperty, tool.ToString());
        }
    }
}
