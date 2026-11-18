using System.ComponentModel;
using Microsoft.Extensions.Time.Testing;
using Track.Core.Display;
using Track.Core.Ingest;
using Track.Core.Live;
using Track.Core.Parsing;
using Track.Core.Settings;
using Track.Core.Usage;

namespace Track.Core.Tests.Display;

public class PopoverViewModelTests
{
    // Wednesday 18 Nov 2026, 13:00 in Copenhagen.
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeZoneInfo Copenhagen = TimeZoneInfo.FindSystemTimeZoneById("Europe/Copenhagen");

    private readonly FakeTimeProvider _time = new(Now);
    private readonly UsageStore _store = new();
    private readonly UsageAggregator _usage;

    public PopoverViewModelTests() => _usage = new UsageAggregator(_store);

    private PopoverViewModel Create(TrackSettings? settings = null) =>
        new(_store, _usage, new PeriodCalculator(_time, Copenhagen), _time, settings ?? new TrackSettings());

    private void Add(Tool tool, string model, DateTimeOffset at, long input = 0, long cacheWrite = 0, long cacheRead = 0, long output = 0) =>
        _store.Add([new UsageRecord(tool, "s", model, at, input, cacheWrite, cacheRead, output, 0, Guid.NewGuid().ToString())]);

    /// <summary>
    /// Hand-priced with the bundled LiteLLM list: claude-opus-5-5 $4 in / $20 out / $5 cache write / $0.20 cache read per M;
    /// gpt-6.1-sol $2 / $10 / $2.50 / $0.10; gpt-6-luna $0.10 in; codex-auto-review has no price.
    /// </summary>
    private void AddFixture()
    {
        // Claude today: 1M in + 2M cache write + 18M cache read + 0.4M out = 21.4M; $4 + $10 + $3.60 + $8 = $25.60
        Add(Tool.Claude, "claude-opus-5-5", Now.AddHours(-3), input: 1_000_000, cacheWrite: 2_000_000, cacheRead: 18_000_000, output: 400_000);
        // Codex Monday (this week, not today): 0.5M in + 4.5M cache + 0.1M out = 5.1M; $1 + $0.45 + $1 = $2.45
        Add(Tool.Codex, "gpt-6.1-sol", new DateTimeOffset(2026, 11, 16, 10, 0, 0, TimeSpan.Zero), input: 500_000, cacheRead: 4_500_000, output: 100_000);
        // Codex 3 Nov (this month only), no price: 0.92M
        Add(Tool.Codex, "codex-auto-review", new DateTimeOffset(2026, 11, 3, 10, 0, 0, TimeSpan.Zero), input: 920_000);
        // Copilot today: 999,499 in = "999k"; $3.997996
        Add(Tool.Copilot, "claude-opus-5.5", Now.AddHours(-1), input: 999_499);
        // Copilot 2 Nov (this month only): 1M in at $0.10/M
        Add(Tool.Copilot, "gpt-6-luna", new DateTimeOffset(2026, 11, 2, 10, 0, 0, TimeSpan.Zero), input: 1_000_000);
    }

    private static string[] Lines(PopoverViewModel vm) =>
        [.. vm.Rows.Select(r => $"{r.Name} | {r.TokenText} | {r.SubText}"), $"{vm.TotalRow!.Name} | {vm.TotalRow.TokenText} | {vm.TotalRow.SubText}"];

    [Fact]
    public void Snapshot_of_all_rows_for_today_week_and_month()
    {
        AddFixture();
        var vm = Create();
        vm.UsageRefreshed();

        Assert.Equal(
            [
                "Claude Code | 21.4M | $25.60 · in 3.0M · out 400k · cache 18.0M",
                "Copilot | 999k | $4.00 · in 999k · out 0 · cache 0",
                "Codex | 0 | $0.00 · in 0 · out 0 · cache 0",
                "Total | 22.4M | $29.60 · in 4.0M · out 400k · cache 18.0M",
            ],
            Lines(vm));

        vm.SelectPeriodCommand.Execute(Period.Week);
        Assert.Equal(
            [
                "Claude Code | 21.4M | $25.60 · in 3.0M · out 400k · cache 18.0M",
                "Codex | 5.1M | $2.45 · in 500k · out 100k · cache 4.5M",
                "Copilot | 999k | $4.00 · in 999k · out 0 · cache 0",
                "Total | 27.5M | $32.05 · in 4.5M · out 500k · cache 22.5M",
            ],
            Lines(vm));

        vm.SelectPeriodCommand.Execute(Period.Month);
        Assert.Equal(
            [
                "Claude Code | 21.4M | $25.60 · in 3.0M · out 400k · cache 18.0M",
                "Codex | 6.0M | ≈$2.45 · in 1.4M · out 100k · cache 4.5M",
                "Copilot | 2.0M | $4.10 · in 2.0M · out 0 · cache 0",
                "Total | 29.4M | ≈$32.15 · in 6.4M · out 500k · cache 22.5M",
            ],
            Lines(vm));
        Assert.Equal("This month", vm.PeriodLabel);
    }

    [Fact]
    public void Bars_are_relative_to_the_largest_row_and_every_row_adds_up()
    {
        AddFixture();
        var vm = Create();
        vm.UsageRefreshed();

        Assert.Equal(1.0, vm.Rows[0].BarFraction);
        Assert.Equal(999_499 / 21_400_000.0, vm.Rows[1].BarFraction, 6);
        Assert.Equal(0.0, vm.Rows[2].BarFraction);
        foreach (var period in Enum.GetValues<Period>())
        {
            vm.SelectedPeriod = period;
            Assert.All(vm.Rows.Append(vm.TotalRow!), r => Assert.Equal(r.Totals.Tokens.Total, r.Totals.InTokens + r.Totals.OutTokens + r.Totals.CacheHits));
        }
    }

    [Fact]
    public void Row_keys_name_the_logo_to_draw()
    {
        AddFixture();
        var vm = Create();
        vm.UsageRefreshed();

        Assert.Equal(["claude", "copilot", "codex"], vm.Rows.Select(r => r.LogoKey));
        Assert.Equal("", vm.TotalRow!.LogoKey);
        Assert.Null(vm.TotalRow.Tool);
    }

    [Fact]
    public void Rows_are_sorted_by_usage_highest_first_and_ties_keep_the_tool_order()
    {
        Add(Tool.Claude, "claude-opus-5-5", Now.AddHours(-1), input: 10);
        Add(Tool.Codex, "gpt-6.1-sol", Now.AddHours(-1), input: 30);
        Add(Tool.Copilot, "gpt-6-luna", Now.AddHours(-1), input: 10);
        var vm = Create();
        vm.UsageRefreshed();

        Assert.Equal(["Codex", "Claude Code", "Copilot"], vm.Rows.Select(r => r.Name));
    }

    [Fact]
    public void Tools_without_recent_usage_are_left_out_and_one_tool_has_no_total_row()
    {
        Add(Tool.Claude, "claude-opus-5-5", Now.AddHours(-1), input: 10);
        var vm = Create();
        vm.UsageRefreshed();

        Assert.Equal(["Claude Code"], vm.Rows.Select(r => r.Name));
        Assert.Null(vm.TotalRow);
        Assert.Null(vm.EmptyText);
        Assert.False(vm.Visibility[Tool.Codex].InPopover);
    }

    [Fact]
    public void No_usage_at_all_shows_the_empty_text()
    {
        var vm = Create();
        vm.UsageRefreshed();

        Assert.Empty(vm.Rows);
        Assert.Empty(vm.LiveRows);
        Assert.Equal("No Claude Code, Codex or Copilot usage in the last 30 days.", vm.EmptyText);
    }

    [Fact]
    public void A_running_tool_without_usage_is_listed()
    {
        var vm = Create();
        vm.LiveUpdated(Live(Tool.Codex, working: true, sessions: 1, model: null));

        Assert.Equal(["Codex"], vm.Rows.Select(r => r.Name));
        Assert.Equal(new LiveRowViewModel(Tool.Codex, "Codex", "codex", true, "1 session"), Assert.Single(vm.LiveRows));
    }

    [Fact]
    public void Live_rows_say_what_each_tool_is_doing()
    {
        AddFixture();
        var vm = Create();
        var live = new Dictionary<Tool, LiveStatus>
        {
            [Tool.Claude] = new(Tool.Claude, true, 2, 0, true, "claude-opus-5-5", Now),
            [Tool.Codex] = new(Tool.Codex, false, 0, 0, true, "gpt-6.1-sol", Now.AddMinutes(-41)),
        };

        vm.LiveUpdated(live);

        Assert.Equal(
            ["2 sessions · claude-opus-5-5", "idle · 41 min ago", "idle"],
            vm.LiveRows.Select(r => r.Detail));
        Assert.Equal([true, false, false], vm.LiveRows.Select(r => r.Working));

        vm.LiveUpdated(new Dictionary<Tool, LiveStatus> { [Tool.Copilot] = new(Tool.Copilot, false, 0, 3, true, null, null) });
        Assert.Equal("idle", vm.LiveRows.Single(r => r.Tool == Tool.Copilot).Detail);
    }

    [Fact]
    public void Updated_text_counts_up_from_the_last_refresh()
    {
        var vm = Create();
        Assert.Equal("Not updated yet", vm.UpdatedText);
        vm.Tick();
        Assert.Equal("Not updated yet", vm.UpdatedText);

        vm.UsageRefreshed();
        Assert.Equal("Updated just now", vm.UpdatedText);

        _time.Advance(TimeSpan.FromSeconds(12));
        vm.Tick();
        Assert.Equal("Updated 12s ago", vm.UpdatedText);
    }

    [Fact]
    public void Settings_change_the_footer_logos_and_starting_tab()
    {
        var vm = Create(new TrackSettings { LastPeriod = Period.Week, RefreshSeconds = 60, RowLogos = false });
        Assert.Equal((Period.Week, "This week", "Week resets Mon 00:00 · every 60 s", false), (vm.SelectedPeriod, vm.PeriodLabel, vm.ResetText, vm.RowLogos));

        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        vm.ApplySettings(new TrackSettings());

        Assert.Equal(("Week resets Mon 00:00 · every 30 s", true), (vm.ResetText, vm.RowLogos));
        Assert.Contains(nameof(PopoverViewModel.ResetText), changed);
        Assert.Contains(nameof(PopoverViewModel.RowLogos), changed);
    }

    [Fact]
    public void Changing_the_tab_raises_change_notifications_once()
    {
        var vm = Create();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.SelectedPeriod = Period.Month;
        vm.SelectedPeriod = Period.Month;

        Assert.Equal(1, changed.Count(n => n == nameof(PopoverViewModel.SelectedPeriod)));
        Assert.Contains(nameof(PopoverViewModel.PeriodLabel), changed);
    }

    [Fact]
    public void Refresh_button_asks_the_app_to_refresh()
    {
        var vm = Create();
        vm.RefreshCommand.Execute(null); // nobody listening yet: nothing happens

        var asked = 0;
        vm.RefreshRequested += (_, _) => asked++;
        vm.RefreshCommand.Execute(null);

        Assert.Equal(1, asked);
        Assert.True(vm.RefreshCommand.CanExecute(null));
        EventHandler handler = (_, _) => { };
        vm.RefreshCommand.CanExecuteChanged += handler;
        vm.RefreshCommand.CanExecuteChanged -= handler;
    }

    [Fact]
    public void Status_text_shows_refresh_errors()
    {
        var vm = Create();
        vm.StatusText = "Usage refresh failed: disk busy";

        Assert.Equal("Usage refresh failed: disk busy", vm.StatusText);
    }

    [Fact]
    public void Model_breakdown_lists_models_of_the_selected_period_largest_first()
    {
        AddFixture();
        var vm = Create();
        vm.SelectedPeriod = Period.Month;

        var card = vm.ModelBreakdown(Tool.Codex);

        Assert.Equal((Tool.Codex, "Codex", "This month"), (card.Tool, card.ToolName, card.PeriodLabel));
        Assert.Equal(
            [
                new ModelRowViewModel("gpt-6.1-sol", "5.1M", "$2.45", "in 500k · out 100k · cache 4.5M", 5_100_000 / 6_020_000.0),
                new ModelRowViewModel("codex-auto-review", "920k", "$—", "in 920k · out 0 · cache 0", 920_000 / 6_020_000.0),
            ],
            card.Rows);
        Assert.Equal(1.0, card.Rows.Sum(r => r.Share), 9);
    }

    [Fact]
    public void Model_breakdown_is_empty_when_the_tool_has_no_tokens_in_the_period()
    {
        AddFixture();
        var vm = Create();

        Assert.Empty(vm.ModelBreakdown(Tool.Codex).Rows);
        Assert.DoesNotContain(vm.ModelBreakdown(Tool.Copilot).Rows, r => r.Model == "gpt-6-luna");
    }

    [Fact]
    public void Unknown_periods_have_no_label()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PopoverViewModel.Label((Period)7));
    }

    private static Dictionary<Tool, LiveStatus> Live(Tool tool, bool working, int sessions, string? model) =>
        new() { [tool] = new LiveStatus(tool, working, sessions, 0, true, model, Now) };
}
