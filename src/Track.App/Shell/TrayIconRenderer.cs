using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Track.Core.Tray;
using Media = System.Windows.Media;

namespace Track.App.Shell;

/// <summary>
/// Turns a <see cref="TrayIconSpec"/> (pure data from Track.Core) into a tray icon at the screen's
/// pixel size. Icons are cached by <see cref="TrayIconSpec.CacheKey"/> and size, so the 5-second
/// live check only redraws when something visible changed.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "WPF/GDI drawing; the layout it draws is tested in TrayIconModel, the look by screenshots.")]
public sealed class TrayIconRenderer : IDisposable
{
    private readonly Dictionary<string, System.Drawing.Icon> _cache = [];

    /// <summary>Tray icon size in pixels for the current scaling (16 at 100 %, 24 at 150 %, 32 at 200 %).</summary>
    public static int PixelSize() => (int)Math.Round(16 * Native.SystemDpiScale());

    /// <summary>
    /// A new icon for the tray. Always a fresh copy: H.NotifyIcon disposes the previous icon when a new one
    /// is set, so handing it a cached instance twice crashed the app (found 2026-11-18 when a state came back).
    /// </summary>
    public System.Drawing.Icon Icon(TrayIconSpec spec, int size)
    {
        var key = $"{spec.CacheKey}@{size}";
        if (!_cache.TryGetValue(key, out var icon))
        {
            using var png = new MemoryStream(Png(spec, size));
            using var bitmap = new System.Drawing.Bitmap(png);
            var handle = bitmap.GetHicon();
            icon = (System.Drawing.Icon)System.Drawing.Icon.FromHandle(handle).Clone();
            Native.DestroyIconHandle(handle);
            _cache[key] = icon;
        }

        return (System.Drawing.Icon)icon.Clone();
    }

    /// <summary>The icon as PNG bytes (also used to save verification images).</summary>
    public static byte[] Png(TrayIconSpec spec, int size)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(size / 16.0, size / 16.0));
            foreach (var shape in spec.Shapes)
            {
                Draw(dc, shape);
            }

            dc.Pop();
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    public void Dispose()
    {
        foreach (var icon in _cache.Values)
        {
            icon.Dispose();
        }

        _cache.Clear();
    }

    private static void Draw(DrawingContext dc, IconShape shape)
    {
        var brush = new SolidColorBrush((Media.Color)ColorConverter.ConvertFromString(shape.Color)) { Opacity = shape.Opacity };
        brush.Freeze();
        switch (shape.Kind)
        {
            case ShapeKind.TrackBars:
                dc.DrawGeometry(brush, null, Logos.TrackBars);
                break;
            case ShapeKind.Dot:
                dc.DrawEllipse(brush, null, new Point(shape.X, shape.Y), shape.Size / 2, shape.Size / 2);
                break;
            case ShapeKind.Ring:
                dc.DrawEllipse(null, new Media.Pen(brush, 1), new Point(shape.X, shape.Y), shape.Size / 2, shape.Size / 2);
                break;
            default:
                var scale = shape.Size / 16;
                dc.PushTransform(new TranslateTransform(shape.X - (shape.Size / 2), shape.Y - (shape.Size / 2)));
                dc.PushTransform(new ScaleTransform(scale, scale));
                dc.DrawGeometry(brush, null, Logos.For(shape.Tool!.Value));
                dc.Pop();
                dc.Pop();
                break;
        }
    }
}
