namespace Track.Core.Tests.Ingest;

/// <summary>Minimal log lines for ingest tests.</summary>
internal static class Lines
{
    public static readonly DateTimeOffset Now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A Copilot usage event worth <paramref name="tokens"/> output tokens.</summary>
    public static string Copilot(string id, long tokens = 10, string timestamp = "2026-11-18T10:00:00Z") =>
        "{\"type\":\"session.usage_record\",\"id\":\"" + id + "\",\"timestamp\":\"" + timestamp
        + "\",\"data\":{\"usage\":{\"model\":\"gpt-6.1-sol\",\"outputTokens\":" + tokens + "}}}";

    public static string Claude(string id, long tokens = 10, string timestamp = "2026-11-18T10:00:00Z") =>
        "{\"type\":\"assistant\",\"timestamp\":\"" + timestamp + "\",\"requestId\":\"r\",\"message\":{\"id\":\"" + id
        + "\",\"model\":\"claude-opus-5-5\",\"usage\":{\"output_tokens\":" + tokens + "}}}";

    public static string Codex(long total, string timestamp = "2026-11-18T10:00:00Z") =>
        "{\"timestamp\":\"" + timestamp + "\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"info\":{\"total_token_usage\":"
        + "{\"input_tokens\":" + total + ",\"output_tokens\":0,\"total_tokens\":" + total + "}}}}";
}
