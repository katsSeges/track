using System.IO.Abstractions;

namespace Track.Core.Pricing;

/// <summary>Fetches the current LiteLLM price list as text.</summary>
public interface IPriceDownloader
{
    Task<string> GetAsync(CancellationToken cancellationToken);
}

/// <summary>Downloads LiteLLM's public price list (the same list ccusage uses).</summary>
public sealed class LiteLlmPriceDownloader(HttpClient http) : IPriceDownloader
{
    public const string Url = "https://raw.githubusercontent.com/BerriAI/litellm/main/model_prices_and_context_window.json";

    public Task<string> GetAsync(CancellationToken cancellationToken) => http.GetStringAsync(Url, cancellationToken);
}

public enum PriceSource
{
    /// <summary>Shipped inside Track.</summary>
    Bundled,

    /// <summary>A previous download saved on disk.</summary>
    Cache,

    /// <summary>Downloaded just now.</summary>
    Download,
}

/// <summary>The price table in use and where it came from (for a "prices from …" note).</summary>
public sealed record LoadedPrices(PriceTable Table, PriceSource Source, DateTimeOffset? AsOfUtc);

/// <summary>
/// Picks the freshest usable prices: a cached download younger than <see cref="MaxAge"/>, else a new
/// download (saved to the cache), else the older cache, else the bundled table. Never throws for
/// network or disk trouble.
/// </summary>
public sealed class PriceLoader(IFileSystem fileSystem, string cachePath, IPriceDownloader downloader, TimeProvider time)
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);

    public static string DefaultCachePath(string localAppData) => Path.Combine(localAppData, "Track", "prices.json");

    public async Task<LoadedPrices> LoadAsync(CancellationToken cancellationToken = default)
    {
        var now = time.GetUtcNow();
        var cached = ReadCache();
        if (cached is not null && now - cached.WrittenUtc < MaxAge)
        {
            return cached.ToLoaded();
        }

        if (await TryDownloadAsync(cancellationToken) is { } downloaded)
        {
            return new LoadedPrices(downloaded, PriceSource.Download, now);
        }

        return cached?.ToLoaded() ?? new LoadedPrices(PriceTable.Bundled, PriceSource.Bundled, null);
    }

    private CachedPrices? ReadCache()
    {
        try
        {
            if (!fileSystem.File.Exists(cachePath) || PriceTable.Parse(fileSystem.File.ReadAllText(cachePath)) is not { } table)
            {
                return null;
            }

            return new CachedPrices(table, new DateTimeOffset(fileSystem.File.GetLastWriteTimeUtc(cachePath), TimeSpan.Zero));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private async Task<PriceTable?> TryDownloadAsync(CancellationToken cancellationToken)
    {
        string json;
        try
        {
            json = await downloader.GetAsync(cancellationToken);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return null; // offline, blocked or timed out
        }

        if (PriceTable.Parse(json) is not { } table)
        {
            return null; // an error page or a broken file
        }

        SaveCache(json);
        return table;
    }

    private void SaveCache(string json)
    {
        var temp = cachePath + ".tmp";
        try
        {
            fileSystem.Directory.CreateDirectory(fileSystem.Path.GetDirectoryName(cachePath)!);
            fileSystem.File.WriteAllText(temp, json);
            fileSystem.File.Move(temp, cachePath, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Prices still work from memory; the next start tries again.
        }
    }

    private sealed record CachedPrices(PriceTable Table, DateTimeOffset WrittenUtc)
    {
        public LoadedPrices ToLoaded() => new(Table, PriceSource.Cache, WrittenUtc);
    }
}
