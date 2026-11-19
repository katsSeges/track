using Track.Core.Ingest;
using Track.Core.Providers;
using Track.Core.Settings;
using Track.Core.Tray;

namespace Track.Core.Tests.Providers;

public class ProviderCatalogTests
{
    [Fact]
    public void Catalog_order_and_legacy_provider_order_are_stable()
    {
        Assert.Equal(
            [Tool.Claude, Tool.Codex, Tool.Copilot, Tool.Gemini, Tool.OpenCode, Tool.Cline, Tool.Roo, Tool.Ollama],
            ProviderCatalog.Order);
        Assert.Equal([Tool.Claude, Tool.Codex, Tool.Copilot], ProviderCatalog.LegacyOrder);
        Assert.Equal(ProviderCatalog.Order.Count, ProviderCatalog.All.Count);
    }

    [Theory]
    [InlineData(Tool.Claude, "Claude Code", "claude", "#D97757", "#E08560", true)]
    [InlineData(Tool.Codex, "Codex", "codex", "#2B2B2E", "#E4E4EA", true)]
    [InlineData(Tool.Copilot, "Copilot", "copilot", "#7A4FD6", "#A98BFF", true)]
    [InlineData(Tool.Gemini, "Gemini CLI", "gemini", "#4285F4", "#8AB4F8", true)]
    [InlineData(Tool.OpenCode, "OpenCode", "opencode", "#2D6A4F", "#6FCF97", true)]
    [InlineData(Tool.Cline, "Cline", "cline", "#0B8F87", "#55DAD0", true)]
    [InlineData(Tool.Roo, "Roo Code", "roo", "#A74BC8", "#D38AE8", true)]
    [InlineData(Tool.Ollama, "Ollama", "ollama", "#454545", "#E4E4EA", false)]
    public void Catalog_has_display_color_and_capability_metadata(
        Tool tool,
        string name,
        string logoKey,
        string light,
        string dark,
        bool hasTokens)
    {
        var provider = ProviderCatalog.Get(tool);

        Assert.Equal((name, logoKey, true, hasTokens), (provider.Name, provider.LogoKey, provider.Capabilities.HasActivity, provider.Capabilities.HasTokens));
        Assert.Equal((light, dark), (provider.Colors.For(TaskbarTheme.Light), provider.Colors.For(TaskbarTheme.Dark)));
        Assert.All(provider.ProcessNames, process => Assert.False(string.IsNullOrWhiteSpace(process)));
    }

    [Fact]
    public void Log_sources_match_the_original_three_provider_locations()
    {
        var home = @"C:\home";

        var sources = ProviderCatalog.LogSources(home);

        Assert.Equal(13, sources.Count);
        Assert.Collection(sources.Take(5),
            s => Assert.Equal((Tool.Claude, Path.Combine(home, ".claude", "projects"), "*.jsonl", SessionIdFrom.FileName), (s.Tool, s.Root, s.Pattern, s.SessionFrom)),
            s => Assert.Equal((Tool.Claude, Path.Combine(home, ".config", "claude", "projects"), "*.jsonl", SessionIdFrom.FileName), (s.Tool, s.Root, s.Pattern, s.SessionFrom)),
            s => Assert.Equal((Tool.Codex, Path.Combine(home, ".codex", "sessions"), "rollout-*.jsonl", SessionIdFrom.FileName), (s.Tool, s.Root, s.Pattern, s.SessionFrom)),
            s => Assert.Equal((Tool.Codex, Path.Combine(home, ".codex", "archived_sessions"), "rollout-*.jsonl", SessionIdFrom.FileName), (s.Tool, s.Root, s.Pattern, s.SessionFrom)),
            s => Assert.Equal((Tool.Copilot, Path.Combine(home, ".copilot", "session-state"), "events.jsonl", SessionIdFrom.ParentFolder), (s.Tool, s.Root, s.Pattern, s.SessionFrom)));
        var gemini = Assert.Single(ProviderCatalog.Get(Tool.Gemini).Sources(home));
        Assert.Equal((Tool.Gemini, Path.Combine(home, ".gemini", "tmp"), "session-*.jsonl", SessionIdFrom.FileName, LogReadMode.GeminiJsonLines),
            (gemini.Tool, gemini.Root, gemini.Pattern, gemini.SessionFrom, gemini.Mode));

        var opencode = Assert.Single(ProviderCatalog.Get(Tool.OpenCode).Sources(home));
        Assert.Equal((Tool.OpenCode, SessionIdFrom.FileName, LogReadMode.OpenCodeDatabase),
            (opencode.Tool, opencode.SessionFrom, opencode.Mode));
        Assert.NotEmpty(opencode.Root);

        Assert.Equal(3, ProviderCatalog.Get(Tool.Cline).Sources(home).Count);
        Assert.Equal(3, ProviderCatalog.Get(Tool.Roo).Sources(home).Count);
        Assert.All(ProviderCatalog.Get(Tool.Cline).Sources(home), source =>
            Assert.Equal((Tool.Cline, "ui_messages.json", SessionIdFrom.ParentFolder, LogReadMode.JsonDocument),
                (source.Tool, source.Pattern, source.SessionFrom, source.Mode)));
        Assert.All(ProviderCatalog.Get(Tool.Roo).Sources(home), source =>
            Assert.Equal((Tool.Roo, "ui_messages.json", SessionIdFrom.ParentFolder, LogReadMode.JsonDocument),
                (source.Tool, source.Pattern, source.SessionFrom, source.Mode)));
        Assert.Empty(ProviderCatalog.Get(Tool.Ollama).Sources(home));
    }

    [Fact]
    public void OpenCode_database_configuration_resolves_default_relative_absolute_and_memory_paths()
    {
        var oldXdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        var oldAppData = Environment.GetEnvironmentVariable("APPDATA");
        var oldDatabase = Environment.GetEnvironmentVariable("OPENCODE_DB");
        try
        {
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", null);
            Environment.SetEnvironmentVariable("APPDATA", null);
            Environment.SetEnvironmentVariable("OPENCODE_DB", null);
            var defaultSource = Assert.Single(ProviderCatalog.Get(Tool.OpenCode).Sources(@"C:\home"));
            Assert.Equal((Path.Combine(@"C:\home", ".local", "share", "opencode"), "opencode*.db*"),
                (defaultSource.Root, defaultSource.Pattern));

            Environment.SetEnvironmentVariable("XDG_DATA_HOME", @"C:\home\data");
            Environment.SetEnvironmentVariable("OPENCODE_DB", " ");
            var xdgSource = Assert.Single(ProviderCatalog.Get(Tool.OpenCode).Sources(@"C:\home"));
            Assert.Equal((Path.Combine(@"C:\home\data", "opencode"), "opencode*.db*"), (xdgSource.Root, xdgSource.Pattern));

            // OpenCode never uses %APPDATA%, on Windows too.
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", null);
            Environment.SetEnvironmentVariable("APPDATA", @"C:\home\AppData\Roaming");
            var appDataSource = Assert.Single(ProviderCatalog.Get(Tool.OpenCode).Sources(@"C:\home"));
            Assert.Equal((Path.Combine(@"C:\home", ".local", "share", "opencode"), "opencode*.db*"),
                (appDataSource.Root, appDataSource.Pattern));

            Environment.SetEnvironmentVariable("XDG_DATA_HOME", @"C:\home\data");
            Environment.SetEnvironmentVariable("OPENCODE_DB", "custom.db");
            var relativeSource = Assert.Single(ProviderCatalog.Get(Tool.OpenCode).Sources(@"C:\home"));
            Assert.Equal((Path.Combine(@"C:\home\data", "opencode"), "custom.db*"), (relativeSource.Root, relativeSource.Pattern));

            Environment.SetEnvironmentVariable("OPENCODE_DB", @"C:\custom\opencode.db");
            var absoluteSource = Assert.Single(ProviderCatalog.Get(Tool.OpenCode).Sources(@"C:\home"));
            Assert.Equal((@"C:\custom", "opencode.db*"), (absoluteSource.Root, absoluteSource.Pattern));

            Environment.SetEnvironmentVariable("OPENCODE_DB", ":memory:");
            Assert.Empty(ProviderCatalog.Get(Tool.OpenCode).Sources(@"C:\home"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", oldXdgDataHome);
            Environment.SetEnvironmentVariable("APPDATA", oldAppData);
            Environment.SetEnvironmentVariable("OPENCODE_DB", oldDatabase);
        }
    }

    [Fact]
    public void Unknown_tool_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ProviderCatalog.Get((Tool)99));
    }

    [Theory]
    [InlineData(Tool.Claude, ProviderMode.Auto)]
    [InlineData(Tool.Codex, ProviderMode.Auto)]
    [InlineData(Tool.Copilot, ProviderMode.Auto)]
    [InlineData(Tool.Gemini, ProviderMode.Auto)]
    [InlineData(Tool.OpenCode, ProviderMode.Auto)]
    [InlineData(Tool.Cline, ProviderMode.Auto)]
    [InlineData(Tool.Roo, ProviderMode.Auto)]
    [InlineData(Tool.Ollama, ProviderMode.Off)]
    public void Provider_modes_have_compatible_defaults(Tool tool, ProviderMode expected) =>
        Assert.Equal(expected, new ProviderSettings().ModeFor(tool));

    [Theory]
    [InlineData(Tool.Claude)]
    [InlineData(Tool.Codex)]
    [InlineData(Tool.Copilot)]
    [InlineData(Tool.Gemini)]
    [InlineData(Tool.OpenCode)]
    [InlineData(Tool.Cline)]
    [InlineData(Tool.Roo)]
    [InlineData(Tool.Ollama)]
    public void Provider_mode_can_be_changed_immutably(Tool tool)
    {
        var defaults = new ProviderSettings();

        var changed = defaults.WithMode(tool, ProviderMode.On);

        Assert.Equal(ProviderMode.On, changed.ModeFor(tool));
        Assert.Equal(tool == Tool.Ollama ? ProviderMode.Off : ProviderMode.Auto, defaults.ModeFor(tool));
    }

    [Fact]
    public void Provider_settings_reject_unknown_tools()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProviderSettings().ModeFor((Tool)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProviderSettings().WithMode((Tool)99, ProviderMode.On));
    }
}
