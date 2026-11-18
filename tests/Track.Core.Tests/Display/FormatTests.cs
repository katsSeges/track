using Track.Core.Display;
using Track.Core.Ingest;
using Track.Core.Usage;

namespace Track.Core.Tests.Display;

public class FormatTests
{
    [Theory]
    [InlineData(0, "0")]
    [InlineData(999, "999")]
    [InlineData(1_000, "1k")]
    [InlineData(920_400, "920k")]
    [InlineData(999_499, "999k")]
    [InlineData(999_500, "1.0M")]
    [InlineData(1_000_000, "1.0M")]
    [InlineData(21_449_999, "21.4M")]
    [InlineData(21_450_000, "21.5M")]
    [InlineData(999_949_999, "999.9M")]
    [InlineData(999_950_000, "1.0B")]
    [InlineData(1_457_200_000, "1.5B")]
    public void Tokens_use_k_M_and_B_without_awkward_boundaries(long tokens, string expected)
    {
        Assert.Equal(expected, Format.Tokens(tokens));
    }

    [Fact]
    public void Cost_is_in_dollars_regardless_of_the_windows_language()
    {
        var before = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("da-DK");
        try
        {
            Assert.Equal("$1,234.50", Format.Cost(Totals(1, cost: 1234.5m)));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = before;
        }
    }

    [Fact]
    public void Cost_marks_partial_and_unknown_prices()
    {
        Assert.Equal("$14.82", Format.Cost(Totals(100, cost: 14.82m)));
        Assert.Equal("≈$14.82", Format.Cost(Totals(100, cost: 14.82m, unpriced: 10)));
        Assert.Equal("$—", Format.Cost(Totals(100, cost: 0, unpriced: 100)));
        Assert.Equal("$0.00", Format.Cost(UsageTotals.Empty));
    }

    [Fact]
    public void Sub_line_reads_price_in_out_cache_and_adds_up()
    {
        var totals = new UsageTotals(new TokenCounts(1_400_000, 1_000_000, 18_100_000, 920_000, 300_000, 3), 14.82m, 0);

        Assert.Equal("in 2.4M · out 920k · cache 18.1M", Format.Split(totals));
        Assert.Equal("$14.82 · in 2.4M · out 920k · cache 18.1M", Format.SubLine(totals));
        Assert.Equal(totals.Tokens.Total, totals.InTokens + totals.OutTokens + totals.CacheHits);
        Assert.Equal("$0.00 · in 0 · out 0 · cache 0", Format.SubLine(UsageTotals.Empty));
    }

    [Theory]
    [InlineData(0, "just now")]
    [InlineData(4.9, "just now")]
    [InlineData(12, "12s ago")]
    [InlineData(59.9, "59s ago")]
    [InlineData(60, "1 min ago")]
    [InlineData(41 * 60, "41 min ago")]
    [InlineData(3 * 3600, "3 h ago")]
    [InlineData(2 * 86400, "2 d ago")]
    public void Ago_reads_like_a_person_would_say_it(double seconds, string expected)
    {
        Assert.Equal(expected, Format.Ago(TimeSpan.FromSeconds(seconds)));
    }

    [Theory]
    [InlineData(-5, "0:00")]
    [InlineData(7, "0:07")]
    [InlineData(754, "12:34")]
    [InlineData(3599, "59:59")]
    [InlineData(3600, "1:00:00")]
    [InlineData(3730, "1:02:10")]
    public void Timer_switches_to_hours_after_59_59(int seconds, string expected)
    {
        Assert.Equal(expected, Format.Timer(TimeSpan.FromSeconds(seconds)));
    }

    private static UsageTotals Totals(long tokens, decimal cost, long unpriced = 0) =>
        new(new TokenCounts(tokens, 0, 0, 0, 0, 1), cost, unpriced);
}
