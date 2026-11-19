using System.Globalization;
using System.IO.Abstractions;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Track.Core.Parsing;

namespace Track.Core.Ingest;

/// <summary>A whole-file contribution whose records replace, rather than append to, the prior version.</summary>
public sealed record DocumentCursor(
    string Path,
    Tool Tool,
    string SessionId,
    string Fingerprint,
    DateTimeOffset LastWriteUtc,
    long Length,
    IReadOnlyList<UsageRecord> Records);

public sealed record DocumentReadResult(string Path, IReadOnlyList<UsageRecord> Records);

/// <summary>Reads provider documents and remembers their contributions across refreshes and restarts.</summary>
public sealed class DocumentLogReader(IFileSystem fileSystem)
{
    private readonly Dictionary<string, DocumentCursor> _cursors = new(StringComparer.OrdinalIgnoreCase);

    public DocumentReadResult? ReadChanged(LogFile file, DateTimeOffset keepFrom)
    {
        var path = DatabasePath(file);
        var fingerprint = Fingerprint(path, file.Mode);
        if (fingerprint is null || (_cursors.TryGetValue(path, out var previous) && previous.Fingerprint == fingerprint))
        {
            return null;
        }

        IReadOnlyList<UsageRecord> records;
        try
        {
            records = Parse(file with { Path = path }, keepFrom);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or SqliteException)
        {
            return null;
        }

        var info = fileSystem.FileInfo.New(path);
        var cursor = new DocumentCursor(
            path,
            file.Tool,
            file.SessionId,
            fingerprint,
            new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
            info.Length,
            records);
        _cursors[path] = cursor;
        return new DocumentReadResult(path, records);
    }

    public IReadOnlyList<DocumentCursor> SaveCursors() => _cursors.Values.ToList();

    public void RestoreCursors(IEnumerable<DocumentCursor> cursors)
    {
        foreach (var cursor in cursors)
        {
            _cursors[cursor.Path] = cursor;
        }
    }

    public void Prune(DateTimeOffset keepFrom)
    {
        foreach (var path in _cursors.Keys.ToList())
        {
            var cursor = _cursors[path];
            _cursors[path] = cursor with { Records = cursor.Records.Where(record => record.Timestamp >= keepFrom).ToList() };
        }
    }

    private IReadOnlyList<UsageRecord> Parse(LogFile file, DateTimeOffset keepFrom) => file.Mode switch
    {
        LogReadMode.GeminiJsonLines when file.Tool == Tool.Gemini => ParseGeminiJsonLines(file, keepFrom),
        LogReadMode.GeminiJsonLines => throw new ArgumentOutOfRangeException(nameof(file), file.Tool, "No JSONL parser for this provider."),
        LogReadMode.JsonDocument when file.Tool is Tool.Cline or Tool.Roo => ParseTaskMessages(file, keepFrom),
        LogReadMode.JsonDocument => throw new ArgumentOutOfRangeException(nameof(file), file.Tool, "No document parser for this provider."),
        LogReadMode.OpenCodeDatabase when file.Tool == Tool.OpenCode => ParseOpenCodeDatabase(file, keepFrom),
        LogReadMode.OpenCodeDatabase => throw new ArgumentOutOfRangeException(nameof(file), file.Tool, "No database parser for this provider."),
        _ => throw new ArgumentOutOfRangeException(nameof(file), file.Mode, "The file must use a document read mode."),
    };

    private IReadOnlyList<UsageRecord> ParseTaskMessages(LogFile file, DateTimeOffset keepFrom)
    {
        using var document = JsonDocument.Parse(fileSystem.File.ReadAllText(file.Path));
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("Task messages must be a JSON array.");
        }

        var records = new List<UsageRecord>();
        var messages = document.RootElement;
        for (var index = 0; index < messages.GetArrayLength(); index++)
        {
            var message = messages[index];
            if (!TryTimestampMilliseconds(message, "ts", out var timestamp)
                || !IsString(message, "type", "say")
                || !message.TryGetProperty("say", out var say)
                || say.ValueKind != JsonValueKind.String
                || say.GetString() != "api_req_started"
                || !message.TryGetProperty("text", out var text)
                || text.ValueKind != JsonValueKind.String
                || timestamp < keepFrom)
            {
                continue;
            }

            JsonDocument payload;
            try
            {
                payload = JsonDocument.Parse(text.GetString()!);
            }
            catch (JsonException)
            {
                continue;
            }

            using (payload)
            {
                var usage = payload.RootElement;
                if (usage.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var inputWithCache = ReadNonNegativeLong(usage, "tokensIn");
                var cacheWrite = ReadNonNegativeLong(usage, "cacheWrites");
                var cacheRead = ReadNonNegativeLong(usage, "cacheReads");
                var input = file.Tool == Tool.Roo
                    ? Math.Max(0, inputWithCache - cacheWrite - cacheRead)
                    : inputWithCache;
                var output = ReadNonNegativeLong(usage, "tokensOut");
                if (input + cacheWrite + cacheRead + output == 0)
                {
                    continue;
                }

                var model = ReadModel(message);
                records.Add(new UsageRecord(
                    file.Tool,
                    file.SessionId,
                    model,
                    timestamp,
                    input,
                    cacheWrite,
                    cacheRead,
                    output,
                    0,
                    $"{file.SessionId}:{timestamp.ToUnixTimeMilliseconds()}:{index}"));
            }
        }

        return records;
    }

    private IReadOnlyList<UsageRecord> ParseGeminiJsonLines(LogFile file, DateTimeOffset keepFrom)
    {
        var messages = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var order = new List<string>();
        var sessionId = file.SessionId;

        foreach (var line in fileSystem.File.ReadLines(file.Path))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(line);
            }
            catch (JsonException)
            {
                continue;
            }

            using (document)
            {
                var record = document.RootElement;
                if (record.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (ReadString(record, "sessionId") is { Length: > 0 } id)
                {
                    sessionId = id;
                }

                if (record.TryGetProperty("$set", out var set) && set.ValueKind == JsonValueKind.Object)
                {
                    if (ReadString(set, "sessionId") is { Length: > 0 } metadataId)
                    {
                        sessionId = metadataId;
                    }

                    if (set.TryGetProperty("messages", out var checkpoint) && checkpoint.ValueKind == JsonValueKind.Array)
                    {
                        messages.Clear();
                        order.Clear();
                        foreach (var message in checkpoint.EnumerateArray())
                        {
                            StoreMessage(message, messages, order);
                        }
                    }

                    continue;
                }

                if (ReadString(record, "projectHash") is not null
                    && record.TryGetProperty("messages", out var legacyMessages)
                    && legacyMessages.ValueKind == JsonValueKind.Array)
                {
                    foreach (var message in legacyMessages.EnumerateArray())
                    {
                        StoreMessage(message, messages, order);
                    }
                }

                if (record.TryGetProperty("$rewindTo", out var rewind) && rewind.ValueKind == JsonValueKind.String)
                {
                    var rewindIndex = order.IndexOf(rewind.GetString()!);
                    var removeAt = rewindIndex < 0 ? 0 : rewindIndex;
                    foreach (var removed in order.Skip(removeAt).ToList())
                    {
                        messages.Remove(removed);
                    }

                    order.RemoveRange(removeAt, order.Count - removeAt);
                    continue;
                }

                if (record.TryGetProperty("$patch", out var patch) && patch.ValueKind == JsonValueKind.Object)
                {
                    if (patch.TryGetProperty("removeIds", out var removeIds) && removeIds.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var removed in removeIds.EnumerateArray())
                        {
                            if (removed.ValueKind != JsonValueKind.String)
                            {
                                continue;
                            }

                            var removedId = removed.GetString()!;
                            messages.Remove(removedId);
                            order.Remove(removedId);
                        }
                    }

                    if (patch.TryGetProperty("orderIds", out var orderIds) && orderIds.ValueKind == JsonValueKind.Array)
                    {
                        var requestedOrder = orderIds.EnumerateArray()
                            .Where(id => id.ValueKind == JsonValueKind.String)
                            .Select(id => id.GetString()!)
                            .Where(messages.ContainsKey)
                            .ToList();
                        var requestedIds = requestedOrder.ToHashSet(StringComparer.Ordinal);
                        order = order.Where(id => !requestedIds.Contains(id)).Concat(requestedOrder).ToList();
                    }

                    continue;
                }

                StoreMessage(record, messages, order);
            }
        }

        var records = new List<UsageRecord>();
        foreach (var id in order)
        {
            var message = messages[id];
            if (!IsString(message, "type", "gemini")
                || !TryReadTimestamp(message, "timestamp", out var timestamp)
                || timestamp < keepFrom
                || !message.TryGetProperty("tokens", out var tokens)
                || tokens.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var inputWithCache = ReadNonNegativeLong(tokens, "input");
            var cacheRead = ReadNonNegativeLong(tokens, "cached");
            var input = Math.Max(0, inputWithCache - cacheRead);
            var output = ReadNonNegativeLong(tokens, "output");
            var reasoning = Math.Min(output, ReadNonNegativeLong(tokens, "thoughts"));
            if (input + cacheRead + output == 0)
            {
                continue;
            }

            records.Add(new UsageRecord(
                Tool.Gemini,
                sessionId,
                ReadString(message, "model") ?? "Unknown",
                timestamp,
                input,
                0,
                cacheRead,
                output,
                reasoning,
                id));
        }

        return records;
    }

    private static IReadOnlyList<UsageRecord> ParseOpenCodeDatabase(LogFile file, DateTimeOffset keepFrom)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = file.Path,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Shared,
            Pooling = false,
        };
        using var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, session_id, time_created, data
            FROM session_message
            WHERE type = 'assistant' AND time_created >= $from
            """;
        command.Parameters.AddWithValue("$from", keepFrom.ToUnixTimeMilliseconds());

        using var rows = command.ExecuteReader();
        var records = new List<UsageRecord>();
        while (rows.Read())
        {
            var id = rows.GetString(0);
            var sessionId = rows.GetString(1);
            var timestamp = DateTimeOffset.FromUnixTimeMilliseconds(rows.GetInt64(2));
            using var document = JsonDocument.Parse(rows.GetString(3));
            var data = document.RootElement;
            if (!data.TryGetProperty("tokens", out var tokens) || tokens.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var modelInfo = data.TryGetProperty("model", out var model)
                && model.ValueKind == JsonValueKind.Object
                    ? model
                    : default;
            var modelId = ReadString(modelInfo, "id");
            if (modelId is null)
            {
                continue;
            }

            var providerId = ReadString(modelInfo, "providerID");
            var cache = tokens.TryGetProperty("cache", out var cacheValue) && cacheValue.ValueKind == JsonValueKind.Object
                ? cacheValue
                : default;
            var input = ReadNonNegativeLong(tokens, "input");
            var output = ReadNonNegativeLong(tokens, "output");
            var cacheRead = ReadNonNegativeLong(cache, "read");
            var cacheWrite = ReadNonNegativeLong(cache, "write");
            var reasoning = Math.Min(output, ReadNonNegativeLong(tokens, "reasoning"));
            if (input + output + cacheRead + cacheWrite == 0)
            {
                continue;
            }

            records.Add(new UsageRecord(
                Tool.OpenCode,
                sessionId,
                providerId is null ? modelId : $"{providerId}/{modelId}",
                timestamp,
                input,
                cacheWrite,
                cacheRead,
                output,
                reasoning,
                id));
        }

        return records;
    }

    private string? Fingerprint(string path, LogReadMode mode)
    {
        if (mode != LogReadMode.OpenCodeDatabase)
        {
            var info = fileSystem.FileInfo.New(path);
            return info.Exists ? $"{info.Length}:{info.LastWriteTimeUtc.Ticks}" : null;
        }

        var paths = new[] { path, path + "-wal", path + "-shm" };
        var signatures = new List<string>(paths.Length);
        foreach (var candidate in paths)
        {
            var info = fileSystem.FileInfo.New(candidate);
            signatures.Add(info.Exists ? $"{info.Length}:{info.LastWriteTimeUtc.Ticks}" : "-");
        }

        return signatures[0] == "-" ? null : string.Join("|", signatures);
    }

    private static string DatabasePath(LogFile file)
    {
        if (file.Mode != LogReadMode.OpenCodeDatabase)
        {
            return file.Path;
        }

        return file.Path.EndsWith("-wal", StringComparison.OrdinalIgnoreCase)
            || file.Path.EndsWith("-shm", StringComparison.OrdinalIgnoreCase)
            ? file.Path[..^4]
            : file.Path;
    }

    private static void StoreMessage(JsonElement message, Dictionary<string, JsonElement> messages, List<string> order)
    {
        if (ReadString(message, "id") is not { Length: > 0 } id)
        {
            return;
        }

        if (!messages.ContainsKey(id))
        {
            order.Add(id);
        }

        messages[id] = message.Clone();
    }

    private static string ReadModel(JsonElement message)
    {
        if (message.TryGetProperty("modelInfo", out var info) && info.ValueKind == JsonValueKind.Object)
        {
            var modelId = ReadString(info, "modelId");
            if (modelId is not null)
            {
                return ReadString(info, "providerId") is { Length: > 0 } provider
                    ? $"{provider}/{modelId}"
                    : modelId;
            }
        }

        return "Unknown";
    }

    private static bool IsString(JsonElement value, string property, string expected) =>
        value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty(property, out var candidate)
        && candidate.ValueKind == JsonValueKind.String
        && candidate.GetString() == expected;

    private static string? ReadString(JsonElement value, string property) =>
        value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty(property, out var candidate)
        && candidate.ValueKind == JsonValueKind.String
            ? candidate.GetString()
            : null;

    private static long ReadNonNegativeLong(JsonElement value, string property) =>
        value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty(property, out var candidate)
        && candidate.ValueKind == JsonValueKind.Number
        && candidate.TryGetInt64(out var number)
        && number >= 0
            ? number
            : 0;

    private static bool TryTimestampMilliseconds(JsonElement value, string property, out DateTimeOffset timestamp)
    {
        timestamp = default;
        if (value.ValueKind != JsonValueKind.Object
            || !value.TryGetProperty(property, out var candidate)
            || candidate.ValueKind != JsonValueKind.Number
            || !candidate.TryGetInt64(out var milliseconds))
        {
            return false;
        }

        try
        {
            timestamp = DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static bool TryReadTimestamp(JsonElement value, string property, out DateTimeOffset timestamp)
    {
        timestamp = default;
        return value.ValueKind == JsonValueKind.Object
            && value.TryGetProperty(property, out var candidate)
            && candidate.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(candidate.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out timestamp);
    }
}
