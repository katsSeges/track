using System.Globalization;
using System.Text;
using Track.Core.Ingest;
using Track.Core.Providers;
using Track.Core.Usage;

namespace Track.Core.Display;

public sealed record AnalyticsRange(DateOnly StartDate, DateOnly EndDateInclusive, TimeRange UtcRange)
{
    public int Days => EndDateInclusive.DayNumber - StartDate.DayNumber + 1;
}

public sealed record AnalyticsDay(DateOnly Date, IReadOnlyDictionary<Tool, UsageTotals> Providers);

public sealed record AnalyticsModel(Tool Tool, string Model, UsageTotals Totals);

public sealed record AnalyticsHeatmapCell(int DayOfWeekMondayFirst, int Hour, long Tokens);

public sealed record AnalyticsSession(Tool Tool, string SessionId, SessionUsage Usage)
{
    public TimeSpan ObservedSpan => Usage.LastUtc - Usage.FirstUtc;
}

public sealed record AnalyticsData(
    AnalyticsRange Range,
    IReadOnlyList<Tool> Providers,
    IReadOnlyList<AnalyticsDay> Days,
    IReadOnlyList<AnalyticsModel> Models,
    IReadOnlyList<AnalyticsHeatmapCell> Heatmap,
    UsageTotals Total,
    decimal? CacheHitRate,
    int SessionCount,
    TimeSpan? MedianObservedSpan,
    IReadOnlyList<AnalyticsSession> TopSessions);

/// <summary>Builds range-local analytics from hourly buckets and explicitly range-overlapping session totals.</summary>
public sealed class AnalyticsBuilder(
    UsageStore store,
    UsageAggregator usage,
    PeriodCalculator periods,
    TimeProvider time)
{
    private const int TopSessionLimit = 10;

    public AnalyticsRange LastDays(int days)
    {
        if (days is < 1 or > RetentionWindow.Days)
        {
            throw new ArgumentOutOfRangeException(nameof(days), days, $"Choose between 1 and {RetentionWindow.Days} days.");
        }

        var today = DateOnly.FromDateTime(periods.ToLocal(time.GetUtcNow()));
        var end = today;
        var start = end.AddDays(1 - days);
        return CreateRange(start, end);
    }

    public AnalyticsRange CustomRange(DateOnly start, DateOnly endInclusive)
    {
        if (endInclusive < start)
        {
            throw new ArgumentOutOfRangeException(nameof(endInclusive), "The end date must not precede the start date.");
        }

        var days = endInclusive.DayNumber - start.DayNumber + 1;
        if (days > RetentionWindow.Days)
        {
            throw new ArgumentOutOfRangeException(nameof(endInclusive), $"Custom ranges are limited to {RetentionWindow.Days} days.");
        }

        return CreateRange(start, endInclusive);
    }

    public AnalyticsData Build(AnalyticsRange range, IEnumerable<Tool> visibleProviders)
    {
        var providers = ProviderCatalog.Order
            .Where(visibleProviders.ToHashSet().Contains)
            .Where(tool => ProviderCatalog.Get(tool).Capabilities.HasTokens)
            .ToList();
        var providerSet = providers.ToHashSet();
        var dayTotals = new Dictionary<(DateOnly Date, Tool Tool), UsageTotals>();
        var modelTotals = new Dictionary<(Tool Tool, string Model), UsageTotals>();
        var heatmap = new long[7, 24];
        var total = UsageTotals.Empty;

        foreach (var (key, tokens) in store.Buckets)
        {
            if (!providerSet.Contains(key.Tool) || !range.UtcRange.Contains(key.HourUtc))
            {
                continue;
            }

            var local = periods.ToLocal(key.HourUtc);
            var date = DateOnly.FromDateTime(local);
            var value = usage.Price(key.Model, tokens);
            var dayKey = (date, key.Tool);
            dayTotals[dayKey] = dayTotals.GetValueOrDefault(dayKey) + value;
            var modelKey = (key.Tool, key.Model);
            modelTotals[modelKey] = modelTotals.GetValueOrDefault(modelKey) + value;
            var dayIndex = ((int)local.DayOfWeek + 6) % 7;
            heatmap[dayIndex, local.Hour] += tokens.Total;
            total += value;
        }

        var days = Enumerable.Range(0, range.Days)
            .Select(offset =>
            {
                var date = range.StartDate.AddDays(offset);
                var totals = providers.ToDictionary(
                    tool => tool,
                    tool => dayTotals.GetValueOrDefault((date, tool), UsageTotals.Empty));
                return new AnalyticsDay(date, totals);
            })
            .ToList();
        var models = modelTotals
            .Select(pair => new AnalyticsModel(pair.Key.Tool, pair.Key.Model, pair.Value))
            .OrderByDescending(model => model.Totals.Tokens.Total)
            .ThenBy(model => model.Tool)
            .ThenBy(model => model.Model, StringComparer.Ordinal)
            .ToList();
        var cells = Enumerable.Range(0, 7)
            .SelectMany(day => Enumerable.Range(0, 24).Select(hour => new AnalyticsHeatmapCell(day, hour, heatmap[day, hour])))
            .ToList();

        var sessions = store.Sessions
            .Where(pair => providerSet.Contains(pair.Key.Tool)
                && pair.Value.FirstUtc < range.UtcRange.EndUtc
                && pair.Value.LastUtc >= range.UtcRange.StartUtc)
            .Select(pair => new AnalyticsSession(pair.Key.Tool, pair.Key.SessionId, pair.Value))
            .ToList();
        var spans = sessions.Select(session => session.ObservedSpan).Order().ToList();
        var medianSpan = spans.Count switch
        {
            0 => (TimeSpan?)null,
            var count when count % 2 == 1 => spans[count / 2],
            var count => TimeSpan.FromTicks((spans[(count / 2) - 1].Ticks + spans[count / 2].Ticks) / 2),
        };
        var sessionsTop = sessions
            .OrderByDescending(session => session.Usage.Tokens.Total)
            .ThenBy(session => session.Tool)
            .ThenBy(session => session.SessionId, StringComparer.Ordinal)
            .Take(TopSessionLimit)
            .ToList();
        var cacheTokens = total.Tokens.Input + total.Tokens.CacheWrite + total.Tokens.CacheRead;
        var cacheHitRate = cacheTokens == 0 ? (decimal?)null : (decimal)total.Tokens.CacheRead / cacheTokens;

        return new AnalyticsData(range, providers, days, models, cells, total, cacheHitRate, sessions.Count, medianSpan, sessionsTop);
    }

    private AnalyticsRange CreateRange(DateOnly start, DateOnly endInclusive)
    {
        if (endInclusive == DateOnly.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(endInclusive), "The end date must leave room for the exclusive boundary.");
        }

        var startUtc = periods.ToUtc(start.ToDateTime(TimeOnly.MinValue));
        var endUtc = periods.ToUtc(endInclusive.AddDays(1).ToDateTime(TimeOnly.MinValue));
        return new AnalyticsRange(start, endInclusive, new TimeRange(startUtc, endUtc));
    }
}

/// <summary>Exports the same rows presented by analytics: daily provider totals, models, sessions and heatmap.</summary>
public static class AnalyticsCsv
{
    private const string Header = "section,date,provider,model,session_id,first_observed_utc,last_observed_utc,observed_span_minutes,day_of_week,hour,input,cache_write,cache_read,output,reasoning,calls,total_tokens,known_cost,unpriced_tokens,cache_hit_rate,session_count,median_observed_span_minutes";

    public static string Write(AnalyticsData data)
    {
        var builder = new StringBuilder().AppendLine(Header);
        AddTotals(builder, "summary", null, null, null, null, null, null, null, data.Total,
            data.CacheHitRate, data.SessionCount, data.MedianObservedSpan);
        foreach (var day in data.Days)
        {
            foreach (var tool in data.Providers)
            {
                AddTotals(builder, "daily_provider", day.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), tool, null, null, null, null, null,
                    day.Providers[tool], null, null, null);
            }
        }

        foreach (var model in data.Models)
        {
            AddTotals(builder, "model", null, model.Tool, model.Model, null, null, null, null, model.Totals, null, null, null);
        }

        foreach (var session in data.TopSessions)
        {
            AddTotals(builder, "session", null, session.Tool, null, session.SessionId, session.Usage.FirstUtc, session.Usage.LastUtc,
                session.ObservedSpan, new UsageTotals(session.Usage.Tokens, 0, 0), null, null, null, includePrice: false);
        }

        foreach (var cell in data.Heatmap)
        {
            AddLine(builder, "heatmap", null, null, null, null, null, null, cell.DayOfWeekMondayFirst, cell.Hour,
                null, null, null, null, null, null, null, null, cell.Tokens, null, null, null, null);
        }

        return builder.ToString();
    }

    private static void AddTotals(
        StringBuilder builder,
        string section,
        string? date,
        Tool? tool,
        string? model,
        string? sessionId,
        DateTimeOffset? first,
        DateTimeOffset? last,
        TimeSpan? span,
        UsageTotals totals,
        decimal? cacheHitRate,
        int? sessionCount,
        TimeSpan? medianSpan,
        bool includePrice = true) =>
        AddLine(builder, section, date, tool is { } id ? ProviderCatalog.Get(id).Name : null, model, sessionId,
            first?.ToString("O", CultureInfo.InvariantCulture), last?.ToString("O", CultureInfo.InvariantCulture),
            null, null, span?.TotalMinutes.ToString("0.##", CultureInfo.InvariantCulture),
            medianSpan?.TotalMinutes.ToString("0.##", CultureInfo.InvariantCulture),
            totals.Tokens.Input, totals.Tokens.CacheWrite, totals.Tokens.CacheRead, totals.Tokens.Output,
            totals.Tokens.Reasoning, totals.Tokens.Calls, totals.Tokens.Total,
            includePrice ? totals.KnownCost.ToString("0.########", CultureInfo.InvariantCulture) : null,
            includePrice ? totals.UnpricedTokens : null,
            cacheHitRate?.ToString("0.########", CultureInfo.InvariantCulture), sessionCount);

    private static void AddLine(
        StringBuilder builder,
        string section,
        string? date,
        string? provider,
        string? model,
        string? sessionId,
        string? first,
        string? last,
        int? dayOfWeek,
        int? hour,
        string? observedSpanMinutes,
        string? medianSpanMinutes,
        long? input,
        long? cacheWrite,
        long? cacheRead,
        long? output,
        long? reasoning,
        long? calls,
        long? totalTokens,
        string? knownCost,
        long? unpricedTokens,
        string? cacheHitRate,
        int? sessionCount)
    {
        var fields = new[]
        {
            section, date, provider, model, sessionId, first, last, observedSpanMinutes,
            Number(dayOfWeek), Number(hour), Number(input), Number(cacheWrite), Number(cacheRead), Number(output),
            Number(reasoning), Number(calls), Number(totalTokens), knownCost, Number(unpricedTokens),
            cacheHitRate, Number(sessionCount), medianSpanMinutes,
        };
        builder.AppendLine(string.Join(",", fields.Select(Escape)));
    }

    private static string? Number<T>(T? value) where T : struct, IFormattable =>
        value is { } number ? number.ToString(null, CultureInfo.InvariantCulture) : null;

    private static string Escape(string? value) => value is null
        ? ""
        : value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r')
            ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : value;
}
