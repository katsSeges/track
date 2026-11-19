using Track.Core.Display;
using Track.Core.Live;
using Track.Core.Providers;
using Track.Core.Settings;

namespace Track.Core.Tests.Display;

public sealed class SettingsTextTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("120k", 120_000L)]
    [InlineData("1.5M", 1_500_000L)]
    [InlineData("2b", 2_000_000_000L)]
    [InlineData(" 40,000 ", 40_000L)]
    [InlineData("40 000", 40_000L)]
    [InlineData("7", 7L)]
    [InlineData("0.0015k", 2L)]
    public void Token_limits_read_the_short_way_people_write_them(string text, long expected)
    {
        Assert.True(SettingsText.TryParseTokens(text, out var tokens));
        Assert.Equal(expected, tokens);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void A_blank_token_limit_means_no_limit(string? text)
    {
        Assert.True(SettingsText.TryParseTokens(text, out var tokens));
        Assert.Null(tokens);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("0.2")]
    [InlineData("-5k")]
    [InlineData("lots")]
    [InlineData("k")]
    [InlineData("12x")]
    [InlineData("2000000000000000000")]
    public void Invalid_or_non_positive_token_limits_are_rejected(string text)
    {
        Assert.False(SettingsText.TryParseTokens(text, out _));
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData(999L, "999")]
    [InlineData(1_000L, "1k")]
    [InlineData(120_000L, "120k")]
    [InlineData(1_500_000L, "1.5M")]
    [InlineData(2_000_000_000L, "2B")]
    public void Token_limits_are_shown_the_short_way(long? tokens, string expected)
    {
        Assert.Equal(expected, SettingsText.FormatTokens(tokens));
    }

    [Theory]
    [InlineData("75", 75.0)]
    [InlineData("$7.50", 7.5)]
    [InlineData(" $ 1,200 ", 1200.0)]
    public void Spend_limits_read_dollars(string text, double expected)
    {
        Assert.True(SettingsText.TryParseSpend(text, out var spend));
        Assert.Equal((decimal)expected, spend);
        Assert.Equal(((decimal)expected).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture), SettingsText.FormatSpend(spend));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("ten")]
    public void Invalid_spend_limits_are_rejected(string text)
    {
        Assert.False(SettingsText.TryParseSpend(text, out _));
    }

    [Fact]
    public void A_blank_spend_limit_means_no_limit()
    {
        Assert.True(SettingsText.TryParseSpend(" ", out var spend));
        Assert.Null(spend);
        Assert.True(SettingsText.TryParseSpend(null, out spend));
        Assert.Null(spend);
        Assert.Equal("", SettingsText.FormatSpend(null));
    }

    [Fact]
    public void Provider_status_says_running_last_used_or_why_nothing_shows()
    {
        var claude = ProviderCatalog.Get(Tool.Claude);
        var ollama = ProviderCatalog.Get(Tool.Ollama);
        LiveStatus Live(bool working, int sessions = 0, bool process = false, string? model = null) =>
            new(Tool.Claude, working, sessions, 0, process, model, null);

        Assert.Equal("Running · 1 session", Status(claude, Live(true, 1), null));
        Assert.Equal("Running · 3 sessions", Status(claude, Live(true, 3), null));
        Assert.Equal("Used just now", Status(claude, Live(false), Now.AddSeconds(-20)));
        Assert.Equal("Used 12 min ago", Status(claude, null, Now.AddMinutes(-12)));
        Assert.Equal("Used 2 h ago", Status(claude, null, Now.AddHours(-2.5)));
        Assert.Equal("Used yesterday", Status(claude, null, Now.AddHours(-30)));
        Assert.Equal("Used 6 days ago", Status(claude, null, Now.AddDays(-6.2)));
        Assert.Equal("No usage in the last 90 days", Status(claude, null, null));
        Assert.Equal("Off · its logs are not read", SettingsText.ProviderStatus(claude, ProviderMode.Off, null, null, Now));

        Assert.Equal("Running · llama3", Status(ollama, Live(false, process: true, model: "llama3"), null));
        Assert.Equal("Running", Status(ollama, Live(false, process: true, model: ""), null));
        Assert.Equal("Running", Status(ollama, Live(false, process: true, model: null), null));
        Assert.Equal("Status only, no token data", Status(ollama, Live(false), null));
        Assert.Equal("Status only, no token data", Status(ollama, null, null));
        Assert.Equal("Off", SettingsText.ProviderStatus(ollama, ProviderMode.Off, null, null, Now));

        static string Status(ProviderInfo provider, LiveStatus? live, DateTimeOffset? last) =>
            SettingsText.ProviderStatus(provider, ProviderMode.Auto, live, last, Now);
    }
}
