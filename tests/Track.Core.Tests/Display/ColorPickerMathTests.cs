using Track.Core.Display;

namespace Track.Core.Tests.Display;

public class ColorPickerMathTests
{
    [Theory]
    [InlineData("#000000")]
    [InlineData("#FFFFFF")]
    [InlineData("#E08560")]
    [InlineData("#7D5BFF")]
    [InlineData("#00AACC")]
    [InlineData("#FF0080")]
    [InlineData("#FFFF00")]
    [InlineData("#00FF00")]
    [InlineData("#00FFFF")]
    [InlineData("#0000FF")]
    [InlineData("#FF00FF")]
    public void Hex_and_hsv_round_trip(string color)
    {
        var hsv = ColorPickerMath.FromHex(color.ToLowerInvariant());

        Assert.Equal(color, ColorPickerMath.NormalizeHex(color.ToLowerInvariant()));
        Assert.Equal(color, ColorPickerMath.ToHex(hsv));
    }

    [Fact]
    public void Wheel_maps_center_and_edge_to_saturation_and_hue()
    {
        Assert.Equal(new HsvColor(0, 0, 0.6), ColorPickerMath.FromWheelPoint(50, 50, 100, 0.6));
        Assert.Equal(new HsvColor(0, 1, 1), ColorPickerMath.FromWheelPoint(100, 50, 100));
        Assert.Equal(270, ColorPickerMath.FromWheelPoint(50, 0, 100).Hue);

        var point = ColorPickerMath.ToWheelPoint(new HsvColor(210, 0.5, 0.8), 100);
        var returned = ColorPickerMath.FromWheelPoint(point.X, point.Y, 100, 0.8);

        Assert.InRange(Math.Abs(returned.Hue - 210), 0, 1e-10);
        Assert.InRange(Math.Abs(returned.Saturation - 0.5), 0, 1e-10);
        Assert.Equal(0.8, returned.Value);
    }

    [Theory]
    [InlineData("#000000", "#FFFFFF")]
    [InlineData("#FFFFFF", "#000000")]
    [InlineData("#4B4CC4", "#FFFFFF")]
    [InlineData("#0A66D8", "#FFFFFF")]
    public void Contrast_text_chooses_a_wcag_readable_foreground(string background, string expected)
    {
        var foreground = ColorPickerMath.ContrastTextHex(background);

        Assert.Equal(expected, foreground);
        Assert.True(ColorPickerMath.ContrastRatio(foreground, background) >= 4.5);
    }

    [Theory]
    [InlineData("#4C9BFF", "#28282C", "#4C9BFF")]
    [InlineData("#FDFE00", "#F6F6F8", "#000000")]
    [InlineData("#303036", "#28282C", "#FFFFFF")]
    public void Visible_accent_has_at_least_three_to_one_surface_contrast(string accent, string surface, string expected)
    {
        var visible = ColorPickerMath.VisibleAccentHex(accent, surface);

        Assert.Equal(expected, visible);
        Assert.True(ColorPickerMath.ContrastRatio(visible, surface) >= 3);
    }

    [Theory]
    [InlineData("")]
    [InlineData("#123")]
    [InlineData("123456")]
    [InlineData("#12GG56")]
    public void Invalid_hex_is_rejected(string value) =>
        Assert.Throws<ArgumentException>(() => ColorPickerMath.FromHex(value));

    [Fact]
    public void Wheel_rejects_invalid_dimensions_and_hsv_values()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ColorPickerMath.FromWheelPoint(1, 1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ColorPickerMath.ToWheelPoint(new HsvColor(360, 1, 1), 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => ColorPickerMath.FromWheelPoint(double.NaN, 1, 100));
    }

    [Theory]
    [InlineData(1, double.NaN, 100, 1)]
    [InlineData(1, 1, 100, double.NaN)]
    [InlineData(1, 1, 100, -0.1)]
    [InlineData(1, 1, 100, 1.1)]
    public void Wheel_rejects_invalid_coordinates_and_values(double x, double y, double diameter, double value) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => ColorPickerMath.FromWheelPoint(x, y, diameter, value));

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Wheel_rejects_non_finite_diameters(double diameter) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => ColorPickerMath.FromWheelPoint(1, 1, diameter));

    [Theory]
    [InlineData(double.NaN, 0, 0)]
    [InlineData(0, double.NaN, 0)]
    [InlineData(0, 0, double.NaN)]
    [InlineData(-1, 0, 0)]
    [InlineData(360, 0, 0)]
    [InlineData(0, -0.1, 0)]
    [InlineData(0, 1.1, 0)]
    [InlineData(0, 0, -0.1)]
    [InlineData(0, 0, 1.1)]
    public void Rgb_conversion_rejects_invalid_hsv(double hue, double saturation, double value) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => ColorPickerMath.ToRgb(new HsvColor(hue, saturation, value)));
}
