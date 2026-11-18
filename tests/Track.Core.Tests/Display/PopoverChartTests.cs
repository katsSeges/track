using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Time.Testing;
using Track.Core.Display;
using Track.Core.Ingest;
using Track.Core.Parsing;
using Track.Core.Settings;
using Track.Core.Usage;

namespace Track.Core.Tests.Display;

/// <summary>Round 5 in the popover: chart, comparison, drill-down, Hide / Show, and rollover. Every text is hand-computed.</summary>
public class PopoverChartTests
{
    private static readonly TimeZoneInfo Copenhagen = TimeZoneInfo.FindSystemTimeZoneById("Europe/Copenhagen");
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero); // Wed 18 Nov, 13:00 in Copenhagen

    private readonly FakeTimeProvider _time = new(Now);
    private readonly UsageStore _store = new();

    private PopoverViewModel Create(TrackSettings? settings = null)
    {
        var vm = new PopoverViewModel(_store, new UsageAggregator(_store), new PeriodCalculator(_time, Copenhagen), _time, settings ?? new TrackSettings());
        vm.UsageRefreshed();
        return vm;
    }

    private void Add(Tool tool, string model, DateTimeOffset at, long input = 0, long cacheWrite = 0, long cacheRead = 0, long output = 0) =>
        _store.Add([new UsageRecord(tool, "s", model, at, input, cacheWrite, cacheRead, output, 0, Guid.NewGuid().ToString())]);

    /// <summary>Same fixture as PopoverViewModelTests, priced with the bundled LiteLLM list.</summary>
    private void AddFixture()
    {
        Add(Tool.Claude, "claude-opus-5-5", Now.AddHours(-3), input: 1_000_000, cacheWrite: 2_000_000, cacheRead: 18_000_000, output: 400_000); // 10:00 local, 21.4M, $25.60
        Add(Tool.Codex, "gpt-6.1-sol", new DateTimeOffset(2026, 11, 16, 10, 0, 0, TimeSpan.Zero), input: 500_000, cacheRead: 4_500_000, output: 100_000); // Mon, 5.1M, $2.45
        Add(Tool.Codex, "codex-auto-review", new DateTimeOffset(2026, 11, 3, 10, 0, 0, TimeSpan.Zero), input: 920_000); // no price
        Add(Tool.Copilot, "claude-opus-5.5", Now.AddHours(-1), input: 999_499); // 12:00 local, $4.00
        Add(Tool.Copilot, "gpt-6-luna", new DateTimeOffset(2026, 11, 2, 10, 0, 0, TimeSpan.Zero), input: 1_000_000); // $0.10
    }

    /// <summary>Everything the popover's usage part shows, as plain text.</summary>
    private static string Text(PopoverViewModel vm)
    {
        var lines = new List<string> { $"[{vm.PeriodLabel}]" };
        lines.AddRange(vm.Rows.Append(vm.TotalRow).OfType<ToolRowViewModel>().Select(r => $"{r.Name} | {r.TokenText} | {r.SubText}"));
        if (vm.Chart is { } c)
        {
            lines.Add($"chart: {c.Title} | {c.TopText} | axis {string.Join(" ", c.Axis.Select(a => $"{a.Text}@{a.Position.ToString(System.Globalization.CultureInfo.InvariantCulture)}"))}");
            lines.AddRange(c.Bars.Where(b => b.Segments.Count > 0 || b.Current).Select(b =>
                $"bar {b.Index}{(b.Current ? " (now)" : "")}{(b.Selected ? " (selected)" : "")}: {b.Tooltip.Replace("\n", " / ")}"));
            lines.Add($"future bars: {c.Bars.Count(b => b.Future)}");
        }

        lines.Add($"compare: {vm.ComparisonText ?? "-"}");
        return string.Join("\n", lines);
    }

    [Fact]
    public void Snapshot_today()
    {
        AddFixture();

        Assert.Equal(
            """
            [Today]
            Claude Code | 21.4M | $25.60 · in 3.0M · out 400k · cache 18.0M
            Copilot | 999k | $4.00 · in 999k · out 0 · cache 0
            Codex | 0 | $0.00 · in 0 · out 0 · cache 0
            Total | 22.4M | $29.60 · in 4.0M · out 400k · cache 18.0M
            chart: Today by hour | top 25.0M | axis 00@0.5 06@6.5 12@12.5 18@18.5 24@24
            bar 10: 10:00–11:00 · 21.4M tokens · $25.60 / ● Claude Code 21.4M
            bar 12: 12:00–13:00 · 999k tokens · $4.00 / ● Copilot 999k
            bar 13 (now): 13:00–14:00 / no usage
            future bars: 10
            compare: new today
            """.ReplaceLineEndings("\n"),
            Text(Create()));
    }

    [Fact]
    public void Snapshot_week()
    {
        AddFixture();
        var vm = Create();
        vm.SelectedPeriod = Period.Week;

        Assert.Equal(
            """
            [This week]
            Claude Code | 21.4M | $25.60 · in 3.0M · out 400k · cache 18.0M
            Codex | 5.1M | $2.45 · in 500k · out 100k · cache 4.5M
            Copilot | 999k | $4.00 · in 999k · out 0 · cache 0
            Total | 27.5M | $32.05 · in 4.5M · out 500k · cache 22.5M
            chart: This week by day | top 25.0M | axis M@0.5 T@1.5 W@2.5 T@3.5 F@4.5 S@5.5 S@6.5
            bar 0: Mon 16 Nov · 5.1M tokens · $2.45 / ● Codex 5.1M
            bar 2 (now): Wed 18 Nov · 22.4M tokens · $29.60 / ● Claude Code 21.4M / ● Copilot 999k
            future bars: 4
            compare: new this week
            """.ReplaceLineEndings("\n"),
            Text(vm));
    }

    [Fact]
    public void Snapshot_month()
    {
        AddFixture();
        var vm = Create();
        vm.SelectedPeriod = Period.Month;

        Assert.Equal(
            """
            [This month]
            Claude Code | 21.4M | $25.60 · in 3.0M · out 400k · cache 18.0M
            Codex | 6.0M | ≈$2.45 · in 1.4M · out 100k · cache 4.5M
            Copilot | 2.0M | $4.10 · in 2.0M · out 0 · cache 0
            Total | 29.4M | ≈$32.15 · in 6.4M · out 500k · cache 22.5M
            chart: This month by day | top 25.0M | axis 1@0.5 8@7.5 15@14.5 22@21.5 29@28.5
            bar 1: Mon 2 Nov · 1.0M tokens · $0.10 / ● Copilot 1.0M
            bar 2: Tue 3 Nov · 920k tokens · $— / ● Codex 920k
            bar 15: Mon 16 Nov · 5.1M tokens · $2.45 / ● Codex 5.1M
            bar 17 (now): Wed 18 Nov · 22.4M tokens · $29.60 / ● Claude Code 21.4M / ● Copilot 999k
            future bars: 12
            compare: new this month
            """.ReplaceLineEndings("\n"),
            Text(vm));
    }

    [Fact]
    public void Bars_stack_tools_largest_at_the_bottom_on_one_scale()
    {
        AddFixture();
        var vm = Create();
        vm.SelectedPeriod = Period.Week;

        var wednesday = vm.Chart!.Bars[2];

        Assert.Equal([Tool.Claude, Tool.Copilot], wednesday.Segments.Select(s => s.Tool));
        Assert.Equal(0, wednesday.Segments[0].From);
        Assert.Equal(21_400_000 / 25_000_000.0, wednesday.Segments[0].To, 9);
        Assert.Equal(wednesday.Segments[0].To, wednesday.Segments[1].From);
        Assert.Equal(22_399_499 / 25_000_000.0, wednesday.Segments[1].To, 9);
    }

    [Fact]
    public void Comparison_counts_the_previous_period_up_to_the_same_time()
    {
        AddFixture();
        Add(Tool.Claude, "claude-opus-5-5", Now.AddDays(-1).AddHours(-3), output: 1_000_000); // yesterday 10:00
        Add(Tool.Claude, "claude-opus-5-5", Now.AddDays(-1).AddHours(5), output: 9_000_000);  // yesterday 18:00: later than now

        Assert.Equal("+2140 % vs yesterday at this time", Create().ComparisonText); // 22,399,499 vs 1,000,000
    }

    [Fact]
    public void Clicking_a_bar_shows_only_that_day_and_clicking_it_again_goes_back()
    {
        AddFixture();
        var vm = Create();
        vm.SelectedPeriod = Period.Week;

        vm.SelectBucketCommand.Execute(0); // Monday

        Assert.Equal(
            """
            [Mon 16 Nov]
            Codex | 5.1M | $2.45 · in 500k · out 100k · cache 4.5M
            Claude Code | 0 | $0.00 · in 0 · out 0 · cache 0
            Copilot | 0 | $0.00 · in 0 · out 0 · cache 0
            Total | 5.1M | $2.45 · in 500k · out 100k · cache 4.5M
            """.ReplaceLineEndings("\n"),
            string.Join("\n", Text(vm).Split('\n').Take(5)));
        Assert.True(vm.Chart!.Bars[0].Selected);

        vm.SelectBucketCommand.Execute(0);
        Assert.Equal("This week", vm.PeriodLabel);
        Assert.DoesNotContain(vm.Chart!.Bars, b => b.Selected);

        vm.SelectBucketCommand.Execute(5); // Saturday: in the future, nothing to show
        Assert.Equal("This week", vm.PeriodLabel);
    }

    [Fact]
    public void Clicking_an_hour_shows_that_hour_and_changing_tab_clears_it()
    {
        AddFixture();
        var vm = Create();

        vm.SelectBucketCommand.Execute(12);
        Assert.Equal(("12:00–13:00", "999k"), (vm.PeriodLabel, vm.Rows[0].TokenText));
        Assert.Equal("Copilot", vm.Rows[0].Name);

        vm.SelectedPeriod = Period.Month;
        Assert.Equal("This month", vm.PeriodLabel);
        vm.SelectedPeriod = Period.Today;
        Assert.Equal("Today", vm.PeriodLabel);
    }

    [Fact]
    public void The_model_card_keeps_the_whole_tab_while_a_bar_is_drilled_into()
    {
        AddFixture();
        var vm = Create();
        vm.SelectBucketCommand.Execute(12);

        Assert.Equal("Today", vm.ModelBreakdown(Tool.Claude).PeriodLabel);
    }

    [Fact]
    public void Hide_removes_chart_and_comparison_and_is_saved_as_a_setting()
    {
        AddFixture();
        var vm = Create();
        TrackSettings? saved = null;
        vm.SettingsChanged += (_, s) => saved = s;
        vm.SelectBucketCommand.Execute(10);

        Assert.Equal((true, "Minimize chart"), (vm.ChartVisible, vm.ChartToggleText));
        vm.ToggleChartCommand.Execute(null);

        Assert.Equal((false, "Show chart", "Today"), (vm.ChartVisible, vm.ChartToggleText, vm.PeriodLabel));
        Assert.Null(vm.Chart);
        Assert.Null(vm.ComparisonText);
        Assert.True(saved!.PopoverChartHidden);
        vm.SelectBucketCommand.Execute(10); // no chart: nothing to click
        Assert.Equal("Today", vm.PeriodLabel);

        // Minimized, the chart's header row stays with its title, and the title follows the tab.
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        Assert.Equal("Today by hour", vm.ChartTitle);
        vm.SelectedPeriod = Period.Week;
        Assert.Equal("This week by day", vm.ChartTitle);
        Assert.Contains(nameof(vm.ChartTitle), raised);
        Assert.Null(vm.Chart);
        vm.SelectedPeriod = Period.Today;

        vm.ToggleChartCommand.Execute(null);
        Assert.Equal((true, "Minimize chart", false), (vm.ChartVisible, vm.ChartToggleText, saved.PopoverChartHidden));
        Assert.NotNull(vm.Chart);
    }

    [Fact]
    public void A_hidden_chart_stays_hidden_after_a_restart()
    {
        var fs = new MockFileSystem();
        var store = new SettingsStore(fs, @"C:\d\settings.json");
        store.Save(new TrackSettings { PopoverChartHidden = true });

        var vm = Create(store.Load());

        Assert.False(vm.ChartVisible);
        Assert.Null(vm.Chart);
        Assert.False(new TrackSettings().PopoverChartHidden);
    }

    [Fact]
    public void Toggling_without_a_listener_still_works()
    {
        var vm = Create();
        vm.ToggleChartCommand.Execute(null);

        Assert.False(vm.ChartVisible);
    }

    [Fact]
    public void While_closed_the_table_stays_correct_and_the_chart_is_built_again_on_open()
    {
        AddFixture();
        var vm = Create();
        var open = Text(vm);
        var changes = new List<string?>();
        vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        vm.IsShown = false;

        Assert.False(vm.IsShown);
        Assert.Null(vm.Chart);
        Assert.Null(vm.ComparisonText);
        Assert.Equal(
            """
            [Today]
            Claude Code | 21.4M | $25.60 · in 3.0M · out 400k · cache 18.0M
            Copilot | 999k | $4.00 · in 999k · out 0 · cache 0
            Codex | 0 | $0.00 · in 0 · out 0 · cache 0
            Total | 22.4M | $29.60 · in 4.0M · out 400k · cache 18.0M
            compare: -
            """.ReplaceLineEndings("\n"),
            Text(vm));

        changes.Clear();
        vm.IsShown = false; // no change: nothing rebuilt
        Assert.Empty(changes);

        vm.IsShown = true;
        Assert.Equal(open, Text(vm));
    }

    [Fact]
    public void Listed_tools_are_the_ones_the_table_shows_so_the_island_can_list_the_same()
    {
        Assert.Empty(new PopoverViewModel(_store, new UsageAggregator(_store), new PeriodCalculator(_time, Copenhagen), _time, new TrackSettings()).ListedTools);

        AddFixture();
        var vm = Create();

        Assert.Equal([Tool.Claude, Tool.Codex, Tool.Copilot], vm.ListedTools);
        Assert.Equal(vm.Rows.Select(r => r.Tool!.Value).Order(), vm.ListedTools.Order());
    }

    [Fact]
    public void A_drilled_bar_survives_closing_and_opening_the_popover()
    {
        AddFixture();
        var vm = Create();
        vm.SelectedPeriod = Period.Week;
        vm.SelectBucketCommand.Execute(0);

        vm.IsShown = false;
        Assert.Equal("Mon 16 Nov", vm.PeriodLabel);
        Assert.Equal("5.1M", vm.Rows[0].TokenText);

        vm.IsShown = true;
        Assert.Equal("Mon 16 Nov", vm.PeriodLabel);
        Assert.True(vm.Chart!.Bars[0].Selected);
    }

    [Fact]
    public void At_midnight_today_becomes_the_new_day_and_a_drilled_hour_is_dropped()
    {
        AddFixture();
        _time.SetUtcNow(new DateTimeOffset(2026, 11, 18, 22, 59, 59, TimeSpan.Zero)); // 23:59:59 local
        var vm = Create();
        vm.SelectBucketCommand.Execute(10);
        Assert.Equal("10:00–11:00", vm.PeriodLabel);

        _time.Advance(TimeSpan.FromSeconds(1)); // 00:00:00 on Thursday
        vm.LiveUpdated(new Dictionary<Tool, Track.Core.Live.LiveStatus>());

        Assert.Equal(
            """
            [Today]
            Claude Code | 0 | $0.00 · in 0 · out 0 · cache 0
            Codex | 0 | $0.00 · in 0 · out 0 · cache 0
            Copilot | 0 | $0.00 · in 0 · out 0 · cache 0
            Total | 0 | $0.00 · in 0 · out 0 · cache 0
            chart: Today by hour | no usage yet | axis 00@0.5 06@6.5 12@12.5 18@18.5 24@24
            bar 0 (now): 00:00–01:00 / no usage
            future bars: 23
            compare: -
            """.ReplaceLineEndings("\n"),
            Text(vm));
    }

    [Fact]
    public void On_monday_at_midnight_a_new_week_starts()
    {
        AddFixture();
        _time.SetUtcNow(new DateTimeOffset(2026, 11, 22, 22, 59, 59, TimeSpan.Zero)); // Sunday 23:59:59 local
        var vm = Create();
        vm.SelectedPeriod = Period.Week;
        Assert.Equal("Mon 16 Nov", vm.Chart!.Bars[0].Tooltip.Split(" · ")[0]);
        Assert.Equal("27.5M", vm.TotalRow!.TokenText);

        _time.Advance(TimeSpan.FromSeconds(1));
        vm.UsageRefreshed();

        Assert.Equal(("Mon 23 Nov\nno usage", "0", 6), (vm.Chart!.Bars[0].Tooltip, vm.TotalRow!.TokenText, vm.Chart.Bars.Count(b => b.Future)));
        Assert.Null(vm.ComparisonText); // 0 minutes into the week: nothing to compare
    }

    [Fact]
    public void On_the_first_a_new_month_starts_with_its_own_number_of_days()
    {
        AddFixture();
        _time.SetUtcNow(new DateTimeOffset(2026, 11, 30, 22, 59, 59, TimeSpan.Zero)); // 30 Nov 23:59:59 local
        var vm = Create();
        vm.SelectedPeriod = Period.Month;
        Assert.Equal((30, "29.4M"), (vm.Chart!.Bars.Count, vm.TotalRow!.TokenText));

        _time.Advance(TimeSpan.FromSeconds(1));
        vm.UsageRefreshed();

        Assert.Equal((31, "Tue 1 Dec\nno usage", "0"), (vm.Chart!.Bars.Count, vm.Chart.Bars[0].Tooltip, vm.TotalRow!.TokenText));
    }
}
