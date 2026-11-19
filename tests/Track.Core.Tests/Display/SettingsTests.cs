using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Track.Core;
using Track.Core.Display;
using Track.Core.Settings;
using Track.Core.Usage;

namespace Track.Core.Tests.Display;

public class SettingsTests
{
    private const string Path = @"C:\appdata\Track\settings.json";

    [Fact]
    public void Defaults_are_the_mockup_choices()
    {
        var s = new TrackSettings();

        Assert.Equal(
            (true, Period.Today, 30, IconLayout.Triangle, IdleLook.GreyMark, NothingRunningLook.AllGrey, true),
            (s.StartWithWindows, s.LastPeriod, s.RefreshSeconds, s.IconLayout, s.IdleLook, s.NothingRunningLook, s.RowLogos));
        Assert.Equal(new VisibilitySettings(), s.Visibility);
        Assert.Equal(ThemeMode.System, s.Appearance.Theme);
        Assert.False(s.Behavior.NotificationsEnabled);
        // A global shortcut takes keys from every app: off until the user turns it on.
        Assert.False(s.Behavior.HotkeyEnabled);
        Assert.Equal("Ctrl+Alt+Shift+T", s.Behavior.HotkeyGesture);
    }

    [Fact]
    public void Settings_view_model_updates_and_persists_all_three_tabs()
    {
        var fs = new MockFileSystem();
        var store = new SettingsStore(fs, Path);
        var vm = new SettingsViewModel(new TrackSettings());
        var changed = new List<TrackSettings>();
        vm.SettingsChanged += (_, settings) => changed.Add(settings);

        vm.SelectedTab = SettingsTab.Providers;
        vm.SetTheme(ThemeMode.Dark);
        vm.SetStartWithWindows(false);
        vm.SetProviderColors(Tool.Gemini, "#112233", "#445566");
        vm.SetProviderMode(Tool.Gemini, ProviderMode.On);
        vm.SetChatTarget(Tool.Gemini, "gemini-cli");
        vm.SetNotificationsEnabled(true);
        vm.SetHotkey(false, "not a hotkey");
        vm.SetBudgetEnabled(true);
        vm.SetBudgetPeriod(BudgetPeriod.Month);
        vm.SetBudgetThreshold(null, new BudgetThreshold(25, 1000));
        vm.SetBudgetThreshold(Tool.Gemini, new BudgetThreshold(5, 400));

        Assert.Equal(SettingsTab.Providers, vm.SelectedTab);
        Assert.Equal(11, changed.Count);
        Assert.False(vm.Settings.StartWithWindows);
        Assert.Equal(new ProviderColorOverride("#112233", "#445566"), vm.Settings.Appearance.ProviderColors[Tool.Gemini]);
        Assert.Equal(ProviderMode.On, vm.Settings.Providers.ModeFor(Tool.Gemini));
        Assert.Equal("gemini-cli", vm.Settings.Providers.ChatTargetFor(Tool.Gemini));
        Assert.True(vm.Settings.Behavior.NotificationsEnabled);
        Assert.False(vm.Settings.Behavior.HotkeyEnabled);
        Assert.Equal("not a hotkey", vm.Settings.Behavior.HotkeyGesture);
        Assert.True(vm.Settings.Behavior.Budget.Enabled);
        Assert.Equal(BudgetPeriod.Month, vm.Settings.Behavior.Budget.Period);
        Assert.Equal(new BudgetThreshold(25, 1000), vm.Settings.Behavior.Budget.For(null));
        Assert.Equal(new BudgetThreshold(5, 400), vm.Settings.Behavior.Budget.For(Tool.Gemini));

        Assert.True(store.Save(vm.Settings));
        Assert.Equal(vm.Settings, store.Load());
        Assert.Equal(vm.Settings.Appearance.GetHashCode(), store.Load().Appearance.GetHashCode());

        vm.ResetProviderColors();
        Assert.Empty(vm.Settings.Appearance.ProviderColors);
        Assert.Equal(12, changed.Count);
    }

    [Fact]
    public void Settings_view_model_rejects_chat_targets_not_for_the_provider()
    {
        var vm = new SettingsViewModel(new TrackSettings());

        Assert.Throws<ArgumentException>(() => vm.SetChatTarget(Tool.Gemini, "copilot-app"));
    }

    [Theory]
    [InlineData(true, "")]
    [InlineData(true, "Ctrl+Alt")]
    [InlineData(true, "Ctrl+Ctrl+T")]
    [InlineData(true, "Ctrl+T+Y")]
    public void Settings_view_model_rejects_invalid_enabled_hotkeys(bool enabled, string gesture)
    {
        var vm = new SettingsViewModel(new TrackSettings());

        Assert.Throws<ArgumentException>(() => vm.SetHotkey(enabled, gesture));
    }

    [Fact]
    public void Settings_view_model_rejects_nonpositive_budget_limits()
    {
        var vm = new SettingsViewModel(new TrackSettings());

        Assert.Throws<ArgumentOutOfRangeException>(() => vm.SetBudgetThreshold(null, new BudgetThreshold(0, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => vm.SetBudgetThreshold(null, new BudgetThreshold(1, 0)));
        vm.SetBudgetThreshold(null, BudgetThreshold.Empty);
    }

    [Fact]
    public void Notifications_opt_out_persists_after_restart()
    {
        var fs = new MockFileSystem();
        var store = new SettingsStore(fs, Path);
        var vm = new SettingsViewModel(new TrackSettings());
        vm.SetNotificationsEnabled(true);
        vm.SetNotificationsEnabled(false);

        Assert.True(store.Save(vm.Settings));
        Assert.False(store.Load().Behavior.NotificationsEnabled);
    }

    [Theory]
    [InlineData("")]
    [InlineData("red")]
    [InlineData("x123456")]
    [InlineData("#GG0000")]
    public void Settings_view_model_rejects_invalid_colors(string color)
    {
        var vm = new SettingsViewModel(new TrackSettings());

        Assert.Throws<ArgumentException>(() => vm.SetProviderColors(Tool.Claude, color, "#000000"));
    }

    [Fact]
    public void Settings_view_model_does_not_notify_for_an_unchanged_value()
    {
        var vm = new SettingsViewModel(new TrackSettings());
        var changes = 0;
        vm.SettingsChanged += (_, _) => changes++;

        vm.SetTheme(ThemeMode.System);

        Assert.Equal(0, changes);
    }

    [Fact]
    public void Appearance_equality_and_hash_code_follow_the_provider_colors_content()
    {
        var empty = new AppearanceSettings();
        AppearanceSettings Custom() => empty with
        {
            ProviderColors = new Dictionary<Tool, ProviderColorOverride>
            {
                [Tool.Gemini] = new("#112233", "#445566"),
            },
        };

        // Equal content in two different dictionaries: equal, with the same hash (settings are compared to skip saves).
        Assert.Equal(Custom(), Custom());
        Assert.Equal(Custom().GetHashCode(), Custom().GetHashCode());
        Assert.Equal(new AppearanceSettings().GetHashCode(), empty.GetHashCode());
        Assert.NotEqual(empty, Custom());
    }

    [Fact]
    public void Provider_and_budget_settings_compare_dictionaries_by_content()
    {
        var providers = new ProviderSettings().WithChatTarget(Tool.Roo, "roo-vscode");
        var providersCopy = new ProviderSettings().WithChatTarget(Tool.Roo, "roo-vscode");
        Assert.Equal(providers, providersCopy);
        Assert.Equal(providers.GetHashCode(), providersCopy.GetHashCode());
        Assert.NotEqual(new ProviderSettings(), providers);
        Assert.Null(new ProviderSettings().ChatTargetFor(Tool.Gemini));

        var budget = new BudgetSettings
        {
            Enabled = true,
            Period = BudgetPeriod.Month,
            Total = new BudgetThreshold(10, 100),
        }.WithThreshold(Tool.Roo, new BudgetThreshold(5, 50));
        var budgetCopy = new BudgetSettings
        {
            Enabled = true,
            Period = BudgetPeriod.Month,
            Total = new BudgetThreshold(10, 100),
            Providers = new Dictionary<Tool, BudgetThreshold> { [Tool.Roo] = new(5, 50) },
        };
        Assert.Equal(budget, budgetCopy);
        Assert.Equal(budget.GetHashCode(), budgetCopy.GetHashCode());
        Assert.NotEqual(new BudgetSettings(), budget);
        Assert.False(budget.Equals(null));
        Assert.False(providers.Equals(null));
        Assert.Equal(BudgetThreshold.Empty, new BudgetSettings().For(Tool.Roo));
        Assert.NotEqual(budget, budget with { Period = BudgetPeriod.Day });
        Assert.NotEqual(budget, budget with { Total = new BudgetThreshold(11, 100) });
        Assert.NotEqual(budget, budget with { Providers = [] });
        Assert.NotEqual(budget, budget with { Providers = new Dictionary<Tool, BudgetThreshold> { [Tool.Roo] = new(6, 50) } });
    }

    [Fact]
    public void Settings_view_model_updates_without_a_change_listener()
    {
        var vm = new SettingsViewModel(new TrackSettings());

        vm.SetTheme(ThemeMode.Dark);

        Assert.Equal(ThemeMode.Dark, vm.Settings.Appearance.Theme);
    }

    [Fact]
    public void Save_then_load_gives_the_same_settings()
    {
        var fs = new MockFileSystem();
        var store = new SettingsStore(fs, Path);
        var custom = new TrackSettings
        {
            StartWithWindows = false,
            LastPeriod = Period.Month,
            IconLayout = IconLayout.RunningOnly,
            Visibility = new VisibilitySettings { TrayRule = TrayRule.BelowEither, TrayLimitWeek = 1 },
        };

        Assert.True(store.Save(custom));

        Assert.Equal(custom, store.Load());
        Assert.Contains("\"iconLayout\": \"RunningOnly\"", fs.File.ReadAllText(Path));
        Assert.False(fs.File.Exists(Path + ".tmp"));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("{broken")]
    public void Empty_or_broken_files_give_defaults(string content)
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData> { [Path] = new(content) });

        Assert.Equal(new TrackSettings(), new SettingsStore(fs, Path).Load());
    }

    [Fact]
    public void Missing_fields_keep_their_defaults()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData> { [Path] = new("""{"rowLogos": false}""") });

        Assert.Equal(new TrackSettings { RowLogos = false }, new SettingsStore(fs, Path).Load());
    }

    [Fact]
    public void The_v1_1_settings_serializer_fixture_loads_with_current_defaults()
    {
        var fixture = File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", "settings-v1.1.0.json"));
        var fs = new MockFileSystem(new Dictionary<string, MockFileData> { [Path] = new(fixture) });

        var loaded = new SettingsStore(fs, Path).Load();

        Assert.Equal(new TrackSettings(), loaded);
    }

    [Fact]
    public void Unknown_settings_properties_are_ignored()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData> { [Path] = new("""{"futureProperty":"ignored"}""") });

        Assert.Equal(new TrackSettings(), new SettingsStore(fs, Path).Load());
    }

    [Fact]
    public void A_file_with_the_dropped_accent_and_material_keeps_everything_else()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path] = new("""{"appearance":{"theme":"Dark","material":"MicaAlt","accentColor":"#C450B8"},"startWithWindows":false}"""),
        });

        var loaded = new SettingsStore(fs, Path).Load();

        Assert.Equal((ThemeMode.Dark, false), (loaded.Appearance.Theme, loaded.StartWithWindows));
        Assert.Empty(loaded.Appearance.ProviderColors);
    }

    [Fact]
    public void Partial_sections_keep_the_defaults_of_the_fields_they_leave_out()
    {
        // With init-only properties the JSON source generator set missing fields to null (a crash later).
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path] = new("""{"appearance":{},"behavior":{"notificationsEnabled":true,"budget":{"enabled":true}}}"""),
        });

        var loaded = new SettingsStore(fs, Path).Load();

        Assert.Empty(loaded.Appearance.ProviderColors);
        Assert.True(loaded.Behavior.NotificationsEnabled);
        Assert.Equal("Ctrl+Alt+Shift+T", loaded.Behavior.HotkeyGesture);
        Assert.Equal((true, BudgetPeriod.Day, BudgetThreshold.Empty), (loaded.Behavior.Budget.Enabled, loaded.Behavior.Budget.Period, loaded.Behavior.Budget.Total));
        Assert.Empty(loaded.Behavior.Budget.Providers);
    }

    [Theory]
    [InlineData("""{"providers":{"gemini":"future-mode"}}""")]
    [InlineData("""{"appearance":{"theme":"FutureTheme"}}""")]
    [InlineData("""{"appearance":{"material":"FutureMaterial"}}""")]
    public void An_unknown_enum_value_uses_the_existing_whole_file_default_fallback(string content)
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData> { [Path] = new(content) });

        Assert.Equal(new TrackSettings(), new SettingsStore(fs, Path).Load());
    }

    [Fact]
    public void A_missing_file_gives_defaults()
    {
        Assert.Equal(new TrackSettings(), new SettingsStore(new MockFileSystem(), Path).Load());
    }

    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    public void Disk_trouble_gives_defaults_on_load_and_false_on_save(Type exception)
    {
        var fs = Substitute.For<IFileSystem>();
        fs.File.Exists(Path).Returns(true);
        fs.File.ReadAllText(Path).Throws((Exception)Activator.CreateInstance(exception)!);
        fs.Path.GetDirectoryName(Path).Returns(@"C:\appdata\Track");
        fs.File.When(f => f.WriteAllText(Arg.Any<string>(), Arg.Any<string>())).Throw((Exception)Activator.CreateInstance(exception)!);
        var store = new SettingsStore(fs, Path);

        Assert.Equal(new TrackSettings(), store.Load());
        Assert.False(store.Save(new TrackSettings()));
    }

    [Fact]
    public void Unexpected_errors_are_not_hidden()
    {
        var fs = Substitute.For<IFileSystem>();
        fs.File.Exists(Path).Returns(true);
        fs.File.ReadAllText(Path).Throws(new InvalidOperationException());
        fs.Path.GetDirectoryName(Path).Returns(@"C:\appdata\Track");
        fs.File.When(f => f.WriteAllText(Arg.Any<string>(), Arg.Any<string>())).Throw(new InvalidOperationException());
        var store = new SettingsStore(fs, Path);

        Assert.Throws<InvalidOperationException>(() => store.Load());
        Assert.Throws<InvalidOperationException>(() => store.Save(new TrackSettings()));
    }

    [Fact]
    public void Default_path_is_in_the_track_folder()
    {
        Assert.Equal(@"C:\L\Track\settings.json", SettingsStore.DefaultPath(@"C:\L"));
    }

    [Fact]
    public void Startup_entry_is_added_fixed_or_removed_only_when_needed()
    {
        var registry = Substitute.For<IStartupRegistry>();
        var manager = new StartupManager(registry, @"C:\Programs\Track\Track.exe");
        Assert.Equal("\"C:\\Programs\\Track\\Track.exe\"", manager.Command);

        registry.Get().Returns((string?)null);
        manager.Apply(true);
        registry.Received(1).Set(manager.Command);

        registry.ClearReceivedCalls();
        registry.Get().Returns(manager.Command);
        manager.Apply(true);
        registry.DidNotReceiveWithAnyArgs().Set(default!);

        registry.Get().Returns("\"C:\\old\\Track.exe\"");
        manager.Apply(true);
        registry.Received(1).Set(manager.Command);

        registry.ClearReceivedCalls();
        manager.Apply(false);
        registry.Received(1).Remove();

        registry.ClearReceivedCalls();
        registry.Get().Returns((string?)null);
        manager.Apply(false);
        registry.DidNotReceive().Remove();
    }
}
