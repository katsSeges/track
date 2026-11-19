using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
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
    public void A_legacy_1_1_settings_file_loads_and_ignores_unknown_properties()
    {
        const string legacy = """{"startWithWindows":false,"lastPeriod":"Week","iconLayout":"Row","visibility":{"trayLimitWeek":42},"futureProperty":"ignored"}""";
        var fs = new MockFileSystem(new Dictionary<string, MockFileData> { [Path] = new(legacy) });

        var loaded = new SettingsStore(fs, Path).Load();

        Assert.False(loaded.StartWithWindows);
        Assert.Equal(Period.Week, loaded.LastPeriod);
        Assert.Equal(IconLayout.Row, loaded.IconLayout);
        Assert.Equal(42, loaded.Visibility.TrayLimitWeek);
        Assert.Equal(ProviderMode.Auto, loaded.Providers.ModeFor(Tool.Claude));
        Assert.Equal(ProviderMode.Auto, loaded.Providers.ModeFor(Tool.Gemini));
        Assert.Equal(ProviderMode.Off, loaded.Providers.ModeFor(Tool.Ollama));
    }

    [Fact]
    public void An_unknown_enum_value_uses_the_existing_whole_file_default_fallback()
    {
        const string content = """{"startWithWindows":false,"providers":{"gemini":"future-mode"}}""";
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
