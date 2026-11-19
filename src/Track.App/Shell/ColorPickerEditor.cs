using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Track.Core.Display;

namespace Track.App.Shell;

[ExcludeFromCodeCoverage(Justification = "WPF color picker interaction and drawing; HSV, wheel mapping, contrast and validation are covered in Track.Core.")]
internal sealed class ColorPickerEditor : UserControl
{
    private readonly string _defaultColor;
    private readonly Action<string> _applyColor;
    private readonly Button _openButton;
    private readonly Popup _popup;
    private readonly Border _popupSurface;
    private readonly ColorWheel _wheel;
    private readonly Slider _brightness;
    private readonly TextBox _hex;
    private readonly Border _swatch;
    private readonly TextBlock _previewText;
    private readonly TextBlock _error;
    private HsvColor _selected;
    private bool _syncing;

    public ColorPickerEditor(string label, string color, string defaultColor, Action<string> applyColor, bool showLabel = true)
    {
        _defaultColor = ColorPickerMath.NormalizeHex(defaultColor);
        _applyColor = applyColor;
        _selected = ColorPickerMath.FromHex(color);
        var title = new TextBlock { Text = label, FontSize = 11, Margin = new Thickness(0, 0, 0, 4) };
        title.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        _openButton = new Button { Padding = new Thickness(8, 5, 10, 5), HorizontalContentAlignment = HorizontalAlignment.Left };
        var content = new StackPanel
        {
            Children = { _openButton },
        };
        if (showLabel)
        {
            content.Children.Insert(0, title);
        }

        Content = content;
        _popup = new Popup
        {
            Placement = PlacementMode.Bottom,
            AllowsTransparency = true,
            StaysOpen = true,
            PopupAnimation = PopupAnimation.Fade,
            PlacementTarget = _openButton,
        };
        _openButton.Click += (_, _) => _popup.IsOpen = true;

        _wheel = new ColorWheel(_selected);
        _wheel.ColorChanged += colorValue =>
        {
            _selected = colorValue;
            RefreshPicker();
        };
        _brightness = new Slider { Minimum = 0, Maximum = 1, Value = _selected.Value, Margin = new Thickness(0, 2, 0, 8), Width = 200 };
        _brightness.ValueChanged += (_, _) =>
        {
            if (!_syncing)
            {
                _selected = _selected with { Value = _brightness.Value };
                _wheel.Color = _selected;
                RefreshPicker();
            }
        };
        _hex = new TextBox { Width = 108, MaxLength = 7, Text = ColorPickerMath.ToHex(_selected) };
        _hex.TextChanged += (_, _) => HexChanged();
        _swatch = new Border { Width = 46, Height = 30, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 8, 0) };
        _swatch.SetResourceReference(Border.BorderBrushProperty, "ControlBorder");
        _previewText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
        _previewText.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
        _error = new TextBlock { FontSize = 10, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        _error.SetResourceReference(TextBlock.ForegroundProperty, "ErrorText");

        _popupSurface = BuildPopup();
        _popup.Child = _popupSurface;
        _popup.Closed += (_, _) => RefreshButton();
        RefreshPicker();
        RefreshButton();
    }

    public void OpenForRender(string selectedColor)
    {
        _selected = ColorPickerMath.FromHex(selectedColor);
        _wheel.Color = _selected;
        RefreshPicker();
        _popup.IsOpen = true;
        UpdateLayout();
        _popupSurface.UpdateLayout();
    }

    public void SavePopupForRender(string path, string selectedColor)
    {
        OpenForRender(selectedColor);
        _popupSurface.Measure(new Size(560, double.PositiveInfinity));
        _popupSurface.Arrange(new Rect(new Point(0, 0), _popupSurface.DesiredSize));
        _popupSurface.UpdateLayout();
        SaveElement(_popupSurface, path);
    }

    private Border BuildPopup()
    {
        var border = new Border
        {
            Width = 520,
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
        };
        border.SetResourceReference(Border.BackgroundProperty, "SurfaceElevated");
        border.SetResourceReference(Border.BorderBrushProperty, "ControlBorder");

        var title = new TextBlock { Text = "Choose a color", FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) };
        title.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
        var right = new StackPanel { Margin = new Thickness(18, 0, 0, 0) };
        right.Children.Add(title);
        var wheelLabel = new TextBlock { Text = "Hue and saturation", FontSize = 11, Margin = new Thickness(0, 0, 0, 6) };
        wheelLabel.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        right.Children.Add(wheelLabel);
        var brightnessLabel = new TextBlock { Text = "Brightness", FontSize = 11, Margin = new Thickness(0, 12, 0, 0) };
        brightnessLabel.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
        right.Children.Add(brightnessLabel);
        right.Children.Add(_brightness);
        var hexLabel = new TextBlock { Text = "Hex value", FontSize = 11, Margin = new Thickness(0, 4, 0, 4) };
        hexLabel.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        right.Children.Add(hexLabel);
        var preview = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 4) };
        preview.Children.Add(_swatch);
        preview.Children.Add(_previewText);
        right.Children.Add(preview);
        right.Children.Add(_hex);
        right.Children.Add(_error);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0),
        };
        var reset = new Button { Content = "Reset to default", Margin = new Thickness(0, 0, 8, 0) };
        reset.Click += (_, _) =>
        {
            _selected = ColorPickerMath.FromHex(_defaultColor);
            _wheel.Color = _selected;
            RefreshPicker();
        };
        var cancel = new Button { Content = "Cancel", Margin = new Thickness(0, 0, 8, 0) };
        cancel.Click += (_, _) => _popup.IsOpen = false;
        var apply = new Button { Content = "Apply", MinWidth = 72 };
        apply.SetResourceReference(FrameworkElement.StyleProperty, "TrackPrimaryButton");
        apply.Click += (_, _) =>
        {
            try
            {
                var normalized = ColorPickerMath.NormalizeHex(_hex.Text);
                _popup.IsOpen = false;
                _applyColor(normalized);
                RefreshButton();
            }
            catch (ArgumentException exception)
            {
                _error.Text = exception.Message;
            }
        };
        actions.Children.Add(reset);
        actions.Children.Add(cancel);
        actions.Children.Add(apply);
        right.Children.Add(actions);

        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        layout.ColumnDefinitions.Add(new ColumnDefinition());
        layout.Children.Add(_wheel);
        Grid.SetColumn(right, 1);
        layout.Children.Add(right);
        border.Child = layout;
        return border;
    }

    private void HexChanged()
    {
        if (_syncing)
        {
            return;
        }

        try
        {
            _selected = ColorPickerMath.FromHex(_hex.Text);
            _wheel.Color = _selected;
            _syncing = true;
            _brightness.Value = _selected.Value;
            _syncing = false;
            _error.Text = "";
            RefreshPicker(updateHex: false);
        }
        catch (ArgumentException)
        {
            _error.Text = "Enter a six-digit hex color such as #AABBCC.";
        }
    }

    private void RefreshPicker(bool updateHex = true)
    {
        _syncing = true;
        if (updateHex)
        {
            _hex.Text = ColorPickerMath.ToHex(_selected);
        }

        _brightness.Value = _selected.Value;
        var full = ColorPickerMath.ToRgb(_selected with { Value = 1 });
        _brightness.Background = new LinearGradientBrush(
            Colors.Black,
            Color.FromRgb(full.Red, full.Green, full.Blue),
            new Point(0, 0.5),
            new Point(1, 0.5));
        _swatch.Background = new SolidColorBrush(ToMediaColor(_selected));
        _previewText.Text = ColorPickerMath.ToHex(_selected);
        _error.Text = "";
        _syncing = false;
        _wheel.Color = _selected;
        RefreshButton();
    }

    private void RefreshButton()
    {
        if (_openButton.Content is not StackPanel row)
        {
            row = new StackPanel { Orientation = Orientation.Horizontal };
            _openButton.Content = row;
        }

        row.Children.Clear();
        var swatch = new Border
        {
            Width = 20,
            Height = 20,
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(ToMediaColor(_selected)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 9, 0),
        };
        swatch.SetResourceReference(Border.BorderBrushProperty, "ControlBorder");
        row.Children.Add(swatch);
        var code = new TextBlock { Text = ColorPickerMath.ToHex(_selected), VerticalAlignment = VerticalAlignment.Center, FontSize = 11 };
        code.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
        row.Children.Add(code);
        _openButton.ToolTip = $"Choose {code.Text}";
    }

    private static Color ToMediaColor(HsvColor color)
    {
        var rgb = ColorPickerMath.ToRgb(color);
        return Color.FromRgb(rgb.Red, rgb.Green, rgb.Blue);
    }

    private static void SaveElement(FrameworkElement element, string path)
    {
        var dpi = VisualTreeHelper.GetDpi(element).DpiScaleX;
        var width = Math.Max(1, (int)Math.Ceiling(element.ActualWidth * dpi));
        var height = Math.Max(1, (int)Math.Ceiling(element.ActualHeight * dpi));
        var bitmap = new RenderTargetBitmap(width, height, 96 * dpi, 96 * dpi, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path);
        encoder.Save(output);
    }

    private sealed class ColorWheel : FrameworkElement
    {
        private HsvColor _color;
        private WriteableBitmap? _bitmap;

        public ColorWheel(HsvColor color)
        {
            _color = color;
            Width = 180;
            Height = 180;
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
            LostMouseCapture += (_, _) => ReleaseMouseCapture();
        }

        public event Action<HsvColor>? ColorChanged;

        public HsvColor Color
        {
            get => _color;
            set
            {
                if (_color == value)
                {
                    return;
                }

                _color = value;
                _bitmap = null;
                InvalidateVisual();
            }
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);
            var size = Math.Max(1, (int)Math.Min(ActualWidth, ActualHeight));
            if (_bitmap is null || _bitmap.PixelWidth != size || _bitmap.PixelHeight != size)
            {
                _bitmap = CreateBitmap(size, _color.Value);
            }

            drawingContext.DrawImage(_bitmap, new Rect(0, 0, size, size));
            var point = ColorPickerMath.ToWheelPoint(_color, size);
            var center = new Point(point.X, point.Y);
            drawingContext.DrawEllipse(Brushes.Transparent, new Pen(Brushes.Black, 3), center, 7, 7);
            drawingContext.DrawEllipse(Brushes.Transparent, new Pen(Brushes.White, 1.5), center, 7, 7);
        }

        private void Select(Point point)
        {
            Color = ColorPickerMath.FromWheelPoint(point.X, point.Y, Math.Min(ActualWidth, ActualHeight), _color.Value);
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
                    var distance = Math.Sqrt(dx * dx + dy * dy);
                    if (distance > radius)
                    {
                        continue;
                    }

                    var hsv = ColorPickerMath.FromWheelPoint(x, y, size, value);
                    var rgb = ColorPickerMath.ToRgb(hsv);
                    var offset = (y * size + x) * 4;
                    pixels[offset] = rgb.Blue;
                    pixels[offset + 1] = rgb.Green;
                    pixels[offset + 2] = rgb.Red;
                    pixels[offset + 3] = 255;
                }
            }

            var bitmap = new WriteableBitmap(size, size, 96, 96, PixelFormats.Pbgra32, null);
            bitmap.WritePixels(new Int32Rect(0, 0, size, size), pixels, size * 4, 0);
            return bitmap;
        }
    }
}
