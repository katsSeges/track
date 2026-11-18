using System.Globalization;
using System.Text.Json;

namespace Track.Core.Parsing;

/// <summary>Small null-safe helpers for reading one JSON line from an agent log.</summary>
internal static class JsonLine
{
    /// <summary>Parses a line into its root object, or null for blank, malformed or non-object lines.</summary>
    public static JsonElement? Parse(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(line);
            return doc.RootElement.ValueKind == JsonValueKind.Object ? doc.RootElement.Clone() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Walks object properties by name; null when any step is missing or not an object.</summary>
    public static JsonElement? Get(JsonElement? element, params string[] path)
    {
        var current = element;
        foreach (var name in path)
        {
            if (current is not { ValueKind: JsonValueKind.Object } obj || !obj.TryGetProperty(name, out var next))
            {
                return null;
            }

            current = next;
        }

        return current;
    }

    public static string? String(JsonElement? element) =>
        element is { ValueKind: JsonValueKind.String } s ? s.GetString() : null;

    /// <summary>A non-negative whole number, or 0 when missing, not a number or negative.</summary>
    public static long Long(JsonElement? element) =>
        element is { ValueKind: JsonValueKind.Number } n && n.TryGetInt64(out var value) && value > 0 ? value : 0;

    public static DateTimeOffset? Timestamp(JsonElement? element) =>
        String(element) is { } text
        && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value)
            ? value.ToUniversalTime()
            : null;
}
