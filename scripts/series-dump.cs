#:project ../src/Track.Core/Track.Core.csproj
#:property PublishAot=false
// Track's own per-day (this month) and per-hour (today) totals from UsageSeries, as JSON, for check-series.ps1.
// Reads the logs fresh (no snapshot). Usage: dotnet run scripts/series-dump.cs -- <home> <cutoff-utc-iso>
using System.IO.Abstractions;
using System.Text;
using Track.Core;
using Track.Core.Ingest;
using Track.Core.Usage;

var home = args[0];
var cutoff = DateTimeOffset.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture);
var fs = new FileSystem();
var store = new UsageStore();
new UsageIngestor(new LogScanner(fs, LogSources.Default(home)), new IncrementalLogReader(fs), store, TimeProvider.System).Refresh();
var usage = new UsageAggregator(store);
var series = new UsageSeries(store, usage, new PeriodCalculator(TimeProvider.System, TimeZoneInfo.Local), TimeProvider.System);
var atCutoff = new FixedTime(cutoff); // compare "today vs yesterday" as of the cutoff, like the independent count
var comparison = new UsageComparison(usage, new PeriodCalculator(atCutoff, TimeZoneInfo.Local), atCutoff).For(Period.Today);

string Tools(SeriesBucket b) => string.Join(",", Enum.GetValues<Tool>().Select(t => $"\"{t}\":{b.PerTool[t].Tokens.Total}"));
var json = new StringBuilder("{\"days\":{");
json.Append(string.Join(",", series.For(Period.Month).Where(b => b.EndUtc <= cutoff).Select(b =>
    $"\"{TimeZoneInfo.ConvertTime(b.StartUtc, TimeZoneInfo.Local):yyyy-MM-dd}\":{{{Tools(b)}}}")));
json.Append("},\"hours\":{");
json.Append(string.Join(",", series.For(Period.Today).Where(b => b.EndUtc <= cutoff).Select(b =>
    $"\"{TimeZoneInfo.ConvertTime(b.StartUtc, TimeZoneInfo.Local).Hour}\":{{{Tools(b)}}}")));
json.Append($"}},\"comparison\":{{\"current\":{comparison.Current},\"previous\":{comparison.Previous},\"percent\":{(comparison.Percent?.ToString() ?? "null")},\"text\":\"{comparison.Text}\"}}}}");
Console.WriteLine(json);

sealed class FixedTime(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
