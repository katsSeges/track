using Track.Core.Parsing;

namespace Track.Core.Tests.Parsing;

/// <summary>Runs a parser over an anonymized fixture made from real logs (see Fixtures/).</summary>
internal static class Fixture
{
    public static IReadOnlyList<UsageRecord> Parse(Tool tool, string file, string fallbackSessionId = "fallback")
    {
        var parser = LogParsers.Create(tool, fallbackSessionId);
        return File.ReadLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", file))
            .Select(parser.Parse)
            .OfType<UsageRecord>()
            .DistinctBy(r => r.DedupeKey)
            .ToList();
    }
}
