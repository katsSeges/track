using Track.Core.Settings;
using Track.Core.Tray;

namespace Track.Core.Tests.Display;

public class TrayIconModelTests
{
    private static readonly HashSet<Tool> All = [Tool.Claude, Tool.Codex, Tool.Copilot];
    private static readonly HashSet<Tool> None = [];

    private static TrayIconSpec Build(
        IReadOnlySet<Tool> inTray,
        IReadOnlySet<Tool> running,
        IconLayout layout = IconLayout.Triangle,
        IdleLook idle = IdleLook.GreyMark,
        NothingRunningLook nothing = NothingRunningLook.AllGrey,
        TaskbarTheme theme = TaskbarTheme.Light) =>
        TrayIconModel.Build(inTray, running, layout, idle, nothing, theme);

    private static (double X, double Y, double Size)[] Slots(TrayIconSpec spec) => spec.Shapes.Select(s => (s.X, s.Y, s.Size)).ToArray();

    [Fact]
    public void Three_tools_sit_in_a_triangle_with_running_ones_in_color()
    {
        var spec = Build(All, new HashSet<Tool> { Tool.Claude });

        Assert.Equal([(8, 4.1, 7.8), (3.9, 11.9, 7.8), (12.1, 11.9, 7.8)], Slots(spec));
        Assert.Equal([Tool.Claude, Tool.Codex, Tool.Copilot], spec.Shapes.Select(s => s.Tool!.Value));
        Assert.Equal(("#D97757", 1.0), (spec.Shapes[0].Color, spec.Shapes[0].Opacity));
        Assert.All(spec.Shapes.Skip(1), s => Assert.Equal((ShapeKind.Logo, "#222222", TrayIconModel.IdleOpacity), (s.Kind, s.Color, s.Opacity)));
        Assert.Equal("Track · Claude Code running", spec.Tooltip);
    }

    [Fact]
    public void Row_layout_puts_three_tools_side_by_side()
    {
        Assert.Equal([(2.7, 8, 5.6), (8, 8, 5.6), (13.3, 8, 5.6)], Slots(Build(All, None, IconLayout.Row)));
    }

    [Theory]
    [InlineData(IconLayout.Triangle)]
    [InlineData(IconLayout.Row)]
    public void Fewer_tools_in_the_tray_get_bigger_marks(IconLayout layout)
    {
        Assert.Equal([(8, 8, 14.5)], Slots(Build(new HashSet<Tool> { Tool.Codex }, None, layout)));
        Assert.Equal([(4.2, 8, 8), (11.8, 8, 8)], Slots(Build(new HashSet<Tool> { Tool.Claude, Tool.Copilot }, None, layout)));
    }

    [Fact]
    public void Running_only_shows_just_the_running_tools()
    {
        var spec = Build(All, new HashSet<Tool> { Tool.Copilot, Tool.Codex }, IconLayout.RunningOnly);

        Assert.Equal([Tool.Codex, Tool.Copilot], spec.Shapes.Select(s => s.Tool!.Value));
        Assert.Equal([(4.2, 8, 8), (11.8, 8, 8)], Slots(spec));
        Assert.Equal("Track · Codex, Copilot running", spec.Tooltip);
    }

    [Fact]
    public void Running_only_with_nothing_running_falls_back_to_the_nothing_running_look()
    {
        var grey = Build(All, None, IconLayout.RunningOnly);
        var bars = Build(All, None, IconLayout.RunningOnly, nothing: NothingRunningLook.TrackMark);

        Assert.Equal(3, grey.Shapes.Count);
        Assert.All(grey.Shapes, s => Assert.Equal(TrayIconModel.IdleOpacity, s.Opacity));
        Assert.Equal(ShapeKind.TrackBars, Assert.Single(bars.Shapes).Kind);
        Assert.Equal("Track · nothing running", bars.Tooltip);
    }

    [Fact]
    public void No_tools_in_the_tray_shows_the_track_mark()
    {
        var spec = Build(None, new HashSet<Tool> { Tool.Claude });

        Assert.Equal(new IconShape(ShapeKind.TrackBars, null, 8, 8, 12, "#222222", 0.55), Assert.Single(spec.Shapes));
    }

    [Fact]
    public void Idle_tools_can_be_dots_or_rings()
    {
        var dots = Build(All, None, idle: IdleLook.Dot, theme: TaskbarTheme.Dark);
        var rings = Build(All, None, idle: IdleLook.Ring);

        Assert.All(dots.Shapes, s => Assert.Equal((ShapeKind.Dot, 2.5, "#E6E6E6", 0.45), (s.Kind, s.Size, s.Color, s.Opacity)));
        Assert.All(rings.Shapes, s => Assert.Equal((ShapeKind.Ring, 4.68, 0.5), (s.Kind, s.Size, s.Opacity)));
    }

    [Theory]
    [InlineData(Tool.Claude, TaskbarTheme.Light, "#D97757")]
    [InlineData(Tool.Claude, TaskbarTheme.Dark, "#E08560")]
    [InlineData(Tool.Codex, TaskbarTheme.Light, "#2B2B2E")]
    [InlineData(Tool.Codex, TaskbarTheme.Dark, "#E4E4EA")]
    [InlineData(Tool.Copilot, TaskbarTheme.Light, "#7A4FD6")]
    [InlineData(Tool.Copilot, TaskbarTheme.Dark, "#A98BFF")]
    public void Brand_colors_follow_the_mockup_for_each_taskbar_theme(Tool tool, TaskbarTheme theme, string color)
    {
        Assert.Equal(color, TrayIconModel.BrandColor(tool, theme));
    }

    [Fact]
    public void Tooltip_names_every_running_tool()
    {
        Assert.Equal("Track · Claude Code, Codex, Copilot running", Build(All, All).Tooltip);
    }

    [Fact]
    public void Every_combination_draws_only_tray_tools_with_running_ones_in_brand_color()
    {
        var subsets = Enumerable.Range(0, 8)
            .Select(mask => All.Where(t => (mask & (1 << (int)t)) != 0).ToHashSet())
            .ToList();
        var keys = new Dictionary<string, string>();

        foreach (var running in subsets)
        {
            foreach (var layout in Enum.GetValues<IconLayout>())
            {
                foreach (var idle in Enum.GetValues<IdleLook>())
                {
                    foreach (var nothing in Enum.GetValues<NothingRunningLook>())
                    {
                        foreach (var theme in Enum.GetValues<TaskbarTheme>())
                        {
                            var spec = Build(All, running, layout, idle, nothing, theme);
                            var again = Build(All, running, layout, idle, nothing, theme);
                            Assert.Equal(spec.CacheKey, again.CacheKey);

                            var colored = spec.Shapes.Where(s => s.Opacity == 1).Select(s => s.Tool!.Value).ToHashSet();
                            Assert.True(colored.SetEquals(running), $"{layout}/{idle}/{nothing}/{theme}");
                            Assert.All(spec.Shapes, s => Assert.InRange(s.X, 0, 16));

                            var drawn = string.Join(";", spec.Shapes);
                            Assert.True(!keys.TryGetValue(spec.CacheKey, out var seen) || seen == drawn);
                            keys[spec.CacheKey] = drawn;
                        }
                    }
                }
            }
        }

        Assert.NotEqual(
            Build(All, None, theme: TaskbarTheme.Light).CacheKey,
            Build(All, None, theme: TaskbarTheme.Dark).CacheKey);
    }
}
