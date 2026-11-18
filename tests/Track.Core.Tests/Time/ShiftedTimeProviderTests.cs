using Microsoft.Extensions.Time.Testing;
using Track.Core.Time;

namespace Track.Core.Tests.Time;

public class ShiftedTimeProviderTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 22, 45, 0, TimeSpan.Zero);
    private readonly FakeTimeProvider _real = new(Now);

    [Fact]
    public void Runs_the_offset_ahead_and_keeps_moving_with_the_real_clock()
    {
        _real.SetLocalTimeZone(TimeZoneInfo.FindSystemTimeZoneById("Europe/Copenhagen"));
        var shifted = (ShiftedTimeProvider)ShiftedTimeProvider.For(_real, "4500.5", diagnostics: true);

        Assert.Equal(TimeSpan.FromSeconds(4500.5), shifted.Offset);
        Assert.Equal(Now.AddSeconds(4500.5), shifted.GetUtcNow());
        _real.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(Now.AddSeconds(4530.5), shifted.GetUtcNow());
        Assert.Equal("Europe/Copenhagen", shifted.LocalTimeZone.Id);
        Assert.Equal(new DateTime(2026, 11, 19, 1, 0, 30, 500), shifted.GetLocalNow().DateTime); // 23:45:00 + 1 h 15 m 30.5 s
    }

    [Fact]
    public void A_negative_offset_goes_back()
    {
        Assert.Equal(Now.AddDays(-1), ShiftedTimeProvider.For(_real, "-86400", diagnostics: true).GetUtcNow());
    }

    [Theory]
    [InlineData("3600", false)] // only with TRACK_DIAG=1
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("soon", true)]
    [InlineData("0", true)]
    public void Without_diagnostics_or_a_usable_offset_the_real_clock_is_used(string? offset, bool diagnostics)
    {
        Assert.Same(_real, ShiftedTimeProvider.For(_real, offset, diagnostics));
    }
}
