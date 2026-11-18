namespace Track.Core.Ingest;

/// <summary>
/// How far back Track keeps usage. The popover needs this month, the tray rule needs the last full
/// month (at most 62 days back) and "no usage" needs 30 days, so 90 days covers all of them with room
/// for a longer "no usage" setting.
/// </summary>
public static class RetentionWindow
{
    public const int Days = 90;

    public static DateTimeOffset Start(DateTimeOffset now) => now.ToUniversalTime().AddDays(-Days);
}
