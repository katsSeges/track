using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Track.Core.Display;

namespace Track.App.Shell;

/// <summary>
/// The popover of a color well (design canvas "Color well popover"): a hue/saturation wheel, brightness, suggested
/// colors and one hex field. It applies as you pick (debounced) and closes when you click elsewhere; Reset goes back
/// to the provider's own color.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "WPF popover and drawing; HSV, wheel mapping and hex validation are tested in Track.Core.")]
internal sealed class ColorPopover : Popup
{
    private static readonly string[] Suggested = ["#E5484D", "#F5A524", "#30A46C", "#0A66D8", "#7A4FD6", "#D6409F", "#6E6E73"];

    private readonly string _defaultHex;
    private readonly Action<string> _apply;
    private readonly ColorWheel _wheel;
    private readonly Slider _brightness;
    private readonly TextBox _hex;
    private readonly Border _preview;
    private readonly DispatcherTimer _debounce;
    private HsvColor _color;
    private string _applied;
    private bool _syncing;

    public ColorPopover(UIElement target, string title, string hex, string defaultHex, Action<string> apply)
    {
        _defaultHex = ColorPickerMath.NormalizeHex(defaultHex);
        _applied = ColorPickerMath.NormalizeHex(hex);
        _apply = apply;
        _color = ColorPickerMath.FromHex(_applied);
        PlacementTarget = target;
        Placement = PlacementMode.Bottom;
        HorizontalOffset = -130;
        VerticalOffset = 6;
        StaysOpen = false;
        AllowsTransparency = true;
        PopupAnimation = PopupAnimation.Fade;
        _debounce = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => Commit(), Dispatcher.CurrentDispatcher) { IsEnabled = false };
        Closed += (_, _) => Commit();

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var reset = new Button { Content = "Reset", Style = (Style)Application.Current.FindResource("LinkButton") };
        reset.Click += (_, _) => Select(ColorPickerMath.FromHex(_defaultHex));
        DockPanel.SetDock(reset, Dock.Right);
        header.Children.Add(reset);
        header.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 12 });

        _wheel = new ColorWheel(_color) { Width = 168, Height = 168, HorizontalAlignment = HorizontalAlignment.Center };
        _wheel.ColorChanged += color => Select(color with { Value = _color.Value });
        AutomationProperties(_wheel, "Hue and saturation");

        _brightness = new Slider { Minimum = 0, Maximum = 1, Value = _color.Value, VerticalAlignment = VerticalAlignment.Center };
        _brightness.ValueChanged += (_, e) =>
        {
            if (!_syncing)
            {
                Select(_color with { Value = e.NewValue });
            }
        };
        AutomationProperties(_brightness, "Brightness");
        var brightnessRow = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        var brightnessLabel = Muted("Brightness");
        brightnessLabel.Width = 66;
        brightnessRow.Children.Add(brightnessLabel);
        brightnessRow.Children.Add(_brightness);

        var swatches = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        foreach (var swatch in new[] { _defaultHex }.Concat(Suggested.Where(s => s != _defaultHex)).Take(8))
        {
            var well = new Button { Style = (Style)Application.Current.FindResource("ColorWell"), Background = Brush(swatch), Margin = new Thickness(0, 0, 8, 0), ToolTip = swatch == _defaultHex ? "Default" : swatch };
            AutomationProperties(well, swatch == _defaultHex ? $"Default color {swatch}" : $"Color {swatch}");
            well.Click += (_, _) => Select(ColorPickerMath.FromHex(swatch));
            swatches.Children.Add(well);
        }

        _preview = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 10, 0) };
        _preview.SetResourceReference(Border.BorderBrushProperty, "FieldBorder");
        _hex = new TextBox { MaxLength = 7, VerticalContentAlignment = VerticalAlignment.Center };
        AutomationProperties(_hex, "Hex color");
        _hex.LostFocus += (_, _) => ReadHex();
        _hex.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                ReadHex();
                e.Handled = true;
            }
        };
        var hexRow = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        hexRow.Children.Add(_preview);
        hexRow.Children.Add(_hex);

        var body = new StackPanel { Width = 250 };
        body.Children.Add(header);
        body.Children.Add(_wheel);
        body.Children.Add(brightnessRow);
        var suggested = Muted("Suggested");
        suggested.Margin = new Thickness(0, 12, 0, 0);
        body.Children.Add(suggested);
        body.Children.Add(swatches);
        body.Children.Add(hexRow);

        var surface = new Border
        {
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14),
            Margin = new Thickness(12),
            Child = body,
            Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 6, Direction = 270, Opacity = 0.18 },
        };
        surface.SetResourceReference(Border.BackgroundProperty, "PopupBg");
        surface.SetResourceReference(Border.BorderBrushProperty, "FieldBorder");
        surface.SetResourceReference(TextElement.ForegroundProperty, "Fg");
        surface.SetResourceReference(TextElement.FontFamilyProperty, "Sans");
        Child = surface;
        // Escape closes it (the color picked so far stays), like clicking elsewhere.
        surface.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                IsOpen = false;
                e.Handled = true;
            }
        };
        Opened += (_, _) => _wheel.Focus();
        Show(_color);
    }

    /// <summary>The surface, for render mode (a closed popup has no window to capture).</summary>
    public FrameworkElement Surface => (FrameworkElement)Child;

    private void ReadHex()
    {
        try
        {
            Select(ColorPickerMath.FromHex(_hex.Text.Trim()));
            _hex.ClearValue(Control.BorderBrushProperty);
        }
        catch (ArgumentException)
        {
            _hex.SetResourceReference(Control.BorderBrushProperty, "ErrorText");
        }
    }

    private void Select(HsvColor color)
    {
        _color = color;
        Show(color);
        _debounce.Stop();
        _debounce.Start();
    }

    private void Show(HsvColor color)
    {
        _syncing = true;
        _wheel.Color = color;
        _brightness.Value = color.Value;
        var hex = ColorPickerMath.ToHex(color);
        if (!_hex.IsKeyboardFocused)
        {
            _hex.Text = hex;
        }

        _preview.Background = Brush(hex);
        _syncing = false;
    }

    private void Commit()
    {
        _debounce.Stop();
        var hex = ColorPickerMath.ToHex(_color);
        if (hex != _applied)
        {
            _applied = hex;
            _apply(hex);
        }
    }

    private static TextBlock Muted(string text)
    {
        var block = new TextBlock { Text = text, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
        block.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        return block;
    }

    private static void AutomationProperties(DependencyObject element, string name) =>
        System.Windows.Automation.AutomationProperties.SetName(element, name);

    internal static SolidColorBrush Brush(string hex)
    {
        var rgb = ColorPickerMath.ToRgb(ColorPickerMath.FromHex(hex));
        var brush = new SolidColorBrush(Color.FromRgb(rgb.Red, rgb.Green, rgb.Blue));
        brush.Freeze();
        return brush;
    }

    /// <summary>Hue around the circle, saturation from the center; the brightness slider sets the value.</summary>
    internal sealed class ColorWheel : FrameworkElement
    {
        private HsvColor _color;
        private WriteableBitmap? _bitmap;
        private double _bitmapValue = -1;

        public ColorWheel(HsvColor color)
        {
            _color = color;
            Focusable = true;
            Cursor = Cursors.Hand;
            MouseLeftButtonDown += (_, e) =>
            {
                CaptureMouse();
                Select(e.GetPosition(this));
            };
            MouseMove += (_, e) =>
            {
                if (IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed)
                {
                    Select(e.GetPosition(this));
                }
            };
            MouseLeftButtonUp += (_, _) => ReleaseMouseCapture();
        }

        public event Action<HsvColor>? ColorChanged;

        public HsvColor Color
        {
            get => _color;
            set
            {
                if (_color != value)
                {
                    _color = value;
                    InvalidateVisual();
                }
            }
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            var size = Math.Max(1, (int)Math.Min(ActualWidth, ActualHeight));
            // The wheel image only changes with brightness (or size), not with every move of the marker.
            if (_bitmap is null || _bitmap.PixelWidth != size || _bitmapValue != _color.Value)
            {
                _bitmap = CreateBitmap(size, _color.Value);
                _bitmapValue = _color.Value;
            }

            drawingContext.DrawImage(_bitmap, new Rect(0, 0, size, size));
            var point = ColorPickerMath.ToWheelPoint(_color, size);
            var center = new Point(point.X, point.Y);
            drawingContext.DrawEllipse(Brushes.Transparent, new Pen(new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x60, 0, 0, 0)), 3.5), center, 7, 7);
            drawingContext.DrawEllipse(Brushes.Transparent, new Pen(Brushes.White, 2), center, 7, 7);
        }

        private void Select(Point point)
        {
            var size = Math.Min(ActualWidth, ActualHeight);
            var radius = size / 2;
            var dx = point.X - radius;
            var dy = point.Y - radius;
            var distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance > radius && distance > 0)
            {
                // Dragging past the edge keeps the hue at full saturation instead of stopping.
                point = new Point(radius + dx / distance * radius, radius + dy / distance * radius);
            }

            Color = ColorPickerMath.FromWheelPoint(Math.Clamp(point.X, 0, size), Math.Clamp(point.Y, 0, size), size, _color.Value);
            ColorChanged?.Invoke(Color);
        }

        private static WriteableBitmap CreateBitmap(int size, double value)
        {
            var pixels = new byte[size * size * 4];
            var radius = size / 2d;
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var dx = x - radius;
                    var dy = y - radius;
                    if (Math.Sqrt(dx * dx + dy * dy) > radius)
                    {
                        continue;
                    }

                    var rgb = ColorPickerMath.ToRgb(ColorPickerMath.FromWheelPoint(x, y, size, value));
                    var offset = (y * size + x) * 4;
                    pixels[offset] = rgb.Blue;
                    pixels[offset + 1] = rgb.Green;
                    pixels[offset + 2] = rgb.Red;
                    pixels[offset + 3] = 255;
                }
            }

            var bitmap = new WriteableBitmap(size, size, 96, 96, PixelFormats.Pbgra32, null);
            bitmap.WritePixels(new Int32Rect(0, 0, size, size), pixels, size * 4, 0);
            bitmap.Freeze();
            return bitmap;
        }
    }
}
