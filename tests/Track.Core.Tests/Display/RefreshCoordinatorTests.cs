using Microsoft.Extensions.Time.Testing;
using Track.Core.Display;

namespace Track.Core.Tests.Display;

public class RefreshCoordinatorTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero));
    private readonly List<string?> _status = [];
    private int _usage;
    private int _live;

    private RefreshCoordinator Create(Action? usage = null, Action? live = null) => new(
        _time,
        TimeSpan.FromSeconds(30),
        RefreshCoordinator.DefaultLiveInterval,
        usage ?? (() => _usage++),
        live ?? (() => _live++),
        _status.Add);

    [Fact]
    public void Start_refreshes_at_once_then_every_30_and_5_seconds()
    {
        using var c = Create();
        c.Start();
        Assert.Equal((1, 1), (_usage, _live));

        _time.Advance(TimeSpan.FromSeconds(30));

        Assert.Equal((2, 7), (_usage, _live));
        Assert.All(_status, Assert.Null);
    }

    [Fact]
    public void Disposing_stops_the_timers()
    {
        var c = Create();
        c.Start();
        c.Dispose();

        _time.Advance(TimeSpan.FromMinutes(5));

        Assert.Equal((1, 1), (_usage, _live));
    }

    [Fact]
    public void A_failing_refresh_becomes_a_status_line_and_clears_after_a_success()
    {
        var fail = true;
        using var c = Create(usage: () =>
        {
            if (fail)
            {
                throw new IOException("disk busy");
            }
        });

        Assert.True(c.RefreshUsageNow());
        Assert.Equal("Usage refresh failed: disk busy", _status[^1]);

        fail = false;
        c.RefreshUsageNow();
        Assert.Null(_status[^1]);

        using var live = Create(live: () => throw new InvalidOperationException("boom"));
        live.RefreshLiveNow();
        Assert.Equal("Live status refresh failed: boom", _status[^1]);
    }

    [Fact]
    public void A_refresh_never_starts_on_top_of_one_still_running()
    {
        RefreshCoordinator? c = null;
        bool? nested = null;
        c = Create(usage: () => nested = c!.RefreshUsageNow(), live: () => nested ??= c!.RefreshLiveNow());

        Assert.True(c.RefreshUsageNow());
        Assert.False(nested);

        nested = null;
        Assert.True(c.RefreshLiveNow());
        Assert.False(nested);
        c.Dispose();
    }
}
