using System.Globalization;

namespace Track.Core.Time;

/// <summary>
/// A clock running a fixed offset ahead of (or behind) another clock. Verification only: it lets the real app be
/// put a minute before midnight, the end of a week or the end of a month, so rollover can be watched end to end.
/// </summary>
public sealed class ShiftedTimeProvider(TimeProvider inner, TimeSpan offset) : TimeProvider
{
    public TimeSpan Offset => offset;

    public override DateTimeOffset GetUtcNow() => inner.GetUtcNow() + offset;

    public override TimeZoneInfo LocalTimeZone => inner.LocalTimeZone;

    /// <summary>
    /// The app's clock: <paramref name="inner"/>, shifted by <c>TRACK_CLOCK_OFFSET</c> seconds only when diagnostics are
    /// on (<c>TRACK_DIAG=1</c>), so a stray variable can never move a normal user's numbers.
    /// </summary>
    public static TimeProvider For(TimeProvider inner, string? offsetSeconds, bool diagnostics) =>
        diagnostics && double.TryParse(offsetSeconds, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds != 0
            ? new ShiftedTimeProvider(inner, TimeSpan.FromSeconds(seconds))
            : inner;
}
