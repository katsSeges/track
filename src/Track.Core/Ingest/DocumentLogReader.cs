using System.Globalization;
using System.IO.Abstractions;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Track.Core.Parsing;

namespace Track.Core.Ingest;

/// <summary>
/// The version of a whole-file document that was last read. In the snapshot it also carries what that version
/// added to the totals (<see cref="Contribution"/>), so a restart can replace it later without reading it first.
/// </summary>
public sealed record DocumentCursor(
    string Path,
    Tool Tool,
    string SessionId,
    string Fingerprint,
    DocumentContribution? Contribution = null);

public sealed record DocumentReadResult(string Path, IReadOnlyList<UsageRecord> Records);

/// <summary>Reads provider documents and remembers their contributions across refreshes and restarts.</summary>
public sealed partial class DocumentLogReader(IFileSystem fileSystem)
{
    /// <summary>
    /// Copies of OpenCode's database that hold the same usage again: its migration copy <c>opencode-next.db</c>
    /// and backups. (<c>opencode.db</c>, <c>opencode-&lt;channel&gt;.db</c> and an <c>OPENCODE_DB</c> name are read.)
    /// </summary>
    [System.Text.RegularExpressions.GeneratedRegex(@"^opencode-next\.db$|backup|copy|\.bak|\.old", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex OpenCodeDatabaseCopy();

    private readonly Dictionary<string, DocumentCursor> _cursors = new(StringComparer.OrdinalIgnoreCase);

    public DocumentReadResult? ReadChanged(LogFile file, DateTimeOffset keepFrom)
    {
        var path = DatabasePath(file);
        if (file.Mode == LogReadMode.OpenCodeDatabase && OpenCodeDatabaseCopy().IsMatch(Path.GetFileName(path)))
        {
            return null;
        }

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
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SqliteException)
        {
            // Locked or busy right now: try again next refresh.
            return null;
        }
        catch (JsonException)
        {
            // Broken or half-written: remember this version so it is not parsed again every refresh. What the
            // document added before stays counted; a newer version is read as soon as it changes.
            _cursors[path] = new DocumentCursor(path, file.Tool, file.SessionId, fingerprint);
            return null;
        }

        _cursors[path] = new DocumentCursor(path, file.Tool, file.SessionId, fingerprint);
        return new DocumentReadResult(path, records);
    }

    /// <summary>The versions read so far (without contributions; the store holds those).</summary>
    public IReadOnlyList<DocumentCursor> SaveCursors() => _cursors.Values.ToList();

    public void RestoreCursors(IEnumerable<DocumentCursor> cursors)
    {
        foreach (var cursor in cursors)
        {
            _cursors[cursor.Path] = cursor with { Contribution = null };
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

    /// <summary>
    /// Gemini CLI's chat file: messages, checkpoints (<c>$set</c>), edits (<c>$patch</c>) and rewinds (<c>$rewindTo</c>).
    /// Usage is counted per response id, latest version wins. A rewind or removal hides a response from the chat, but
    /// it was sent and billed, so it stays counted.
    /// </summary>
    private IReadOnlyList<UsageRecord> ParseGeminiJsonLines(LogFile file, DateTimeOffset keepFrom)
    {
        var messages = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
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
                        foreach (var message in checkpoint.EnumerateArray())
                        {
                            StoreMessage(message, messages);
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
                        StoreMessage(message, messages);
                    }
                }

                if (record.TryGetProperty("$rewindTo", out _) || record.TryGetProperty("$patch", out _))
                {
                    continue;
                }

                StoreMessage(record, messages);
            }
        }

        var records = new List<UsageRecord>();
        foreach (var (id, message) in messages)
        {
            if (!IsString(message, "type", "gemini")
                || !TryReadTimestamp(message, "timestamp", out var timestamp)
                || timestamp < keepFrom
                || !message.TryGetProperty("tokens", out var tokens)
                || tokens.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            // Gemini's counts: input (promptTokenCount) includes cached; tool-use prompt tokens and thoughts are
            // billed on top of input and output (candidates), which exclude them.
            var cacheRead = ReadNonNegativeLong(tokens, "cached");
            var input = Math.Max(0, ReadNonNegativeLong(tokens, "input") - cacheRead) + ReadNonNegativeLong(tokens, "tool");
            var reasoning = ReadNonNegativeLong(tokens, "thoughts");
            var output = ReadNonNegativeLong(tokens, "output") + reasoning;
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
            WHERE type = 'assistant'
              AND typeof(id) = 'text' AND typeof(session_id) = 'text' AND typeof(data) = 'text'
              AND typeof(time_created) = 'integer' AND time_created BETWEEN $from AND $to
              AND json_valid(data)
            """;
        // Rows a reader cannot use (NULL or odd columns, broken JSON, impossible times) are left out by the
        // query, so one bad row never stops the rest from being counted.
        command.Parameters.AddWithValue("$from", keepFrom.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$to", DateTimeOffset.MaxValue.ToUnixTimeMilliseconds());

        using var rows = command.ExecuteReader();
        var records = new List<UsageRecord>();
        while (rows.Read())
        {
            if (OpenCodeRecord(rows.GetString(0), rows.GetString(1), rows.GetInt64(2), rows.GetString(3)) is { } record)
            {
                records.Add(record);
            }
        }

        return records;
    }

    private static UsageRecord? OpenCodeRecord(string id, string sessionId, long createdMilliseconds, string json)
    {
        var timestamp = DateTimeOffset.FromUnixTimeMilliseconds(createdMilliseconds);
        using var document = JsonDocument.Parse(json);
        var data = document.RootElement;
        if (data.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty("tokens", out var tokens)
            || tokens.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var modelInfo = data.TryGetProperty("model", out var model)
            && model.ValueKind == JsonValueKind.Object
                ? model
                : default;
        var modelId = ReadString(modelInfo, "id");
        if (modelId is null)
        {
            return null;
        }

        var providerId = ReadString(modelInfo, "providerID");
        var cache = tokens.TryGetProperty("cache", out var cacheValue) && cacheValue.ValueKind == JsonValueKind.Object
            ? cacheValue
            : default;
        // OpenCode splits usage into disjoint parts: input without cache, output without reasoning. Reasoning is
        // billed as output, so it is added back (and also reported as reasoning).
        var input = ReadNonNegativeLong(tokens, "input");
        var reasoning = ReadNonNegativeLong(tokens, "reasoning");
        var output = ReadNonNegativeLong(tokens, "output") + reasoning;
        var cacheRead = ReadNonNegativeLong(cache, "read");
        var cacheWrite = ReadNonNegativeLong(cache, "write");
        if (input + output + cacheRead + cacheWrite == 0)
        {
            return null;
        }

        return new UsageRecord(
            Tool.OpenCode,
            sessionId,
            providerId is null ? modelId : $"{providerId}/{modelId}",
            timestamp,
            input,
            cacheWrite,
            cacheRead,
            output,
            reasoning,
            id);
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

    private static void StoreMessage(JsonElement message, Dictionary<string, JsonElement> messages)
    {
        if (ReadString(message, "id") is { Length: > 0 } id)
        {
            messages[id] = message.Clone();
        }
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
