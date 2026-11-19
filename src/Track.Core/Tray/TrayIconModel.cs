using System.Globalization;
using Track.Core.Live;
using Track.Core.Providers;
using Track.Core.Settings;
using Track.Core.Usage;

namespace Track.Core.Tray;

public enum TaskbarTheme
{
    Light,
    Dark,
}

public enum ShapeKind
{
    /// <summary>A tool's logo. <see cref="IconShape.Color"/> is the brand color when running, the taskbar ink when idle.</summary>
    Logo,

    /// <summary>Small filled circle for an idle tool.</summary>
    Dot,

    /// <summary>Empty circle for an idle tool.</summary>
    Ring,

    /// <summary>Track's own three-bar mark (nothing to show).</summary>
    TrackBars,
}

/// <summary>One thing to draw, in a 16 × 16 unit box. X/Y is the center, Size the width of the mark.</summary>
public sealed record IconShape(ShapeKind Kind, Tool? Tool, double X, double Y, double Size, string Color, double Opacity)
{
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Kind}:{Tool}@{X},{Y}/{Size}{Color}*{Opacity}");
}

/// <summary>What the tray icon shows, ready for a renderer; equal states give equal <see cref="CacheKey"/>.</summary>
public sealed record TrayIconSpec(IReadOnlyList<IconShape> Shapes, string Tooltip, string CacheKey);

/// <summary>
/// The tray icon as data (rounds 2–3): provider marks share three slots. When more than three providers
/// qualify, running providers take priority and the remaining marks follow catalog order.
/// </summary>
public static class TrayIconModel
{
    public const double IdleOpacity = 0.38;
    public const int MaximumMarks = 3;

    private static readonly Tool[] Order = ProviderCatalog.Order.ToArray();

    private static readonly (double X, double Y, double Size)[] Triangle = [(8, 4.1, 7.8), (3.9, 11.9, 7.8), (12.1, 11.9, 7.8)];
    private static readonly (double X, double Y, double Size)[] Row = [(2.7, 8, 5.6), (8, 8, 5.6), (13.3, 8, 5.6)];
    private static readonly (double X, double Y, double Size)[] Pair = [(4.2, 8, 8), (11.8, 8, 8)];
    private static readonly (double X, double Y, double Size)[] Single = [(8, 8, 14.5)];

    public static string BrandColor(Tool tool, TaskbarTheme theme) => ProviderCatalog.Get(tool).Colors.For(theme);

    /// <summary>The taskbar's text color, used for idle marks.</summary>
    public static string Ink(TaskbarTheme theme) => theme == TaskbarTheme.Light ? "#222222" : "#E6E6E6";

    public static IReadOnlySet<Tool> RunningProviders(IReadOnlyDictionary<Tool, LiveStatus> live) =>
        live.Where(pair => ProviderVisibilityPolicy.IsRunning(pair.Key, pair.Value))
            .Select(pair => pair.Key)
            .ToHashSet();

    /// <param name="inTray">Tools that earned a tray spot (visibility rule).</param>
    /// <param name="running">Tools working right now.</param>
    public static TrayIconSpec Build(
        IReadOnlySet<Tool> inTray,
        IReadOnlySet<Tool> running,
        IconLayout layout,
        IdleLook idleLook,
        NothingRunningLook nothingRunning,
        TaskbarTheme theme)
    {
        // "Running only" shows just the running tools; with none running it falls back to the nothing-running look.
        var shown = Order.Where(inTray.Contains).ToList();
        var visibleRunning = shown.Where(running.Contains).ToHashSet();
        var anyRunning = visibleRunning.Count > 0;
        if (layout == IconLayout.RunningOnly && anyRunning)
        {
            shown = shown.Where(visibleRunning.Contains).ToList();
        }

        if (shown.Count > MaximumMarks)
        {
            shown = shown
                .OrderByDescending(visibleRunning.Contains)
                .Take(MaximumMarks)
                .ToList();
        }

        var shapes = shown.Count == 0 || (!anyRunning && nothingRunning == NothingRunningLook.TrackMark)
            ? [new IconShape(ShapeKind.TrackBars, null, 8, 8, 12, Ink(theme), 0.55)]
            : Place(shown, layout).Select(p => Shape(p.Tool, p.Slot, running.Contains(p.Tool), idleLook, theme)).ToList();

        var names = Order.Where(visibleRunning.Contains).Select(ToolNames.Display).ToList();
        var tooltip = "Track · " + (names.Count > 0 ? string.Join(", ", names) + " running" : "nothing running");
        return new TrayIconSpec(shapes, tooltip, $"{theme}|{string.Join(";", shapes)}");
    }

    private static IEnumerable<(Tool Tool, (double X, double Y, double Size) Slot)> Place(List<Tool> shown, IconLayout layout)
    {
        if (shown.Count == 3)
        {
            var slots = layout == IconLayout.Row ? Row : Triangle;
            return shown.Select((t, i) => (t, slots[i]));
        }

        var fewer = shown.Count == 2 ? Pair : Single;
        return shown.Select((t, i) => (t, fewer[i]));
    }

    private static IconShape Shape(Tool tool, (double X, double Y, double Size) slot, bool isRunning, IdleLook idle, TaskbarTheme theme)
    {
        var (x, y, size) = slot;
        if (isRunning)
        {
            return new IconShape(ShapeKind.Logo, tool, x, y, size, BrandColor(tool, theme), 1);
        }

        return idle switch
        {
            IdleLook.Dot => new IconShape(ShapeKind.Dot, tool, x, y, Math.Round(size * 0.32, 2), Ink(theme), 0.45),
            IdleLook.Ring => new IconShape(ShapeKind.Ring, tool, x, y, Math.Round(size * 0.6, 2), Ink(theme), 0.5),
            _ => new IconShape(ShapeKind.Logo, tool, x, y, size, Ink(theme), IdleOpacity),
        };
    }
}
