using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Track.Core.Ingest;
using Track.Core.Live;
using Track.Core.Settings;
using Track.Core.Usage;

namespace Track.Core.Display;

/// <summary>Raises <see cref="PropertyChanged"/> when a value really changes.</summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string name = "")
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        Raise(name);
        return true;
    }

    protected void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>A button action for XAML, without WPF types.</summary>
public sealed class RelayCommand(Action<object?> execute) : ICommand
{
    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => execute(parameter);
}

/// <summary>One row of the usage table: big token number, small price · in · out · cache line.</summary>
public sealed record ToolRowViewModel(
    Tool? Tool,
    string Name,
    string LogoKey,
    string TokenText,
    string SubText,
    double BarFraction,
    UsageTotals Totals);

/// <summary>One line of the "what is running" box.</summary>
public sealed record LiveRowViewModel(Tool Tool, string Name, string LogoKey, bool Working, string Detail);

/// <summary>One model in the hover card.</summary>
public sealed record ModelRowViewModel(string Model, string TokenText, string CostText, string SplitText, double Share);

/// <summary>The hover card: a tool's models for the selected period, largest first.</summary>
public sealed record ModelBreakdownViewModel(Tool Tool, string ToolName, string PeriodLabel, IReadOnlyList<ModelRowViewModel> Rows);

/// <summary>Everything the popover shows. Plain .NET: the WPF shell only binds to it.</summary>
public sealed class PopoverViewModel : ObservableObject
{
    private static readonly Tool[] Order = [Tool.Claude, Tool.Codex, Tool.Copilot];

    private readonly UsageAggregator _usage;
    private readonly UsagePanelBuilder _panel;
    private readonly PeriodCalculator _periods;
    private readonly TimeProvider _time;
    private TrackSettings _settings;
    private IReadOnlyDictionary<Tool, LiveStatus> _live = new Dictionary<Tool, LiveStatus>();
    private DateTimeOffset? _lastRefreshUtc;

    private Period _selectedPeriod;
    private IReadOnlyList<ToolRowViewModel> _rows = [];
    private ToolRowViewModel? _totalRow;
    private IReadOnlyList<LiveRowViewModel> _liveRows = [];
    private IReadOnlyDictionary<Tool, ToolVisibility> _visibility = new Dictionary<Tool, ToolVisibility>();
    private string _updatedText = "Not updated yet";
    private string? _statusText;
    private string _periodLabel = "";
    private ChartViewModel? _chart;
    private string? _comparisonText;
    private DateTimeOffset? _selectedStartUtc;
    private bool _isShown = true;

    public PopoverViewModel(UsageStore store, UsageAggregator usage, PeriodCalculator periods, TimeProvider time, TrackSettings settings)
    {
        _usage = usage;
        _panel = new UsagePanelBuilder(store, usage, periods, time);
        _periods = periods;
        _time = time;
        _settings = settings;
        _selectedPeriod = settings.LastPeriod;
        SelectPeriodCommand = new RelayCommand(p => SelectedPeriod = (Period)p!);
        RefreshCommand = new RelayCommand(_ => RefreshRequested?.Invoke(this, EventArgs.Empty));
        ToggleChartCommand = new RelayCommand(_ => ToggleChart());
        SelectBucketCommand = new RelayCommand(p => SelectBucket((int)p!));
        _periodLabel = Label(_selectedPeriod);
    }

    /// <summary>Raised when the user changes a setting from the popover (the Hide / Show chart toggle); the app saves it.</summary>
    public event EventHandler<TrackSettings>? SettingsChanged;

    /// <summary>Hide / Show the chart (next to the period tabs).</summary>
    public ICommand ToggleChartCommand { get; }

    /// <summary>Click on bar <c>index</c>: show only that hour or day; the same bar again goes back.</summary>
    public ICommand SelectBucketCommand { get; }

    public bool ChartVisible => !_settings.PopoverChartHidden;

    /// <summary>The tools the usage table lists (the visibility rule); the island's Usage tab lists the same ones.</summary>
    public IReadOnlyList<Tool> ListedTools { get; private set; } = [];

    /// <summary>
    /// Whether the popover is on screen. While it is not, the chart and comparison are not worked out (nobody can
    /// see them), which keeps the 5-second refresh cheap; opening it rebuilds them at once.
    /// </summary>
    public bool IsShown
    {
        get => _isShown;
        set
        {
            if (Set(ref _isShown, value))
            {
                Rebuild();
            }
        }
    }

    /// <summary>
    /// What the minimize button next to the chart's top value does (its tooltip). It sits on the chart's own header
    /// row, which stays visible while minimized, so the period tabs and the usage rows never move (user review 2026-11-18).
    /// </summary>
    public string ChartToggleText => ChartVisible ? "Minimize chart" : "Show chart";

    /// <summary>The chart header's title ("Today by hour" …), shown whether the chart is open or minimized.</summary>
    public string ChartTitle => ChartViewModel.TitleFor(SelectedPeriod);

    /// <summary>The usage chart, or null when hidden.</summary>
    public ChartViewModel? Chart { get => _chart; private set => Set(ref _chart, value); }

    /// <summary>e.g. "+18 % vs last week at this time"; null when hidden or nothing to compare.</summary>
    public string? ComparisonText { get => _comparisonText; private set => Set(ref _comparisonText, value); }

    /// <summary>Raised by the Refresh button; the app runs a usage refresh.</summary>
    public event EventHandler? RefreshRequested;

    public ICommand SelectPeriodCommand { get; }

    public ICommand RefreshCommand { get; }

    public Period SelectedPeriod
    {
        get => _selectedPeriod;
        set
        {
            if (Set(ref _selectedPeriod, value))
            {
                _selectedStartUtc = null; // a drilled-down bar belongs to the old tab
                Rebuild();
            }
        }
    }

    /// <summary>"Today" / "This week" / "This month", or the clicked bar, e.g. "Tue 17 Nov".</summary>
    public string PeriodLabel { get => _periodLabel; private set => Set(ref _periodLabel, value); }

    public IReadOnlyList<ToolRowViewModel> Rows { get => _rows; private set => Set(ref _rows, value); }

    /// <summary>The Total row, or null when one tool (or none) is listed.</summary>
    public ToolRowViewModel? TotalRow { get => _totalRow; private set => Set(ref _totalRow, value); }

    public IReadOnlyList<LiveRowViewModel> LiveRows { get => _liveRows; private set => Set(ref _liveRows, value); }

    /// <summary>Which tools appear where (also drives the tray icon).</summary>
    public IReadOnlyDictionary<Tool, ToolVisibility> Visibility { get => _visibility; private set => Set(ref _visibility, value); }

    /// <summary>Shown instead of the table when no tool is listed.</summary>
    public string? EmptyText => Rows.Count == 0
        ? $"No Claude Code, Codex or Copilot usage in the last {_settings.Visibility.RemoveAfterNoUsageDays} days."
        : null;

    public string UpdatedText { get => _updatedText; private set => Set(ref _updatedText, value); }

    public string ResetText => $"Week resets Mon 00:00 · every {_settings.RefreshSeconds} s";

    /// <summary>A refresh error, or null when all is well.</summary>
    public string? StatusText { get => _statusText; set => Set(ref _statusText, value); }

    public bool RowLogos => _settings.RowLogos;

    public static string Label(Period period) => UsagePanelBuilder.PeriodLabel(period);

    public void ApplySettings(TrackSettings settings)
    {
        _settings = settings;
        Rebuild();
        Raise(nameof(ResetText));
        Raise(nameof(RowLogos));
        Raise(nameof(ChartVisible));
        Raise(nameof(ChartToggleText));
    }

    /// <summary>Call after a usage refresh.</summary>
    public void UsageRefreshed()
    {
        _lastRefreshUtc = _time.GetUtcNow();
        Rebuild();
    }

    /// <summary>Call after a live-status check (every 5 s).</summary>
    public void LiveUpdated(IReadOnlyDictionary<Tool, LiveStatus> live)
    {
        _live = live;
        Rebuild();
    }

    /// <summary>Updates the "Updated 12s ago" text; call on a timer while the popover is open.</summary>
    public void Tick() => UpdatedText = _lastRefreshUtc is { } last ? "Updated " + Format.Ago(_time.GetUtcNow() - last) : "Not updated yet";

    public ModelBreakdownViewModel ModelBreakdown(Tool tool)
    {
        var models = _usage.ByModel(tool, _periods.Range(SelectedPeriod));
        var total = Math.Max(1, models.Sum(m => m.Totals.Tokens.Total));
        return new ModelBreakdownViewModel(
            tool,
            ToolNames.Display(tool),
            Label(SelectedPeriod), // the card always covers the whole tab, even while a bar is drilled into
            models
                .Where(m => m.Totals.Tokens.Total > 0)
                .Select(m => new ModelRowViewModel(
                    m.Model,
                    Format.Tokens(m.Totals.Tokens.Total),
                    Format.Cost(m.Totals),
                    Format.Split(m.Totals),
                    (double)m.Totals.Tokens.Total / total))
                .ToList());
    }

    private void Rebuild()
    {
        Visibility = Order.ToDictionary(t => t, t => ToolVisibilityPolicy.Decide(t, IsWorking(t), _usage, _periods, _settings.Visibility));
        var listed = Order.Where(t => Visibility[t].InPopover).ToList();
        ListedTools = listed;

        // Rebuilt on every refresh (5 s live, 30 s usage), so the periods follow the clock: shortly after
        // midnight Today is the new day, and a drilled-down bar that no longer exists is dropped.
        var panel = _panel.Build(SelectedPeriod, listed, _selectedStartUtc, ChartVisible && IsShown);
        _selectedStartUtc = panel.SelectedStartUtc;
        Rows = panel.Rows;
        TotalRow = panel.TotalRow;
        Chart = panel.Chart;
        ComparisonText = panel.ComparisonText;
        PeriodLabel = panel.Label;
        Raise(nameof(ChartTitle));
        LiveRows = listed.Select(LiveRow).ToList();
        Raise(nameof(EmptyText));
        Tick();
    }

    private bool IsWorking(Tool tool) => _live.TryGetValue(tool, out var s) && s.Working;

    private void ToggleChart()
    {
        _settings = _settings with { PopoverChartHidden = ChartVisible };
        _selectedStartUtc = null;
        SettingsChanged?.Invoke(this, _settings);
        Rebuild();
        Raise(nameof(ChartVisible));
        Raise(nameof(ChartToggleText));
    }

    private void SelectBucket(int index)
    {
        if (Chart is { } chart)
        {
            _selectedStartUtc = UsagePanelBuilder.Toggle(chart, index, _selectedStartUtc);
            Rebuild();
        }
    }

    private LiveRowViewModel LiveRow(Tool tool)
    {
        var name = ToolNames.Display(tool);
        var logo = tool.ToString().ToLowerInvariant();
        if (!_live.TryGetValue(tool, out var s))
        {
            return new LiveRowViewModel(tool, name, logo, false, "idle");
        }

        var model = s.LastModel is null ? "" : $" · {s.LastModel}";
        var detail = s.Working
            ? $"{s.ActiveSessions} session{(s.ActiveSessions == 1 ? "" : "s")}{model}"
            : s.LastActivityUtc is { } last ? $"idle · {Format.Ago(_time.GetUtcNow() - last)}" : "idle";
        return new LiveRowViewModel(tool, name, logo, s.Working, detail);
    }
}
