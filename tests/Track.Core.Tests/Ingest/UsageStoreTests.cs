using Track.Core.Ingest;
using Track.Core.Parsing;

namespace Track.Core.Tests.Ingest;

public class UsageStoreTests
{
    private static UsageRecord Record(DateTimeOffset at, long output = 10, string session = "s", string model = "m", Tool tool = Tool.Claude) =>
        new(tool, session, model, at, 1, 2, 3, output, 4, "k");

    [Fact]
    public void Records_are_summed_per_tool_model_and_utc_hour()
    {
        var store = new UsageStore();
        var at = new DateTimeOffset(2026, 11, 18, 13, 25, 0, TimeSpan.FromHours(1)); // 12:25 UTC

        Assert.Equal(4, store.Add([Record(at), Record(at.AddMinutes(30)), Record(at.AddMinutes(40)), Record(at, model: "other")]));

        var hour12 = new UsageBucketKey(Tool.Claude, "m", new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero));
        var hour13 = hour12 with { HourUtc = hour12.HourUtc.AddHours(1) };
        Assert.Equal(new TokenCounts(2, 4, 6, 20, 8, 2), store.Buckets[hour12]);
        Assert.Equal(new TokenCounts(1, 2, 3, 10, 4, 1), store.Buckets[hour13]);
        Assert.Equal(1, store.Buckets[hour12 with { Model = "other" }].Calls);
        Assert.Equal(3, store.Buckets.Count);
    }

    [Fact]
    public void Sessions_keep_their_totals_and_first_and_last_use()
    {
        var store = new UsageStore();
        store.Add([Record(Lines.Now), Record(Lines.Now.AddHours(-2)), Record(Lines.Now.AddHours(1)), Record(Lines.Now, session: "b", tool: Tool.Codex)]);

        var s = store.Sessions[new SessionKey(Tool.Claude, "s")];
        Assert.Equal((Lines.Now.AddHours(-2), Lines.Now.AddHours(1), 3L), (s.FirstUtc, s.LastUtc, s.Tokens.Calls));
        Assert.Equal(48, s.Tokens.Total);
        Assert.Equal(2, store.Sessions.Count);
    }

    [Fact]
    public void Prune_drops_old_hours_and_old_sessions()
    {
        var store = new UsageStore();
        store.Add([Record(Lines.Now.AddDays(-91), session: "old"), Record(Lines.Now.AddDays(-8), session: "week-old"), Record(Lines.Now, session: "new")]);

        store.Prune(Lines.Now.AddDays(-90), Lines.Now.AddDays(-7));

        Assert.Equal(2, store.Buckets.Count);
        Assert.Equal(["new"], store.Sessions.Keys.Select(k => k.SessionId));
    }

    [Fact]
    public void Saved_totals_restore_into_an_identical_store()
    {
        var original = new UsageStore();
        original.Add([Record(Lines.Now), Record(Lines.Now.AddHours(-3), model: "other"), Record(Lines.Now, session: "b", tool: Tool.Codex)]);
        var (buckets, sessions) = original.Save();

        var restored = new UsageStore();
        restored.Restore(buckets, sessions);

        Assert.Equal(original.Buckets.OrderBy(b => b.Key.HourUtc), restored.Buckets.OrderBy(b => b.Key.HourUtc));
        Assert.Equal(original.Sessions.OrderBy(s => s.Key.SessionId), restored.Sessions.OrderBy(s => s.Key.SessionId));
    }

    [Fact]
    public void Restore_adds_to_totals_already_there()
    {
        var store = new UsageStore();
        store.Add([Record(Lines.Now)]);
        var key = store.Buckets.Keys.Single();

        store.Restore([new Snapshot.Bucket(key.Tool, key.Model, key.HourUtc, new TokenCounts(0, 0, 0, 5, 0, 1))], []);

        Assert.Equal(15, store.Buckets[key].Output);
    }

    [Fact]
    public void Replacing_one_document_keeps_shared_buckets_and_exposes_document_contributions()
    {
        var store = new UsageStore();
        var first = Record(Lines.Now) with { DedupeKey = "first" };
        var second = Record(Lines.Now) with { DedupeKey = "second" };
        store.ReplaceDocumentContribution("first.json", [first]);
        store.ReplaceDocumentContribution("second.json", [second]);

        Assert.Equal(2, store.DocumentContributions.Count);
        Assert.Equal(2, store.Buckets.Values.Single().Calls);

        store.ReplaceDocumentContribution("first.json", []);

        Assert.Equal(1, store.Buckets.Values.Single().Calls);
        Assert.Equal(["first.json", "second.json"], store.DocumentContributions.Keys.OrderBy(path => path));
    }

    [Fact]
    public void Replacing_restored_document_metadata_without_totals_is_safe()
    {
        var store = new UsageStore();
        var old = Record(Lines.Now) with { DedupeKey = "old" };
        var replacement = old with { DedupeKey = "replacement", Output = 20 };
        store.RestoreDocumentContributions([
            new DocumentCursor("file.json", Tool.Claude, old.SessionId, "old-fingerprint", Lines.Now, 1, [old]),
        ]);

        store.ReplaceDocumentContribution("file.json", [replacement]);

        Assert.Equal(20, store.Buckets.Values.Single().Output);
        Assert.Equal("replacement", Assert.Single(store.DocumentContributions["file.json"]).DedupeKey);
    }

    [Fact]
    public void Token_counts_add_up_and_total_excludes_reasoning_and_calls()
    {
        var sum = new TokenCounts(1, 2, 3, 4, 5, 6) + new TokenCounts(10, 20, 30, 40, 50, 60);

        Assert.Equal(new TokenCounts(11, 22, 33, 44, 55, 66), sum);
        Assert.Equal(110, sum.Total);
    }

    [Fact]
    public void Retention_starts_90_days_ago_in_utc()
    {
        var now = new DateTimeOffset(2026, 11, 18, 13, 0, 0, TimeSpan.FromHours(1));

        Assert.Equal(new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero), RetentionWindow.Start(now));
        Assert.Equal(TimeSpan.Zero, RetentionWindow.Start(now).Offset);
    }
}
