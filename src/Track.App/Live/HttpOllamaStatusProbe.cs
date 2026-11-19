using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using Track.Core.Live;

namespace Track.App.Live;

[ExcludeFromCodeCoverage(Justification = "HTTP adapter only; loopback gating and status decisions are covered in Track.Core.")]
internal sealed class HttpOllamaStatusProbe : IOllamaStatusProbe
{
    private static readonly HttpClient Client = new(new HttpClientHandler { UseProxy = false })
    {
        Timeout = TimeSpan.FromSeconds(1),
    };

    public OllamaStatus Probe()
    {
        try
        {
            using var response = Client.GetAsync("http://127.0.0.1:11434/api/ps").GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                return new OllamaStatus(false, null);
            }

            using var stream = response.Content.ReadAsStream();
            using var document = JsonDocument.Parse(stream);
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
