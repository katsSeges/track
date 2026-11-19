using System.Globalization;
using Track.Core.Live;
using Track.Core.Providers;
using Track.Core.Settings;

namespace Track.Core.Display;

/// <summary>The words and numbers the Settings window shows and reads back (tested here; the window only draws them).</summary>
public static class SettingsText
{
    /// <summary>
    /// Reads a token limit as people write it: <c>120k</c>, <c>1.5M</c>, <c>2B</c>, <c>40000</c> or <c>40,000</c>.
    /// Blank is "no limit" (null). False for anything else, including zero or less.
    /// </summary>
    public static bool TryParseTokens(string? text, out long? tokens)
    {
        tokens = null;
        var value = (text ?? "").Trim().Replace(",", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal);
        if (value.Length == 0)
        {
            return true;
        }

        var multiplier = char.ToUpperInvariant(value[^1]) switch
        {
            'K' => 1_000m,
            'M' => 1_000_000m,
            'B' => 1_000_000_000m,
            _ => 1m,
        };
        var number = multiplier == 1m ? value : value[..^1];
        // Up to a quadrillion tokens: far beyond any budget, and the product cannot overflow.
        if (!decimal.TryParse(number, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount)
            || amount > 1_000_000_000_000_000m
            || amount * multiplier < 1)
        {
            return false;
        }

        tokens = (long)decimal.Round(amount * multiplier);
        return true;
    }

    /// <summary>A token limit the short way: 120k, 1.5M, 2B; small numbers as they are; null as blank.</summary>
    public static string FormatTokens(long? tokens) => tokens switch
    {
        null => "",
        >= 1_000_000_000 => Short(tokens.Value / 1_000_000_000m, "B"),
        >= 1_000_000 => Short(tokens.Value / 1_000_000m, "M"),
        >= 1_000 => Short(tokens.Value / 1_000m, "k"),
        _ => tokens.Value.ToString(CultureInfo.InvariantCulture),
    };

    /// <summary>Reads a spend limit in dollars (<c>75</c>, <c>$7.50</c>). Blank is "no limit"; zero or less is false.</summary>
    public static bool TryParseSpend(string? text, out decimal? spend)
    {
        spend = null;
        var value = (text ?? "").Trim().TrimStart('$').Trim();
        if (value.Length == 0)
        {
            return true;
        }

        if (!decimal.TryParse(value, NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var amount)
            || amount <= 0)
        {
            return false;
        }

        spend = amount;
        return true;
    }

    public static string FormatSpend(decimal? spend) => spend?.ToString("0.##", CultureInfo.InvariantCulture) ?? "";

    /// <summary>The line under a provider's name in Settings: whether it runs, when it was last used, or why it shows nothing.</summary>
    public static string ProviderStatus(ProviderInfo provider, ProviderMode mode, LiveStatus? live, DateTimeOffset? lastUsedUtc, DateTimeOffset now)
    {
        if (mode == ProviderMode.Off)
        {
            return provider.Capabilities.HasTokens ? "Off · its logs are not read" : "Off";
        }

        if (!provider.Capabilities.HasTokens)
        {
            return live is { ProcessRunning: true }
                ? live.LastModel is { Length: > 0 } model ? $"Running · {model}" : "Running"
                : "Status only, no token data";
        }

        if (live is { Working: true, ActiveSessions: var sessions })
        {
            return sessions == 1 ? "Running · 1 session" : $"Running · {sessions} sessions";
        }

        return lastUsedUtc is { } last ? $"Used {Ago(now - last)}" : "No usage in the last 90 days";
    }

    private static string Ago(TimeSpan elapsed) => elapsed switch
    {
        { TotalMinutes: < 1 } => "just now",
        { TotalHours: < 1 } => $"{(int)elapsed.TotalMinutes} min ago",
        { TotalDays: < 1 } => $"{(int)elapsed.TotalHours} h ago",
        { TotalDays: < 2 } => "yesterday",
        _ => $"{(int)elapsed.TotalDays} days ago",
    };

    private static string Short(decimal value, string unit) =>
        decimal.Round(value, 1, MidpointRounding.AwayFromZero).ToString("0.#", CultureInfo.InvariantCulture) + unit;
}
