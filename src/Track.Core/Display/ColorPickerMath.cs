namespace Track.Core.Display;

public readonly record struct HsvColor(double Hue, double Saturation, double Value);
public readonly record struct RgbColor(byte Red, byte Green, byte Blue);

/// <summary>Color parsing and wheel math shared by the Settings color picker and its tests.</summary>
public static class ColorPickerMath
{
    public static string NormalizeHex(string color)
    {
        if (color.Length != 7
            || color[0] != '#'
            || !uint.TryParse(color.AsSpan(1), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out _))
        {
            throw new ArgumentException("Use a six-digit hex color such as #AABBCC.", nameof(color));
        }

        return color.ToUpperInvariant();
    }

    public static HsvColor FromHex(string color)
    {
        var normalized = NormalizeHex(color);
        var red = byte.Parse(normalized.AsSpan(1, 2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture);
        var green = byte.Parse(normalized.AsSpan(3, 2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture);
        var blue = byte.Parse(normalized.AsSpan(5, 2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture);
        return FromRgb(red, green, blue);
    }

    public static HsvColor FromRgb(byte redByte, byte greenByte, byte blueByte)
    {
        var red = redByte / 255d;
        var green = greenByte / 255d;
        var blue = blueByte / 255d;
        var maximum = Math.Max(red, Math.Max(green, blue));
        var minimum = Math.Min(red, Math.Min(green, blue));
        var delta = maximum - minimum;
        var hue = delta == 0
            ? 0
            : maximum == red
                ? 60 * (((green - blue) / delta) % 6)
                : maximum == green
                    ? 60 * (((blue - red) / delta) + 2)
                    : 60 * (((red - green) / delta) + 4);
        if (hue < 0)
        {
            hue += 360;
        }

        return new HsvColor(hue, maximum == 0 ? 0 : delta / maximum, maximum);
    }

    public static string ToHex(HsvColor color)
    {
        var rgb = ToRgb(color);
        return $"#{rgb.Red:X2}{rgb.Green:X2}{rgb.Blue:X2}";
    }

    public static RgbColor ToRgb(HsvColor color)
    {
        ValidateHsv(color);
        var chroma = color.Value * color.Saturation;
        var hueSection = color.Hue / 60;
        var secondary = chroma * (1 - Math.Abs(hueSection % 2 - 1));
        var (red, green, blue) = (int)hueSection switch
        {
            0 => (chroma, secondary, 0d),
            1 => (secondary, chroma, 0d),
            2 => (0d, chroma, secondary),
            3 => (0d, secondary, chroma),
            4 => (secondary, 0d, chroma),
            _ => (chroma, 0d, secondary),
        };
        var match = color.Value - chroma;
        return new RgbColor(Channel(red + match), Channel(green + match), Channel(blue + match));
    }

    public static HsvColor FromWheelPoint(double x, double y, double diameter, double value = 1)
    {
        ValidateDiameter(diameter);
        if (!double.IsFinite(x) || !double.IsFinite(y) || value is < 0 or > 1 || !double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(x), "Wheel coordinates and value must be finite and within range.");
        }

        var radius = diameter / 2;
        var dx = (x - radius) / radius;
        var dy = (y - radius) / radius;
        var saturation = Math.Min(1, Math.Sqrt(dx * dx + dy * dy));
        var hue = Math.Atan2(dy, dx) * 180 / Math.PI;
        return new HsvColor((hue + 360) % 360, saturation, value);
    }

    public static (double X, double Y) ToWheelPoint(HsvColor color, double diameter)
    {
        ValidateDiameter(diameter);
        ValidateHsv(color);
        var angle = color.Hue * Math.PI / 180;
        var radius = diameter / 2 * color.Saturation;
        return (diameter / 2 + Math.Cos(angle) * radius, diameter / 2 + Math.Sin(angle) * radius);
    }

    public static string ContrastTextHex(string backgroundHex)
    {
        var background = NormalizeHex(backgroundHex);
        var black = ContrastRatio("#000000", background);
        var white = ContrastRatio("#FFFFFF", background);
        return black >= white ? "#000000" : "#FFFFFF";
    }

    public static string VisibleAccentHex(string accentHex, string surfaceHex)
    {
        var accent = NormalizeHex(accentHex);
        var surface = NormalizeHex(surfaceHex);
        if (ContrastRatio(accent, surface) >= 3)
        {
            return accent;
        }

        var black = ContrastRatio("#000000", surface);
        var white = ContrastRatio("#FFFFFF", surface);
        return black >= white ? "#000000" : "#FFFFFF";
    }

    public static double ContrastRatio(string foregroundHex, string backgroundHex)
    {
        var foreground = Luminance(NormalizeHex(foregroundHex));
        var background = Luminance(NormalizeHex(backgroundHex));
        var lighter = Math.Max(foreground, background);
        var darker = Math.Min(foreground, background);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double Luminance(string color)
    {
        static double Linear(byte value)
        {
            var channel = value / 255d;
            return channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
        }

        var red = byte.Parse(color.AsSpan(1, 2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture);
        var green = byte.Parse(color.AsSpan(3, 2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture);
        var blue = byte.Parse(color.AsSpan(5, 2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture);
        return 0.2126 * Linear(red) + 0.7152 * Linear(green) + 0.0722 * Linear(blue);
    }

    private static byte Channel(double value) => (byte)Math.Round(Math.Clamp(value, 0, 1) * 255, MidpointRounding.AwayFromZero);

    private static void ValidateDiameter(double diameter)
    {
        if (!double.IsFinite(diameter) || diameter <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(diameter), "Wheel diameter must be greater than zero.");
        }
    }

    private static void ValidateHsv(HsvColor color)
    {
        if (!double.IsFinite(color.Hue) || color.Hue is < 0 or >= 360
            || !double.IsFinite(color.Saturation) || color.Saturation is < 0 or > 1
            || !double.IsFinite(color.Value) || color.Value is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(color), "Hue must be [0,360), saturation and value [0,1].");
        }
    }
}
