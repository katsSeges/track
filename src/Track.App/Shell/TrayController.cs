using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Abstractions;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using H.NotifyIcon;
using Microsoft.Win32;
using Track.Core;
using Track.Core.Activity;
using Track.Core.Island;
using Track.Core.Display;
using Track.Core.Ingest;
using Track.Core.Live;
using Track.Core.Pricing;
using Track.Core.Settings;
using Track.Core.Tray;
using Track.Core.Time;
using Track.Core.Usage;

namespace Track.App.Shell;

/// <summary>Where Track reads logs and keeps its own files, plus verification switches.</summary>
public sealed record AppOptions(
    string Home,
    string DataDirectory,
    bool OpenAtStart,
    Tool? CardAtStart,
    bool? DarkOverride,
    string? RenderIconsTo,
    bool ManageStartup,
    bool Diagnostics,
    string? RenderPopoverTo = null,
    string? RenderIslandTo = null,
    string? PointerFile = null)
{
    /// <summary>
    /// Reads <c>TRACK_HOME</c> / <c>TRACK_DATA</c> / <c>TRACK_DIAG</c> / <c>TRACK_POINTER_FILE</c> and the flags <c>--open</c>,
    /// <c>--card claude|codex|copilot</c>, <c>--theme light|dark</c>, <c>--render-icons &lt;dir&gt;</c>,
    /// <c>--render-popover &lt;dir&gt;</c>, <c>--render-island &lt;dir&gt;</c>, <c>--no-startup</c>.
    /// </summary>
    [ExcludeFromCodeCoverage(Justification = "Startup plumbing.")]
    public static AppOptions From(string[] args)
    {
        string? Value(string flag) => args.SkipWhile(a => a != flag).Skip(1).FirstOrDefault();
        var home = Environment.GetEnvironmentVariable("TRACK_HOME") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var data = Environment.GetEnvironmentVariable("TRACK_DATA")
                   ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Track");
        return new AppOptions(
            home,
            data,
            args.Contains("--open"),
            Logos.FromKey(Value("--card")),
            Value("--theme") switch { "dark" => true, "light" => false, _ => null },
            Value("--render-icons"),
            !args.Contains("--no-startup") && Value("--render-popover") is null && Value("--render-island") is null,
            Environment.GetEnvironmentVariable("TRACK_DIAG") == "1",
            Value("--render-popover"),
            Value("--render-island"),
            Environment.GetEnvironmentVariable("TRACK_POINTER_FILE"));
    }
}

/// <summary>
/// Wires Track.Core to Windows: tray icon, menu, popover, model card, refresh timers, theme, startup
/// entry and snapshot saving. Background refreshes and UI reads share one lock around the store.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "WPF composition root; every decision it relies on is tested in Track.Core.")]
public sealed class TrayController : IDisposable
{
    private readonly object _gate = new();
    private readonly AppOptions _options;
    private readonly Dispatcher _ui;
    private readonly IFileSystem _fs = new FileSystem();
    private readonly SettingsStore _settingsStore;
    private readonly UsageIngestor _ingestor;
    private readonly UsageAggregator _usage;
    private readonly PeriodCalculator _periods;
    private readonly LiveStatusDetector _liveDetector;
    private readonly PriceLoader _prices;
    private readonly PopoverViewModel _vm;
    private readonly TrayIconRenderer _renderer = new();
    private readonly DispatcherTimer _tick;
    private readonly DispatcherTimer _priceTimer;
    private readonly DispatcherTimer _pointer;
    private readonly UsageStore _store;
    private readonly ActivityReader _activity;
    private readonly LogScanCache _scanCache;
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly DispatcherTimer _trim;
    private bool _trimmedAfterStart;
    private bool _liveReadOnce;
    private bool _usageReadOnce;
    private readonly IslandStateMachine _island;
    private readonly IslandViewModel _islandVm;
    private readonly TimeProvider _time;
    private IslandWindow? _islandWindow;
    private IslandContent? _islandState;
    private int _clicksFromFile;
    private int _clicksFromWindow;
    private int _escapes;

    private TrackSettings _settings;
    private RefreshCoordinator? _coordinator;
    private TaskbarIcon? _tray;
    private PopoverWindow? _popover;
    private ModelCardWindow? _card;
    private IReadOnlyDictionary<Tool, LiveStatus> _live = new Dictionary<Tool, LiveStatus>();
    private string? _iconKey;
    private string _priceNote = "bundled prices";

    public TrayController(AppOptions options, Dispatcher ui)
    {
        _options = options;
        _ui = ui;
        _fs.Directory.CreateDirectory(options.DataDirectory);
        _settingsStore = new SettingsStore(_fs, Path.Combine(options.DataDirectory, "settings.json"));
        _settings = _settingsStore.Load();

        // TRACK_CLOCK_OFFSET (seconds, only with TRACK_DIAG=1) shifts the app's clock for rollover checks.
        var time = _time = ShiftedTimeProvider.For(TimeProvider.System, Environment.GetEnvironmentVariable("TRACK_CLOCK_OFFSET"), options.Diagnostics);
        var scanner = new LogScanner(_fs, LogSources.Default(options.Home));
        var store = _store = new UsageStore();
        _ingestor = new UsageIngestor(scanner, new IncrementalLogReader(_fs), store, time, new SnapshotStore(_fs, Path.Combine(options.DataDirectory, "snapshot.json")));
        _usage = new UsageAggregator(store);
        _periods = new PeriodCalculator(time, TimeZoneInfo.Local);
        _liveDetector = new LiveStatusDetector(_fs, scanner, Path.Combine(options.Home, ".copilot", "session-state"), new SystemProcessList(), _usage, time);
        _prices = new PriceLoader(_fs, Path.Combine(options.DataDirectory, "prices.json"), new LiteLlmPriceDownloader(new HttpClient { Timeout = TimeSpan.FromSeconds(20) }), time);
        _vm = new PopoverViewModel(store, _usage, _periods, time, _settings);
        _vm.SettingsChanged += (_, changed) =>
        {
            _settings = changed;
            if (_options.RenderPopoverTo is null && _options.RenderIslandTo is null)
            {
                _settingsStore.Save(changed); // render-only runs never touch the user's settings
            }
        };
        _vm.RefreshRequested += (_, _) => Task.Run(() => _coordinator?.RefreshUsageNow());
        // Closed until the popover opens: the chart is not worked out on every refresh while nobody can see it.
        _vm.IsShown = _options.RenderPopoverTo is not null;

        _activity = new ActivityReader(_fs, scanner, new AgentActivityTracker());
        _scanCache = new LogScanCache(_fs, scanner, time);
        _trim = new DispatcherTimer(TimeSpan.FromMinutes(30), DispatcherPriority.Background, (_, _) => TrimMemory(), ui);
        foreach (var root in LogSources.Default(options.Home).Select(s => s.Root).Where(Directory.Exists))
        {
            // A new log file (new session) triggers a full scan at the next live check instead of within a minute.
            var watcher = new FileSystemWatcher(root) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName };
            watcher.Created += (_, _) => _scanCache.MarkDirty();
            watcher.Renamed += (_, _) => _scanCache.MarkDirty();
            watcher.EnableRaisingEvents = true;
            _watchers.Add(watcher);
        }
        _island = new IslandStateMachine(time, () => _settings.Island);
        // The Usage tab lists the same tools as the popover (the visibility rule), so both show the same numbers.
        _islandVm = new IslandViewModel(_island, store, _usage, _periods, time, () => _settings.Island, () => _vm.ListedTools);
        _islandVm.TabChanged += (_, tab) => _settingsStore.Save(_settings = _settings with { Island = _settings.Island with { OpenTab = tab } });
        _pointer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) => OnPointerTick(), ui) { IsEnabled = false };

        _tick = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => _vm.Tick(), ui) { IsEnabled = false };
        _priceTimer = new DispatcherTimer(TimeSpan.FromHours(6), DispatcherPriority.Background, (_, _) => LoadPrices(), ui);
    }

    private static bool AppDark(AppOptions o) => o.DarkOverride ?? Native.ReadTheme(taskbar: false) == TaskbarTheme.Dark;

    private static TaskbarTheme TaskbarThemeFor(AppOptions o) =>
        o.DarkOverride is { } dark ? (dark ? TaskbarTheme.Dark : TaskbarTheme.Light) : Native.ReadTheme(taskbar: true);

    public void Start()
    {
        if (_options.ManageStartup)
        {
            new StartupManager(new StartupRegistry(), Environment.ProcessPath!).Apply(_settings.StartWithWindows);
        }

        ApplyTheme();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        _tray = new TaskbarIcon { ToolTipText = "Track", NoLeftClickDelay = true, ContextMenu = BuildMenu() };
        _tray.Icon = _renderer.Icon(TrayIconModel.Build(new HashSet<Tool>(), new HashSet<Tool>(), _settings.IconLayout, _settings.IdleLook, _settings.NothingRunningLook, TaskbarThemeFor(_options)), TrayIconRenderer.PixelSize());
        _tray.TrayLeftMouseUp += (_, _) => TogglePopover();
        _tray.ForceCreate(enablesEfficiencyMode: false);

        _pointer.Start();

        _coordinator = new RefreshCoordinator(
            TimeProvider.System,
            TimeSpan.FromSeconds(Math.Max(5, _settings.RefreshSeconds)),
            RefreshCoordinator.DefaultLiveInterval,
            RefreshUsage,
            RefreshLive,
            status => _ui.BeginInvoke(() => _vm.StatusText = status));
        if (_options.RenderIslandTo is { } islandTo)
        {
            Task.Run(async () =>
            {
                _coordinator.Start();
                var loaded = await _prices.LoadAsync();
                _ = _ui.BeginInvoke(DispatcherPriority.ApplicationIdle, () => RenderIslandAndExit(islandTo, loaded.Table));
            });
            return;
        }

        if (_options.RenderPopoverTo is { } renderTo)
        {
            Task.Run(async () =>
            {
                _coordinator.Start();
                var loaded = await _prices.LoadAsync();
                _ = _ui.BeginInvoke(DispatcherPriority.ApplicationIdle, () => RenderPopoverAndExit(renderTo, loaded.Table));
            });
            return;
        }

        Task.Run(() =>
        {
            _coordinator.Start();
            _ui.BeginInvoke(() =>
            {
                if (_options.OpenAtStart)
                {
                    OpenPopover(pinned: true);
                    if (_options.CardAtStart is { } tool)
                    {
                        _ui.BeginInvoke(DispatcherPriority.ApplicationIdle, () => ShowCard(tool));
                    }
                }
            });
        });
        LoadPrices();
    }

    /// <summary>Saves the snapshot and settings and removes the tray icon.</summary>
    public void Dispose()
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _coordinator?.Dispose();
        _tick.Stop();
        _priceTimer.Stop();
        _pointer.Stop();
        _trim.Stop();
        _watchers.ForEach(w => w.Dispose());
        _islandWindow?.Close();
        lock (_gate)
        {
            _ingestor.SaveSnapshot();
        }

        // Render-only runs (--render-popover / --render-island) step through tabs to draw them; they must not
        // change the user's settings (a render run once saved "Month" as the last tab, found 2026-11-18).
        if (_options.RenderPopoverTo is null && _options.RenderIslandTo is null)
        {
            _settingsStore.Save(_settings with { LastPeriod = _vm.SelectedPeriod });
        }
        _tray?.Dispose();
        _renderer.Dispose();
    }

    /// <summary>Renders the tray icon for six running states, both taskbar themes and three sizes, then returns.</summary>
    public static void RenderIcons(string directory, TrackSettings settings)
    {
        Directory.CreateDirectory(directory);
        var all = new HashSet<Tool> { Tool.Claude, Tool.Codex, Tool.Copilot };
        var states = new (string Name, Tool[] Running)[]
        {
            ("nothing", []), ("claude", [Tool.Claude]), ("codex", [Tool.Codex]), ("copilot", [Tool.Copilot]),
            ("claude+copilot", [Tool.Claude, Tool.Copilot]), ("all", [Tool.Claude, Tool.Codex, Tool.Copilot]),
        };
        foreach (var (name, running) in states)
        {
            foreach (var theme in Enum.GetValues<TaskbarTheme>())
            {
                var spec = TrayIconModel.Build(all, running.ToHashSet(), settings.IconLayout, settings.IdleLook, settings.NothingRunningLook, theme);
                foreach (var size in new[] { 16, 24, 32, 96 })
                {
                    File.WriteAllBytes(Path.Combine(directory, $"{name}-{theme.ToString().ToLowerInvariant()}-{size}.png"), TrayIconRenderer.Png(spec, size));
                }
            }
        }
    }

    /// <summary>
    /// Verification: renders the real popover (Today/Week/Month) and the model card for each tool to PNG files,
    /// laid over the solid fallback color because a screen capture of the acrylic blur is not possible here.
    /// </summary>
    private void RenderPopoverAndExit(string directory, PriceTable prices)
    {
        Directory.CreateDirectory(directory);
        var theme = AppDark(_options) ? "dark" : "light";
        lock (_gate)
        {
            _usage.Prices = prices;
            _vm.UsageRefreshed();
            _vm.LiveUpdated(_live = _liveDetector.Detect());
        }

        _popover = new PopoverWindow { DataContext = _vm, Dark = AppDark(_options), Pinned = true, Left = -4000, Top = 0, ShowActivated = false };
        _popover.Show();
        foreach (var period in Enum.GetValues<Period>())
        {
            lock (_gate)
            {
                _vm.SelectedPeriod = period;
            }

            _popover.UpdateLayout();
            Save(_popover, Path.Combine(directory, $"popover-{period.ToString().ToLowerInvariant()}-{theme}.png"));
        }

        // The other chart state (minimized, or open if the user minimized it), then back.
        _vm.ToggleChartCommand.Execute(null);
        _popover.UpdateLayout();
        Save(_popover, Path.Combine(directory, $"popover-month-chart-{(_vm.ChartVisible ? "open" : "minimized")}-{theme}.png"));
        _vm.ToggleChartCommand.Execute(null);
        _popover.UpdateLayout();

        _card = new ModelCardWindow { Dark = AppDark(_options), Left = -4000, Top = 0 };
        foreach (var tool in Enum.GetValues<Tool>())
        {
            var row = FindRow(_popover, tool);
            if (row is null)
            {
                continue;
            }

            lock (_gate)
            {
                _card.ShowFor(_vm.ModelBreakdown(tool), _popover, row);
            }

            _card.UpdateLayout();
            Save(_card, Path.Combine(directory, $"card-{tool.ToString().ToLowerInvariant()}-{theme}.png"));
        }

        UpdateTrayIcon(force: true);
        Application.Current.Shutdown();

        static void Save(Window window, string path)
        {
            var content = (FrameworkElement)window.Content;
            var scale = System.Windows.Media.VisualTreeHelper.GetDpi(window).DpiScaleX;
            var width = (int)Math.Ceiling(content.ActualWidth * scale);
            var height = (int)Math.Ceiling(content.ActualHeight * scale);
            var visual = new System.Windows.Media.DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                var fallback = (System.Windows.Media.Brush)window.FindResource("PopSolid");
                dc.DrawRoundedRectangle(fallback, null, new Rect(0, 0, content.ActualWidth, content.ActualHeight), 12, 12);
                dc.DrawRectangle(new System.Windows.Media.VisualBrush(content), null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
            }

            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height, 96 * scale, 96 * scale, System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var file = File.Create(path);
            encoder.Save(file);
        }
    }

    private void RefreshUsage()
    {
        lock (_gate)
        {
            _ingestor.Refresh();
        }

        _ui.BeginInvoke(() =>
        {
            lock (_gate)
            {
                _vm.UsageRefreshed();
            }

            UpdateTrayIcon();
            _usageReadOnce = true;
            TrimAfterStart();
        });
    }

    private void RefreshLive()
    {
        IReadOnlyDictionary<Tool, LiveStatus> live;
        IReadOnlyList<AgentActivity> working;
        IReadOnlySet<(Tool Tool, string SessionId)> waiting;
        lock (_gate)
        {
            var recent = _scanCache.Recent();
            live = _liveDetector.Detect(recent);
            var now = DateTimeOffset.UtcNow;
            var writes = _activity.Refresh(now, recent);
            working = _activity.Tracker.Working(now, writes, tool => live.TryGetValue(tool, out var s) && s.ProcessRunning);
            waiting = _activity.Tracker.Waiting();
            // One answer for "who is working": the tray icon, the popover and the island all use these agents.
            live = LiveStatusDetector.WithAgents(live, working);
        }

        var fullScreen = FullScreen();
        _ui.BeginInvoke(() =>
        {
            _live = live;
            lock (_gate)
            {
                _vm.LiveUpdated(live);
            }

            _island.Update(working, fullScreen, waiting);
            RenderIsland();
            UpdateTrayIcon();
            _liveReadOnce = true;
            TrimAfterStart();
        });
    }

    /// <summary>
    /// The first reads (snapshot, today's logs, the activity markers of the last day) are the only big allocations:
    /// give that memory back once, after BOTH the first usage read and the first live read. They run in parallel;
    /// trimming after the live one alone left the usage read's garbage (multi-MB Copilot lines) for 30 minutes
    /// (measured 2026-11-18: 52.8 MB one run, 128.7 MB another).
    /// </summary>
    private void TrimAfterStart()
    {
        if (!_trimmedAfterStart && _liveReadOnce && _usageReadOnce)
        {
            _trimmedAfterStart = true;
            TrimMemory();
            _trim.Start();
        }
    }

    /// <summary>The island window lives only while the island shows; it closes itself after sliding out.</summary>
    private IslandWindow CreateIslandWindow()
    {
        var window = new IslandWindow();
        window.Clicked += () =>
        {
            _clicksFromWindow++;
            _island.Click();
            RenderIsland();
        };
        window.EscapePressed += () =>
        {
            _escapes++;
            _island.Escape();
            RenderIsland();
        };
        window.TabSelected += tab =>
        {
            lock (_gate)
            {
                _islandVm.SelectTab(tab);
            }

            RenderIsland();
        };
        window.PeriodSelected += period =>
        {
            lock (_gate)
            {
                _islandVm.SelectPeriod(period);
            }

            RenderIsland();
        };
        window.BarSelected += index =>
        {
            lock (_gate)
            {
                _islandVm.SelectBucket(index);
            }

            RenderIsland();
        };
        window.MinimizeClicked += () =>
        {
            _island.Minimize();
            RenderIsland();
        };
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_islandWindow, window))
            {
                _islandWindow = null;
                _ui.BeginInvoke(DispatcherPriority.ApplicationIdle, TrimMemory);
            }
        };
        return window;
    }

    /// <summary>Compacts the managed heap and returns unused pages to Windows.</summary>
    private static void TrimMemory()
    {
        System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        Native.TrimWorkingSet();
    }

    /// <summary>A real full-screen app in front, or the test override's "fullscreen" line (which only adds, never hides it).</summary>
    private bool FullScreen() =>
        PointerOverride(consume: false) is { FullScreen: true } || Native.FullScreenAppInFront();

    /// <summary>Every 100 ms: top-edge dwell and pointer-over-island, then redraw if anything changed.</summary>
    private void OnPointerTick()
    {
        var o = PointerOverride();
        var pixels = o?.Point ?? Native.CursorPixels();
        if (o is { Click: true })
        {
            _clicksFromFile++;
            _island.Click();
        }

        if (o is { Minimize: true })
        {
            _island.Minimize();
        }

        var scale = Native.SystemDpiScale();
        var screenTop = ScreenArea().Top * scale;
        _island.PointerAtTop(pixels.Y <= screenTop + 1);
        var w = _islandWindow;
        var over = w is { IsVisible: true }
                   && pixels.X >= w.Left * scale && pixels.X <= (w.Left + w.ActualWidth) * scale
                   && pixels.Y >= w.Top * scale && pixels.Y <= (w.Top + w.ActualHeight) * scale;
        _island.PointerOverIsland(over);

        // A click anywhere outside the opened island closes it. Checked here with the real mouse, because WPF
        // does not reliably raise Deactivated for this no-activate tool window (found with real clicks).
        var pressed = Native.LeftButtonPressed();
        if (pressed && _island.State == IslandState.Open && w is { IsVisible: true })
        {
            var real = Native.CursorPixels();
            var onIsland = real.X >= w.Left * scale && real.X <= (w.Left + w.ActualWidth) * scale
                           && real.Y >= w.Top * scale && real.Y <= (w.Top + w.ActualHeight) * scale;
            if (!onIsland)
            {
                _escapes++;
                _island.Escape();
            }
        }

        _island.Tick();
        RenderIsland();
    }

    private Rect ScreenArea() => _settings.Island.Screen == IslandScreen.Mouse
        ? Native.MonitorWorkArea(Native.CursorPixels())
        : SystemParameters.WorkArea;

    private void RenderIsland()
    {
        if (_tray is null)
        {
            return; // not started (render-only modes)
        }

        IslandContent content;
        lock (_gate)
        {
            content = _islandVm.Build();
        }

        if (_islandWindow is null)
        {
            if (content.State == IslandState.Hidden)
            {
                _islandState = content;
                return;
            }

            _islandWindow = CreateIslandWindow();
        }

        _islandWindow.Screen = ScreenArea();
        _islandWindow.Render(content);
        // Tab, period and drilled bar count as changes too, so diag.txt follows clicks inside the opened island at once.
        var changed = _islandState?.State != content.State
                      || _islandState?.Tab != content.Tab
                      || _islandState?.Usage?.Period != content.Usage?.Period
                      || _islandState?.Usage?.Label != content.Usage?.Label
                      || _islandState?.Usage?.Chart?.TopText != content.Usage?.Chart?.TopText;
        _islandState = content;
        if (changed && _options.Diagnostics)
        {
            UpdateTrayIcon(); // also rewrites diag.txt, so island changes show up at once
        }
    }

    /// <summary>
    /// Verification only (<c>TRACK_POINTER_FILE</c>): line 1 "x,y" in screen pixels; optional lines "click"
    /// and "minimize" (each consumed once) and "fullscreen". Lets the top-edge reveal, clicks and full-screen hiding be checked
    /// end to end when the real mouse cannot be used (locked workstation).
    /// </summary>
    private (Point Point, bool Click, bool FullScreen, bool Minimize)? PointerOverride(bool consume = true)
    {
        if (_options.PointerFile is not { } file || !File.Exists(file))
        {
            return null;
        }

        try
        {
            var lines = File.ReadAllLines(file);
            var xy = lines.FirstOrDefault()?.Split(',');
            var point = xy is { Length: 2 } && double.TryParse(xy[0], out var x) && double.TryParse(xy[1], out var y) ? new Point(x, y) : new Point(-1, 9999);
            var click = lines.Contains("click");
            var minimize = lines.Contains("minimize");
            // "island:tab usage", "island:period week", "island:bar 17": the same calls the opened island's buttons and chart make.
            // Only the UI's pointer tick consumes the one-shot lines; the live update's full-screen check just peeks
            // (it used to swallow "click" now and then, found in the Phase 6 run).
            var island = lines.Where(l => l.StartsWith("island:", StringComparison.Ordinal) || l.StartsWith("popover:", StringComparison.Ordinal)).ToList();
            if (!consume)
            {
                return (point, false, lines.Contains("fullscreen"), false);
            }

            if (click || minimize || island.Count > 0)
            {
                File.WriteAllLines(file, lines.Where(l => l is not ("click" or "minimize") && !island.Contains(l)));
            }

            // "popover:period month", "popover:bar 23": the popover's tabs and chart, for rollover runs without a mouse.
            foreach (var command in island.Select(l => l.Split(':', 2)).Select(p => p[1].Split(' ', 2).Prepend(p[0]).ToArray()))
            {
                lock (_gate)
                {
                    switch (command)
                    {
                        case ["popover", "period", var period] when Enum.TryParse<Period>(period, true, out var p):
                            _vm.SelectedPeriod = p;
                            break;
                        case ["popover", "bar", var bar] when int.TryParse(bar, out var b):
                            _vm.SelectBucketCommand.Execute(b);
                            break;
                        case ["island", "tab", var tab] when Enum.TryParse<IslandTab>(tab, true, out var t):
                            _islandVm.SelectTab(t);
                            break;
                        case ["island", "period", var period] when Enum.TryParse<Period>(period, true, out var p):
                            _islandVm.SelectPeriod(p);
                            break;
                        case ["island", "bar", var bar] when int.TryParse(bar, out var b):
                            _islandVm.SelectBucket(b);
                            break;
                    }
                }
            }

            if (island.Count > 0)
            {
                RenderIsland();
            }

            return (point, click, lines.Contains("fullscreen"), minimize);
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>Verification: renders the island in each state to PNG files using the real state machine and view model.</summary>
    private void RenderIslandAndExit(string directory, PriceTable prices)
    {
        Directory.CreateDirectory(directory);
        lock (_gate)
        {
            _usage.Prices = prices;
            _vm.UsageRefreshed();
        }

        var clock = new ManualClock(DateTimeOffset.UtcNow);
        var machine = new IslandStateMachine(clock, () => _settings.Island);
        var vm = new IslandViewModel(machine, _store, _usage, _periods, clock, () => _settings.Island, () => _vm.ListedTools);
        var window = new IslandWindow { Left = -4000, Top = 0 };
        var claude = new AgentActivity(Tool.Claude, "demo-claude", clock.Now.AddSeconds(-754), clock.Now.AddMinutes(-60), "claude-opus-5-5");
        // Round 6 demo: sub-agents under Claude Code (one nested, one done) and one under Copilot.
        var withSubs = claude with
        {
            SubAgents =
            [
                new SubAgentActivity(Tool.Claude, "d1", "Explore", "Find where chart clicks are hit-tested", "claude-haiku-5-5", 1, clock.Now.AddSeconds(-72), null, 640_000),
                new SubAgentActivity(Tool.Claude, "d2", "general-purpose", "Update the popover tests", "claude-opus-5-5", 1, clock.Now.AddSeconds(-245), null, 1_400_000),
                new SubAgentActivity(Tool.Claude, "d3", "Explore", "List every caller of TrimMemory", "claude-haiku-5-5", 2, clock.Now.AddSeconds(-31), null, 120_000),
                new SubAgentActivity(Tool.Claude, "d4", "Explore", "Read the island tests", "claude-haiku-5-5", 1, clock.Now.AddSeconds(-400), clock.Now.AddSeconds(-155), 310_000),
            ],
        };
        var copilot = new AgentActivity(Tool.Copilot, "demo-copilot", clock.Now.AddSeconds(-95), clock.Now.AddMinutes(-20), "gpt-6.1-sol");

        void Save(string name)
        {
            IslandContent content;
            lock (_gate)
            {
                content = vm.Build();
            }

            File.WriteAllBytes(Path.Combine(directory, $"island-{name}.png"), window.Snapshot(content));
        }

        machine.Update([claude], false);
        Save("1-active-claude");
        machine.Update([claude, copilot], false);
        Save("2-two-agents");
        machine.Click();
        Save("3-opened");
        machine.Update([withSubs, copilot with { SubAgents = [new SubAgentActivity(Tool.Copilot, "q", "QA", "Dedicated QA specialist", "gpt-6.1-sol", 1, clock.Now.AddSeconds(-161), null, 520_000)] }], false);
        Save("3-subagents");
        machine.Escape();
        Save("3-subagents-compact");
        machine.Click();
        machine.Update([claude, copilot], false);
        // Usage tab (round 5): each period, then one bar drilled into (the last bar with usage this month).
        vm.SelectTab(IslandTab.Usage);
        foreach (var period in Enum.GetValues<Period>())
        {
            vm.SelectPeriod(period);
            Save($"3-usage-{period.ToString().ToLowerInvariant()}");
        }

        IslandContent month;
        lock (_gate)
        {
            month = vm.Build();
        }

        if (month.Usage?.Chart?.Bars.LastOrDefault(b => b.Segments.Count > 0) is { } busiest)
        {
            vm.SelectBucket(busiest.Index);
            Save("3-usage-month-drilled");
        }

        vm.SelectTab(IslandTab.Now);
        machine.Escape();
        clock.Now = clock.Now.AddSeconds(2);
        machine.Update([copilot], false);
        Save("4-claude-done-while-copilot-runs");
        machine.Update([], false);
        Save("5-copilot-done");
        clock.Now = clock.Now.AddSeconds(6);
        Save("6-idle");
        machine.Click();
        Save("7-opened-nothing-running");
        window.Close();
        Application.Current.Shutdown();
    }

    /// <summary>A clock the island renders can move forward.</summary>
    private sealed class ManualClock(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = start;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private void LoadPrices()
    {
        Task.Run(async () =>
        {
            var loaded = await _prices.LoadAsync();
            _ = _ui.BeginInvoke(() =>
            {
                lock (_gate)
                {
                    _usage.Prices = loaded.Table;
                    _vm.UsageRefreshed();
                }

                _priceNote = $"{loaded.Source} prices{(loaded.AsOfUtc is { } at ? $" from {at.ToLocalTime():yyyy-MM-dd HH:mm}" : "")}";
                UpdateTrayIcon(force: true);
            });
        });
    }

    private void UpdateTrayIcon(bool force = false)
    {
        if (_tray is null)
        {
            return;
        }

        var inTray = _vm.Visibility.Where(v => v.Value.InTray).Select(v => v.Key).ToHashSet();
        var running = _live.Where(l => l.Value.Working).Select(l => l.Key).ToHashSet();
        var spec = TrayIconModel.Build(inTray, running, _settings.IconLayout, _settings.IdleLook, _settings.NothingRunningLook, TaskbarThemeFor(_options));
        var size = TrayIconRenderer.PixelSize();
        var key = $"{spec.CacheKey}@{size}";
        if (force || key != _iconKey)
        {
            _tray.Icon = _renderer.Icon(spec, size);
            _tray.ToolTipText = spec.Tooltip;
            _iconKey = key;
        }

        if (_options.Diagnostics)
        {
            WriteDiagnostics(spec, inTray, running);
        }
    }

    private static string ScreenRect(FrameworkElement? e)
    {
        if (e is not { IsVisible: true } || PresentationSource.FromVisual(e) is null)
        {
            return "-";
        }

        var a = e.PointToScreen(new Point(0, 0));
        var b = e.PointToScreen(new Point(e.ActualWidth, e.ActualHeight));
        return $"{a.X:0},{a.Y:0},{b.X - a.X:0}x{b.Y - a.Y:0}";
    }

    private void WriteDiagnostics(TrayIconSpec spec, HashSet<Tool> inTray, HashSet<Tool> running)
    {
        var text = new StringBuilder()
            .AppendLine($"time: {DateTimeOffset.Now:O}")
            .AppendLine($"clock: {_time.GetLocalNow():yyyy-MM-dd ddd HH:mm:ss} (offset {(_time as ShiftedTimeProvider)?.Offset.ToString() ?? "none"})")
            .AppendLine($"tooltip: {spec.Tooltip}")
            .AppendLine($"icon: {spec.CacheKey}")
            .AppendLine($"inTray: {string.Join(",", inTray.Order())}")
            .AppendLine($"running: {string.Join(",", running.Order())}")
            .AppendLine($"prices: {_priceNote}")
            .AppendLine($"status: {_vm.StatusText}")
            .AppendLine($"period: {_vm.PeriodLabel}")
            .AppendLine($"island: {_island.State} | {_islandState?.MainText} | {_islandState?.MainExtra} | logos={string.Join(",", _islandState?.Logos ?? [])} | bubble={string.Join(",", _islandState?.Bubble ?? [])} | header={_islandState?.Header} | visible={_islandWindow?.IsVisible} | bounds={(_islandWindow is { IsVisible: true } iw ? $"{iw.Left:0},{iw.Top:0},{iw.ActualWidth:0}x{iw.ActualHeight:0}" : "-")}")
            .AppendLine($"island input: clicks from pointer file {_clicksFromFile}, from window {_clicksFromWindow}, escapes {_escapes}, minimized {_island.IsMinimized}, island active {_islandWindow?.IsActive}, last bring-to-front {Native.LastBringToFront}")
            .AppendLine($"working: {string.Join(", ", _island.Working.Select(w => $"{w.Tool}/{w.SessionId}/{w.Model}"))}");
        foreach (var r in _vm.LiveRows)
        {
            text.AppendLine($"live: {r.Name} | working={r.Working} | {r.Detail}");
        }

        foreach (var r in _vm.Rows.Append(_vm.TotalRow).OfType<ToolRowViewModel>())
        {
            text.AppendLine($"row: {r.Name} | {r.TokenText} | {r.SubText}");
        }

        // The opened island's tab, and on the Usage tab the same row format, so island and popover numbers can be compared line by line.
        text.AppendLine($"island-ui: {_islandWindow?.ScreenRects()}");
        foreach (var a in _island.Working)
        {
            foreach (var x in a.Subs)
            {
                text.AppendLine($"subagent: {a.Tool}/{a.SessionId} | {x.Name} | {x.Task} | {x.Model} | {x.Tokens} tokens | depth {x.Depth} | {(x.Running ? "running " + Format.Timer(DateTimeOffset.UtcNow - x.StartedUtc) : "done " + Format.Timer(x.EndedUtc!.Value - x.StartedUtc))}");
            }
        }

        text.AppendLine($"island-tab: {_islandState?.Tab} | period: {_islandState?.Usage?.Period} | label: {_islandState?.Usage?.Label} | compare: {_islandState?.Usage?.ComparisonText ?? "-"}");
        foreach (var r in _islandState?.Usage is { } u ? u.Rows.Append(u.TotalRow).OfType<ToolRowViewModel>() : [])
        {
            text.AppendLine($"island-row: {r.Name} | {r.TokenText} | {r.SubText}");
        }

        text.AppendLine($"chart: {(_vm.Chart is { } c ? $"{c.Title} | {c.TopText} | bars {c.Bars.Count}, now {c.Bars.FirstOrDefault(b => b.Current)?.Index}" : "hidden")} | compare: {_vm.ComparisonText ?? "-"} | label: {_vm.PeriodLabel}");
        // Screen pixels of the chart and the Hide/Show link while the popover is open, so real-mouse checks hit them exactly.
        text.AppendLine($"popover-ui: chart={ScreenRect(_popover?.ChartView)} | toggle={ScreenRect(_popover?.ChartToggle)} | bars={_vm.Chart?.Bars.Count ?? 0}");
        foreach (var bar in _vm.Chart?.Bars.Where(b => b.Segments.Count > 0) ?? [])
        {
            text.AppendLine($"bar {bar.Index}{(bar.Selected ? " (selected)" : "")}: {bar.Tooltip.Replace('\n', '/')}");
        }

        File.WriteAllText(Path.Combine(_options.DataDirectory, "diag.txt"), text.ToString());
    }

    private void TogglePopover()
    {
        if (_popover is { IsVisible: true })
        {
            _popover.HidePopover();
        }
        else
        {
            OpenPopover(pinned: false);
        }
    }

    private void OpenPopover(bool pinned)
    {
        if (_popover is null)
        {
            _popover = new PopoverWindow { DataContext = _vm, Dark = AppDark(_options) };
            _popover.RowHovered += (tool, row) => ShowCard(tool, row);
            _popover.RowLeft += () => _card?.Hide();
            _popover.IsVisibleChanged += (_, _) =>
            {
                _tick.IsEnabled = _popover.IsVisible;
                lock (_gate)
                {
                    _vm.IsShown = _popover.IsVisible;
                }

                if (!_popover.IsVisible)
                {
                    _card?.Hide();
                    _settingsStore.Save(_settings = _settings with { LastPeriod = _vm.SelectedPeriod });
                }
            };
        }

        _popover.Pinned = pinned;
        lock (_gate)
        {
            _vm.IsShown = true;
        }

        _vm.Tick();
        // Pinned (--open) opens at the right edge, like a tray popover; a click opens near the pointer.
        _popover.ShowNear(pinned ? new Point(double.MaxValue / 4, 0) : Native.CursorPixels());
    }

    private void ShowCard(Tool tool, FrameworkElement? row = null)
    {
        if (_popover is null)
        {
            return;
        }

        row ??= FindRow(_popover, tool);
        if (row is null)
        {
            return;
        }

        _card ??= new ModelCardWindow { Dark = AppDark(_options) };
        ModelBreakdownViewModel card;
        lock (_gate)
        {
            card = _vm.ModelBreakdown(tool);
        }

        _card.ShowFor(card, _popover, row);
    }

    private static FrameworkElement? FindRow(DependencyObject parent, Tool tool)
    {
        // The last match is the usage-table row (the live list comes first).
        FrameworkElement? found = null;
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is Border { Tag: Tool t } border && t == tool)
            {
                found = border;
            }

            found = FindRow(child, tool) ?? found;
        }

        return found;
    }

    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(Item("Refresh", () => Task.Run(() => _coordinator?.RefreshUsageNow())));

        var layout = new MenuItem { Header = "Icon layout" };
        foreach (var (name, value) in new[] { ("Triangle", IconLayout.Triangle), ("Row", IconLayout.Row), ("Running only", IconLayout.RunningOnly) })
        {
            var item = new MenuItem { Header = name, IsCheckable = true, IsChecked = _settings.IconLayout == value };
            item.Click += (_, _) =>
            {
                ChangeSettings(_settings with { IconLayout = value });
                foreach (MenuItem other in layout.Items)
                {
                    other.IsChecked = ReferenceEquals(other, item);
                }
            };
            layout.Items.Add(item);
        }

        menu.Items.Add(layout);

        var startup = new MenuItem { Header = "Start with Windows", IsCheckable = true, IsChecked = _settings.StartWithWindows };
        startup.Click += (_, _) =>
        {
            ChangeSettings(_settings with { StartWithWindows = startup.IsChecked });
            if (_options.ManageStartup)
            {
                new StartupManager(new StartupRegistry(), Environment.ProcessPath!).Apply(startup.IsChecked);
            }
        };
        menu.Items.Add(startup);

        var island = new MenuItem { Header = "Show top island", IsCheckable = true, IsChecked = _settings.Island.Enabled };
        island.Click += (_, _) =>
        {
            ChangeSettings(_settings with { Island = _settings.Island with { Enabled = island.IsChecked } });
            RenderIsland();
        };
        menu.Items.Add(island);
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Quit", () => Application.Current.Shutdown()));
        return menu;
    }

    private static MenuItem Item(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private void ChangeSettings(TrackSettings settings)
    {
        _settings = settings;
        _settingsStore.Save(settings);
        lock (_gate)
        {
            _vm.ApplySettings(settings);
        }

        UpdateTrayIcon(force: true);
    }

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e) => _ui.BeginInvoke(() =>
    {
        ApplyTheme();
        UpdateTrayIcon(force: true);
    });

    private void ApplyTheme()
    {
        var dark = AppDark(_options);
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        dictionaries[0] = new ResourceDictionary { Source = new Uri(dark ? "Themes/Dark.xaml" : "Themes/Light.xaml", UriKind.Relative) };
        if (_popover is not null)
        {
            _popover.Dark = dark;
            if (_popover.IsLoaded)
            {
                _popover.ApplyBackdrop();
            }
        }

        if (_card is not null)
        {
            _card.Dark = dark;
        }
    }
}
