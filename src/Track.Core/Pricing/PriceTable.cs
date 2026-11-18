using System.Text.Json;
using System.Text.RegularExpressions;
using Track.Core.Ingest;

namespace Track.Core.Pricing;

/// <summary>Price per token in US dollars. Reasoning tokens are billed as output.</summary>
public sealed record ModelPrice(decimal Input, decimal Output, decimal CacheWrite, decimal CacheRead)
{
    public decimal Cost(TokenCounts t) =>
        (t.Input * Input) + (t.CacheWrite * CacheWrite) + (t.CacheRead * CacheRead) + (t.Output * Output);
}

/// <summary>
/// Model prices in LiteLLM's <c>model_prices_and_context_window.json</c> format. Estimates are
/// API-equivalent list prices: subscriptions are not billed per token, and long-context surcharges
/// and 1-hour cache-write rates are not applied.
/// </summary>
public sealed partial class PriceTable
{
    private const string BundledResource = "Track.Core.prices.bundled.json";
    private const string CopilotPrefix = "github_copilot/";

    private static readonly Lazy<PriceTable> BundledTable = new(() =>
    {
        using var stream = typeof(PriceTable).Assembly.GetManifestResourceStream(BundledResource)!;
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd())!;
    });

    private readonly Dictionary<string, ModelPrice> _prices;

    private PriceTable(Dictionary<string, ModelPrice> prices) => _prices = prices;

    /// <summary>The prices shipped inside Track, used when there is no download or cache.</summary>
    public static PriceTable Bundled => BundledTable.Value;

    public int Count => _prices.Count;

    /// <summary>Reads LiteLLM-format JSON. Returns null when it is not JSON or has no usable prices.</summary>
    public static PriceTable? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var prices = new Dictionary<string, ModelPrice>(StringComparer.OrdinalIgnoreCase);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var entry in doc.RootElement.EnumerateObject())
                {
                    if (ReadPrice(entry.Value) is { } price)
                    {
                        prices[entry.Name] = price;
                    }
                }
            }

            return prices.Count > 0 ? new PriceTable(prices) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Price for a model name as it appears in the logs, or null when unknown.</summary>
    public ModelPrice? Find(string model)
    {
        foreach (var name in ModelNames.Candidates(model))
        {
            if (_prices.TryGetValue(name, out var price) || _prices.TryGetValue(CopilotPrefix + name, out price))
            {
                return price;
            }
        }

        return null;
    }

    /// <summary>Estimated cost in US dollars, or null when the model has no known price.</summary>
    public decimal? Cost(string model, TokenCounts tokens) => Find(model)?.Cost(tokens);

    private static ModelPrice? ReadPrice(JsonElement entry)
    {
        var input = Number(entry, "input_cost_per_token");
        var output = Number(entry, "output_cost_per_token");
        if (input is null || output is null)
        {
            return null;
        }

        // Missing cache prices fall back to the plain input price (never cheaper than reality).
        return new ModelPrice(
            input.Value,
            output.Value,
            Number(entry, "cache_creation_input_token_cost") ?? input.Value,
            Number(entry, "cache_read_input_token_cost") ?? input.Value);
    }

    private static decimal? Number(JsonElement entry, string name) =>
        entry.ValueKind == JsonValueKind.Object
        && entry.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetDecimal(out var number)
        && number >= 0
            ? number
            : null;
}

/// <summary>Turns a model name from the logs into the names a price list may use.</summary>
public static partial class ModelNames
{
    /// <summary>
    /// Lowercased, without provider prefix (<c>anthropic/</c>) or date suffix (<c>-20250514</c>).
    /// Claude names written with dots (<c>claude-opus-5.5</c>, as Copilot does) also try dashes.
    /// </summary>
    public static IReadOnlyList<string> Candidates(string model)
    {
        var name = model.Trim().ToLowerInvariant();
        name = name[(name.LastIndexOf('/') + 1)..];
        name = DateSuffix().Replace(name, "");

        return name.StartsWith("claude-", StringComparison.Ordinal) && name.Contains('.')
            ? [name, name.Replace('.', '-')]
            : [name];
    }

    [GeneratedRegex(@"-(\d{8}|\d{4}-\d{2}-\d{2})$")]
    private static partial Regex DateSuffix();
}
