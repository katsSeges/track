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
    [InlineData(Tool.Gemini, "Gemini CLI", "gemini", "#4285F4", "#8AB4F8", false)]
    [InlineData(Tool.OpenCode, "OpenCode", "opencode", "#2D6A4F", "#6FCF97", false)]
    [InlineData(Tool.Cline, "Cline", "cline", "#0B8F87", "#55DAD0", false)]
    [InlineData(Tool.Roo, "Roo Code", "roo", "#A74BC8", "#D38AE8", false)]
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
        Assert.Contains(provider.ProcessNames, process => !string.IsNullOrWhiteSpace(process));
    }

    [Fact]
    public void Log_sources_match_the_original_three_provider_locations()
    {
        var home = @"C:\home";

        var sources = ProviderCatalog.LogSources(home);

        Assert.Collection(sources,
            s => Assert.Equal((Tool.Claude, Path.Combine(home, ".claude", "projects"), "*.jsonl", SessionIdFrom.FileName), (s.Tool, s.Root, s.Pattern, s.SessionFrom)),
            s => Assert.Equal((Tool.Claude, Path.Combine(home, ".config", "claude", "projects"), "*.jsonl", SessionIdFrom.FileName), (s.Tool, s.Root, s.Pattern, s.SessionFrom)),
            s => Assert.Equal((Tool.Codex, Path.Combine(home, ".codex", "sessions"), "rollout-*.jsonl", SessionIdFrom.FileName), (s.Tool, s.Root, s.Pattern, s.SessionFrom)),
            s => Assert.Equal((Tool.Codex, Path.Combine(home, ".codex", "archived_sessions"), "rollout-*.jsonl", SessionIdFrom.FileName), (s.Tool, s.Root, s.Pattern, s.SessionFrom)),
            s => Assert.Equal((Tool.Copilot, Path.Combine(home, ".copilot", "session-state"), "events.jsonl", SessionIdFrom.ParentFolder), (s.Tool, s.Root, s.Pattern, s.SessionFrom)));
        Assert.Empty(ProviderCatalog.Get(Tool.Gemini).Sources(home));
        Assert.Empty(ProviderCatalog.Get(Tool.OpenCode).Sources(home));
        Assert.Empty(ProviderCatalog.Get(Tool.Cline).Sources(home));
        Assert.Empty(ProviderCatalog.Get(Tool.Roo).Sources(home));
        Assert.Empty(ProviderCatalog.Get(Tool.Ollama).Sources(home));
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
