using Track.Core.Ingest;
using Track.Core.Pricing;

namespace Track.Core.Tests.Pricing;

public class PriceTableTests
{
    private static readonly TokenCounts Million = new(1_000_000, 0, 0, 1_000_000, 0, 1);

    [Fact]
    public void Bundled_prices_cover_every_model_seen_in_the_logs()
    {
        var table = PriceTable.Bundled;

        Assert.Equal(185, table.Count);
        foreach (var model in new[] { "claude-opus-5-5", "claude-opus-5.5", "gpt-6.1-sol", "gpt-6-luna", "gpt-5.6-luna", "gpt-5.6-terra", "gpt-5.6-sol", "gpt-6-sol" })
        {
            Assert.NotNull(table.Find(model));
        }
    }

    [Theory]
    // 1M input + 1M output tokens, hand-computed from LiteLLM list prices (2026-11-18).
    [InlineData("claude-opus-5-5", 24.0)]   // $4 in + $20 out
    [InlineData("claude-opus-5.5", 24.0)]   // Copilot's dotted name, same model
    [InlineData("gpt-6.1-sol", 12.0)]       // $2 + $10
    [InlineData("gpt-5.6-terra", 14.0)]     // $2 + $12
    [InlineData("gpt-5.6-sol", 24.0)]       // $4 + $20
    [InlineData("gpt-6-luna", 0.6)]         // $0.10 + $0.50
    [InlineData("gpt-5.6-luna", 1.4)]       // $0.20 + $1.20
    public void Bundled_costs_match_list_prices(string model, double dollars)
    {
        Assert.Equal((decimal)dollars, PriceTable.Bundled.Cost(model, Million));
    }

    [Fact]
    public void Cache_tokens_use_their_own_prices()
    {
        // claude-opus-5-5: cache write $5/M, cache read $0.20/M
        var cost = PriceTable.Bundled.Cost("claude-opus-5-5", new TokenCounts(0, 1_000_000, 10_000_000, 0, 999, 1));

        Assert.Equal(7.0m, cost);
    }

    [Theory]
    [InlineData("codex-auto-review")]
    [InlineData("unknown")]
    [InlineData("")]
    public void Unknown_models_have_no_cost(string model)
    {
        Assert.Null(PriceTable.Bundled.Find(model));
        Assert.Null(PriceTable.Bundled.Cost(model, Million));
    }

    [Fact]
    public void Names_are_matched_without_provider_prefix_date_or_case()
    {
        var table = PriceTable.Parse("""{"claude-x-1":{"input_cost_per_token":1,"output_cost_per_token":2},"github_copilot/only-copilot":{"input_cost_per_token":3,"output_cost_per_token":4}}""")!;

        Assert.Equal(1m, table.Find("anthropic/Claude-X-1-20250514")!.Input);
        Assert.Equal(1m, table.Find("claude-x-1-2025-05-14")!.Input);
        Assert.Equal(1m, table.Find("claude-x.1")!.Input);
        Assert.Equal(3m, table.Find("only-copilot")!.Input);
    }

    [Theory]
    [InlineData("claude-opus-5.5", new[] { "claude-opus-5.5", "claude-opus-5-5" })]
    [InlineData("gpt-5.6-luna", new[] { "gpt-5.6-luna" })]
    [InlineData(" OpenAI/GPT-6-Sol ", new[] { "gpt-6-sol" })]
    [InlineData("claude-opus-5-5-20261101", new[] { "claude-opus-5-5" })]
    public void Candidates_list_the_names_to_try(string model, string[] expected)
    {
        Assert.Equal(expected, ModelNames.Candidates(model));
    }

    [Fact]
    public void Parse_reads_exponent_numbers_and_fills_missing_cache_prices_with_input()
    {
        var table = PriceTable.Parse("""{"m":{"input_cost_per_token":4e-06,"output_cost_per_token":2E-05}}""")!;

        Assert.Equal(new ModelPrice(0.000004m, 0.00002m, 0.000004m, 0.000004m), table.Find("m"));
    }

    [Fact]
    public void Parse_skips_entries_without_usable_prices()
    {
        var table = PriceTable.Parse("""
            {
              "sample_spec": {"input_cost_per_token": "0.0", "output_cost_per_token": "0.0"},
              "no-output": {"input_cost_per_token": 1},
              "negative": {"input_cost_per_token": -1, "output_cost_per_token": 1},
              "not-an-object": 5,
              "too-big": {"input_cost_per_token": 1e400, "output_cost_per_token": 1},
              "good": {"input_cost_per_token": 1, "output_cost_per_token": 2, "cache_creation_input_token_cost": 3, "cache_read_input_token_cost": 0}
            }
            """)!;

        Assert.Equal(1, table.Count);
        Assert.Equal(new ModelPrice(1, 2, 3, 0), table.Find("good"));
    }

    [Theory]
    [InlineData("<html>404</html>")]
    [InlineData("[1,2]")]
    [InlineData("{}")]
    [InlineData("""{"x":{"input_cost_per_token":1}}""")]
    public void Parse_returns_null_when_nothing_usable(string json)
    {
        Assert.Null(PriceTable.Parse(json));
    }

    [Fact]
    public void Cost_adds_all_four_token_parts()
    {
        var price = new ModelPrice(Input: 1, Output: 10, CacheWrite: 100, CacheRead: 1000);

        Assert.Equal(1 + 20 + 300 + 4000, price.Cost(new TokenCounts(1, 3, 4, 2, 99, 1)));
    }
}
