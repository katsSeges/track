using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Media;
using Track.Core;
using Track.Core.Providers;

namespace Track.App.Shell;

/// <summary>
/// Single-color logo shapes in a 16 × 16 box, so they can be tinted (brand color when running, grey
/// when idle). These are the simplified stand-in marks from <c>design/track-mockup-v2.html</c>; drop
/// official SVGs into <c>Assets/logos/</c> and convert them here to replace them.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "WPF drawing; geometry comes from the mockup and is checked by screenshots.")]
public static class Logos
{
    private static readonly Dictionary<Tool, Geometry> Cache = new()
    {
        [Tool.Claude] = Claude(),
        [Tool.Codex] = Codex(),
        [Tool.Copilot] = Copilot(),
        [Tool.Gemini] = Gemini(),
        [Tool.OpenCode] = OpenCode(),
        [Tool.Cline] = Cline(),
        [Tool.Roo] = Roo(),
        [Tool.Ollama] = Ollama(),
    };

    public static Geometry For(Tool tool) => Cache[tool];

    /// <summary>Track's own three-bar mark.</summary>
    public static Geometry TrackBars { get; } = Freeze(new GeometryGroup
    {
        Children =
        {
            new RectangleGeometry(new Rect(2, 9, 2.6, 5), 1, 1),
            new RectangleGeometry(new Rect(6.7, 5, 2.6, 9), 1, 1),
            new RectangleGeometry(new Rect(11.4, 2, 2.6, 12), 1, 1),
        },
    });

    public static Tool? FromKey(string? key) => ProviderCatalog.All
        .FirstOrDefault(provider => string.Equals(provider.LogoKey, key, StringComparison.Ordinal))
        ?.Id;

    private static Geometry Claude()
    {
        // Twelve rays of three lengths, like a starburst.
        var rays = new GeometryGroup();
        for (var a = 0; a < 12; a++)
        {
            var t = (a * Math.PI / 6) + 0.13;
            var length = a % 3 == 0 ? 7.2 : a % 2 == 1 ? 5.4 : 6.3;
            rays.Children.Add(new LineGeometry(new Point(8, 8), new Point(8 + (Math.Cos(t) * length), 8 + (Math.Sin(t) * length))));
        }

        return Freeze(rays.GetWidenedPathGeometry(RoundPen(1.9)));
    }

    private static Geometry Codex()
    {
        // A rounded terminal window with a ">_" prompt cut out of it.
        var body = new RectangleGeometry(new Rect(1.2, 2.2, 13.6, 11.6), 3.2, 3.2);
        var prompt = Geometry.Parse("M4.4,6 L6.8,8 L4.4,10 M8.6,10.3 L11.6,10.3").GetWidenedPathGeometry(RoundPen(1.6));
        return Freeze(new CombinedGeometry(GeometryCombineMode.Exclude, body, prompt).GetFlattenedPathGeometry());
    }

    private static Geometry Copilot()
    {
        // A rounded head with goggles: eyes and mouth cut out.
        var head = Geometry.Parse(
            "M3,6.2 C3,3.6 5,2 8,2 S13,3.6 13,6.2 V7.5 C14,7.8 14.6,8.6 14.6,9.6 V10.8 C14.6,11.7 14.1,12.4 13.3,12.8 " +
            "C12,14.4 10.3,15 8,15 S4,14.4 2.7,12.8 C1.9,12.4 1.4,11.7 1.4,10.8 V9.6 C1.4,8.6 2,7.8 3,7.5 Z");
        var holes = new GeometryGroup
        {
            Children =
            {
                new RectangleGeometry(new Rect(4.1, 5, 3.4, 3), 1.3, 1.3),
                new RectangleGeometry(new Rect(8.5, 5, 3.4, 3), 1.3, 1.3),
                new RectangleGeometry(new Rect(5.6, 10.2, 1.3, 2.2), 0.6, 0.6),
                new RectangleGeometry(new Rect(9.1, 10.2, 1.3, 2.2), 0.6, 0.6),
            },
        };
        return Freeze(new CombinedGeometry(GeometryCombineMode.Exclude, head, holes).GetFlattenedPathGeometry());
    }

    private static Geometry Gemini() =>
        Freeze(Geometry.Parse("M8,1.2 L9.5,6.5 L14.8,8 L9.5,9.5 L8,14.8 L6.5,9.5 L1.2,8 L6.5,6.5 Z"));

    private static Geometry OpenCode()
    {
        var frame = new RectangleGeometry(new Rect(1.5, 2, 13, 12), 2, 2);
        var prompt = Geometry.Parse("M6,5 L3.5,8 L6,11 M10,5 L12.5,8 L10,11")
            .GetWidenedPathGeometry(RoundPen(1.2));
        return Freeze(new CombinedGeometry(GeometryCombineMode.Exclude, frame, prompt).GetFlattenedPathGeometry());
    }

    private static Geometry Cline() =>
        Freeze(Geometry.Parse("M12.5,3.8 C6.4,1.5 2,4.2 2,8 C2,11.8 6.4,14.5 12.5,12.2")
            .GetWidenedPathGeometry(RoundPen(1.8)));

    private static Geometry Roo()
    {
        var body = new RectangleGeometry(new Rect(2, 2, 12, 12), 3, 3);
        var ears = Geometry.Parse("M4,2 L3,0.8 M12,2 L13,0.8").GetWidenedPathGeometry(RoundPen(1.2));
        var face = Geometry.Parse("M5,6 L5,7 M11,6 L11,7 M6,10 Q8,11.5 10,10").GetWidenedPathGeometry(RoundPen(1));
        return Freeze(new GeometryGroup { Children = { body, ears, face } });
    }

    private static Geometry Ollama()
    {
        var outer = new EllipseGeometry(new Point(8, 8), 6.5, 6.5);
        var inner = new EllipseGeometry(new Point(8, 8), 3.2, 3.2);
        var ring = new CombinedGeometry(GeometryCombineMode.Exclude, outer, inner);
        var nodes = new GeometryGroup
        {
            Children =
            {
                new EllipseGeometry(new Point(8, 8), 1.1, 1.1),
                new EllipseGeometry(new Point(8, 3.2), 1.1, 1.1),
                new EllipseGeometry(new Point(12.2, 10.4), 1.1, 1.1),
                new EllipseGeometry(new Point(3.8, 10.4), 1.1, 1.1),
            },
        };
        return Freeze(new GeometryGroup { Children = { ring, nodes } });
    }

    private static Pen RoundPen(double width) => new(Brushes.Black, width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };

    private static T Freeze<T>(T geometry)
        where T : Geometry
    {
        geometry.Freeze();
        return geometry;
    }
}

/// <summary>Draws a tool logo in its theme color when <see cref="Active"/>, in the muted color otherwise.</summary>
[ExcludeFromCodeCoverage(Justification = "WPF control; checked by screenshots.")]
public sealed class LogoIcon : FrameworkElement
{
    public static readonly DependencyProperty KeyProperty = DependencyProperty.Register(
        nameof(Key), typeof(string), typeof(LogoIcon), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, (d, e) => ((LogoIcon)d).OnKeyChanged()));

    public static readonly DependencyProperty ActiveProperty = DependencyProperty.Register(
        nameof(Active), typeof(bool), typeof(LogoIcon), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly DependencyProperty BrandProperty = DependencyProperty.Register(
        "Brand", typeof(Brush), typeof(LogoIcon), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly DependencyProperty MutedProperty = DependencyProperty.Register(
        "MutedInk", typeof(Brush), typeof(LogoIcon), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public LogoIcon()
    {
        SetResourceReference(MutedProperty, "Muted");
    }

    public string? Key
    {
        get => (string?)GetValue(KeyProperty);
        set => SetValue(KeyProperty, value);
    }

    public bool Active
    {
        get => (bool)GetValue(ActiveProperty);
        set => SetValue(ActiveProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (Logos.FromKey(Key) is not { } tool || ActualWidth <= 0)
        {
            return;
        }

        var scale = Math.Min(ActualWidth, ActualHeight) / 16;
        dc.PushTransform(new ScaleTransform(scale, scale));
        dc.PushOpacity(Active ? 1 : 0.6);
        dc.DrawGeometry((Brush)GetValue(Active ? BrandProperty : MutedProperty), null, Logos.For(tool));
        dc.Pop();
        dc.Pop();
    }

    private void OnKeyChanged()
    {
        if (Logos.FromKey(Key) is { } tool)
        {
            // Theme brushes are named after the tool; a DynamicResource reference follows theme swaps.
            SetResourceReference(BrandProperty, tool.ToString());
        }
    }
}
