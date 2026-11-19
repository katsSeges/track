using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Track.Core.Live;

namespace Track.App.Live;

[ExcludeFromCodeCoverage(Justification = "HTTP adapter only; loopback gating and status decisions are covered in Track.Core.")]
internal sealed class HttpOllamaStatusProbe : IOllamaStatusProbe
{
    private static readonly HttpClient Client = new(new HttpClientHandler { UseProxy = false })
    {
        Timeout = TimeSpan.FromSeconds(1),
    };

    private OllamaStatus _last = new(false, null);
    private int _inFlight;

    /// <summary>
    /// Returns the last answer at once and asks Ollama again in the background (one request at a time), so the
    /// live refresh, which holds the lock the UI also waits on, never waits for the network. The status lags by
    /// one refresh.
    /// </summary>
    public OllamaStatus Probe()
    {
        if (Interlocked.Exchange(ref _inFlight, 1) == 0)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    _last = await FetchAsync().ConfigureAwait(false);
                }
                finally
                {
                    Volatile.Write(ref _inFlight, 0);
                }
            });
        }

        return _last;
    }

    private static async Task<OllamaStatus> FetchAsync()
    {
        try
        {
            using var response = await Client.GetAsync("http://127.0.0.1:11434/api/ps").ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return new OllamaStatus(false, null);
            }

            await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
            var models = document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("models", out var value)
                && value.ValueKind == JsonValueKind.Array
                    ? value
                    : default;
            var model = models.ValueKind == JsonValueKind.Array
                ? models.EnumerateArray()
                    .Select(item => ReadString(item, "name") ?? ReadString(item, "model"))
                    .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name))
                : null;

            return new OllamaStatus(true, model);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or JsonException)
        {
            return new OllamaStatus(false, null);
        }
    }

    private static string? ReadString(JsonElement value, string property) =>
        value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty(property, out var candidate)
        && candidate.ValueKind == JsonValueKind.String
            ? candidate.GetString()
            : null;
}
