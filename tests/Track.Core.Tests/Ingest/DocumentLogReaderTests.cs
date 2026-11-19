using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Track.Core.Ingest;
using Track.Core.Parsing;

namespace Track.Core.Tests.Ingest;

public class DocumentLogReaderTests
{
    private static readonly DateTimeOffset KeepFrom = new(2026, 11, 18, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Timestamp = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Gemini_counts_every_billed_response_once_with_thoughts_and_tool_prompts()
    {
        const string path = @"C:\home\.gemini\tmp\session-a.jsonl";
        var fs = new MockFileSystem();
        fs.AddFile(path, new MockFileData(
            """{"$set":{"sessionId":"gemini-session","messages":[{"id":"old","type":"gemini","timestamp":"2026-11-18T10:00:00Z","model":"old-model","tokens":{"input":5,"cached":0,"output":0}}]}}""" + "\n"
            + GeminiMessage("first", "gemini-2.5", 100, 20, 30, 5) + "\n"
            + """{"$patch":{"removeIds":["old",42]}}""" + "\n"
            + GeminiMessage("second", "gemini-2.5-pro", 1, 0, 1, 0) + "\n"
            + GeminiMessage("second", "gemini-2.5-pro", 50, 10, 15, 2) + "\n"
            + """{"id":"tool","type":"gemini","timestamp":"2026-11-18T12:00:00Z","model":"m","tokens":{"input":10,"cached":0,"output":1,"tool":7}}""" + "\n"
            + """{"$rewindTo":"first"}""" + "\n"
            + "not json\n"
            + """{"type":"user","id":"ignored"}""" + "\n"));
        var file = new LogFile(Tool.Gemini, path, "filename-session", Timestamp, LogReadMode.GeminiJsonLines);

        var records = new DocumentLogReader(fs).ReadChanged(file, KeepFrom)!.Records.ToDictionary(r => r.DedupeKey);

        // Removed and rewound responses were still sent and billed: all stay. A rewritten id counts once (last version).
        Assert.Equal(["first", "old", "second", "tool"], records.Keys.Order());
        Assert.All(records.Values, r => Assert.Equal("gemini-session", r.SessionId));
        Assert.Equal("gemini-2.5-pro", records["second"].Model);
        // input excludes cached; output adds thoughts (also reported as reasoning); tool prompt tokens are input.
        Assert.Equal((80L, 0L, 20L, 35L, 5L), Parts(records["first"]));
        Assert.Equal((40L, 0L, 10L, 17L, 2L), Parts(records["second"]));
        Assert.Equal((17L, 0L, 0L, 1L, 0L), Parts(records["tool"]));
        Assert.Equal((5L, 0L, 0L, 0L, 0L), Parts(records["old"]));

        static (long, long, long, long, long) Parts(UsageRecord r) => (r.Input, r.CacheWrite, r.CacheRead, r.Output, r.Reasoning);
    }

    [Theory]
    [InlineData(Tool.Cline)]
    [InlineData(Tool.Roo)]
    public void Task_message_json_reads_request_tokens_without_double_subtracting_cache(Tool tool)
    {
        var path = $@"C:\home\{tool}\task\ui_messages.json";
        var timestamp = Timestamp.ToUnixTimeMilliseconds();
        var fs = new MockFileSystem();
        var messages = JsonSerializer.Serialize(new[]
        {
            new
            {
                type = "say",
                say = "api_req_started",
                ts = (object)timestamp,
                text = JsonSerializer.Serialize(new { tokensIn = tool == Tool.Roo ? 130 : 100, tokensOut = 30, cacheWrites = 10, cacheReads = 20 }),
                modelInfo = new { providerId = "anthropic", modelId = "claude-sonnet-4" },
            },
            new { type = "say", say = "api_req_started", ts = (object)(timestamp - 1000), text = "not json", modelInfo = new { providerId = "anthropic", modelId = "claude-sonnet-4" } },
            new { type = "say", say = "api_req_started", ts = (object)timestamp, text = "[]", modelInfo = new { providerId = "anthropic", modelId = "claude-sonnet-4" } },
            new { type = "say", say = "api_req_started", ts = (object)timestamp, text = JsonSerializer.Serialize(new { tokensIn = 0, tokensOut = 0 }), modelInfo = new { providerId = "anthropic", modelId = "claude-sonnet-4" } },
            new { type = "say", say = "api_req_started", ts = (object)long.MaxValue, text = JsonSerializer.Serialize(new { tokensIn = 1, tokensOut = 1 }), modelInfo = new { providerId = "anthropic", modelId = "claude-sonnet-4" } },
            new { type = "say", say = "api_req_started", ts = (object)1.5, text = JsonSerializer.Serialize(new { tokensIn = 1, tokensOut = 1 }), modelInfo = new { providerId = "anthropic", modelId = "claude-sonnet-4" } },
            new { type = "say", say = "api_req_started", ts = (object)"invalid", text = JsonSerializer.Serialize(new { tokensIn = 1, tokensOut = 1 }), modelInfo = new { providerId = "anthropic", modelId = "claude-sonnet-4" } },
            new { type = "say", say = "other", ts = (object)timestamp, text = JsonSerializer.Serialize(new { tokensIn = 1, tokensOut = 1 }), modelInfo = new { providerId = "anthropic", modelId = "claude-sonnet-4" } },
        });
        fs.AddFile(path, new MockFileData(messages));
        var reader = new DocumentLogReader(fs);
        var file = new LogFile(tool, path, "task-id", Timestamp, LogReadMode.JsonDocument);

        var record = Assert.Single(reader.ReadChanged(file, KeepFrom)!.Records);

        Assert.Equal("task-id", record.SessionId);
        Assert.Equal("anthropic/claude-sonnet-4", record.Model);
        Assert.Equal((100L, 10L, 20L, 30L), (record.Input, record.CacheWrite, record.CacheRead, record.Output));
    }

    [Fact]
    public void Task_message_without_model_metadata_uses_unknown()
    {
        const string path = @"C:\home\Cline\task\ui_messages.json";
        var fs = new MockFileSystem();
        fs.AddFile(path, new MockFileData(JsonSerializer.Serialize(new[]
        {
            new
            {
                type = "say",
                say = "api_req_started",
                ts = (object)Timestamp.ToUnixTimeMilliseconds(),
                text = JsonSerializer.Serialize(new { tokensIn = 1, tokensOut = 2 }),
            },
        })));
        var reader = new DocumentLogReader(fs);
        var file = new LogFile(Tool.Cline, path, "task", Timestamp, LogReadMode.JsonDocument);

        var record = Assert.Single(reader.ReadChanged(file, KeepFrom)!.Records);

        Assert.Equal("Unknown", record.Model);
    }

    [Fact]
    public void Task_message_model_metadata_may_omit_the_provider_or_model()
    {
        const string path = @"C:\home\Cline\task\ui_messages.json";
        var timestamp = Timestamp.ToUnixTimeMilliseconds();
        var fs = new MockFileSystem();
        fs.AddFile(path, new MockFileData(
            """[{"type":"say","say":"api_req_started","ts":""" + timestamp + ""","text":"{\"tokensIn\":1}","modelInfo":{"modelId":"model-only"}},{"type":"say","say":"api_req_started","ts":""" + timestamp + ""","text":"{\"tokensIn\":1}","modelInfo":{"providerId":"anthropic"}},{"type":"say","say":"api_req_started","ts":""" + timestamp + ""","text":"{\"tokensIn\":1}","modelInfo":42}]"""));
        var reader = new DocumentLogReader(fs);
        var file = new LogFile(Tool.Cline, path, "task", Timestamp, LogReadMode.JsonDocument);

        var records = reader.ReadChanged(file, KeepFrom)!.Records;

        Assert.Equal(["model-only", "Unknown", "Unknown"], records.Select(record => record.Model));
    }

    [Fact]
    public void Empty_task_model_provider_uses_the_model_id()
    {
        const string path = @"C:\home\Cline\task\ui_messages.json";
        var timestamp = Timestamp.ToUnixTimeMilliseconds();
        var fs = new MockFileSystem();
        fs.AddFile(path, new MockFileData(
            """[{"type":"say","say":"api_req_started","ts":""" + timestamp + ""","text":"{\"tokensIn\":1}","modelInfo":{"providerId":"","modelId":"model-only"}}]"""));
        var reader = new DocumentLogReader(fs);
        var file = new LogFile(Tool.Cline, path, "task", Timestamp, LogReadMode.JsonDocument);

        Assert.Equal("model-only", Assert.Single(reader.ReadChanged(file, KeepFrom)!.Records).Model);
    }

    [Fact]
    public void Task_message_without_a_timestamp_is_ignored()
    {
        const string path = @"C:\home\Cline\task\ui_messages.json";
        var fs = new MockFileSystem();
        fs.AddFile(path, new MockFileData("""[[],{"type":"say","say":"api_req_started","text":"{\"tokensIn\":1}"}]"""));
        var reader = new DocumentLogReader(fs);
        var file = new LogFile(Tool.Cline, path, "task", Timestamp, LogReadMode.JsonDocument);

        Assert.Empty(reader.ReadChanged(file, KeepFrom)!.Records);
    }

    [Fact]
    public void Gemini_skips_malformed_and_empty_records_and_reads_legacy_messages()
    {
        const string path = @"C:\home\.gemini\tmp\session-a.jsonl";
        var fs = new MockFileSystem();
        fs.AddFile(path, new MockFileData(
            "\n[]\n"
            + """{"$set":{"sessionId":"gemini-session"}}""" + "\n"
            + """{"$set":{"sessionId":""}}""" + "\n"
            + """{"$set":[]}""" + "\n"
            + """{"$rewindTo":"missing"}""" + "\n"
            + """{"sessionId":42}""" + "\n"
            + """{"projectHash":"legacy","messages":[{"id":"","type":"gemini","timestamp":"2026-11-18T12:00:00Z","tokens":{"input":1}},{"id":"missing-id","type":"gemini"},{"id":"bad-time","type":"gemini","timestamp":"not-a-time"},{"id":"missing-time","type":"gemini"},{"id":"no-tokens","type":"gemini","timestamp":"2026-11-18T12:00:00Z"},{"id":"zero","type":"gemini","timestamp":"2026-11-18T12:00:00Z","tokens":{"input":0,"cached":0,"output":0}},{"id":"wrong-type","type":"user","timestamp":"2026-11-18T12:00:00Z","tokens":{"input":1}},{"id":"missing-type","timestamp":"2026-11-18T12:00:00Z","tokens":{"input":1}},{"id":"non-string-type","type":1,"timestamp":"2026-11-18T12:00:00Z","tokens":{"input":1}},{"id":"valid","type":"gemini","timestamp":"2026-11-18T12:00:00Z","model":"old","tokens":{"input":1}},{"id":"valid","type":"gemini","timestamp":"2026-11-18T12:00:00Z","tokens":{"input":10,"cached":2,"output":4,"thoughts":5}}]}""" + "\n"));
        var reader = new DocumentLogReader(fs);
        var file = new LogFile(Tool.Gemini, path, "filename-session", Timestamp, LogReadMode.GeminiJsonLines);

        var record = Assert.Single(reader.ReadChanged(file, KeepFrom)!.Records);

        Assert.Equal(("gemini-session", "Unknown", "valid"), (record.SessionId, record.Model, record.DedupeKey));
        Assert.Equal((8L, 2L, 9L, 5L), (record.Input, record.CacheRead, record.Output, record.Reasoning));
    }

    [Theory]
    [InlineData(Tool.Claude, LogReadMode.GeminiJsonLines)]
    [InlineData(Tool.Claude, LogReadMode.JsonDocument)]
    [InlineData(Tool.Claude, LogReadMode.OpenCodeDatabase)]
    [InlineData(Tool.Claude, (LogReadMode)99)]
    public void Unsupported_document_parser_combinations_are_rejected(Tool tool, LogReadMode mode)
    {
        const string path = @"C:\home\unsupported.log";
        var fs = new MockFileSystem();
        fs.AddFile(path, new MockFileData("{}"));
        var reader = new DocumentLogReader(fs);
        var file = new LogFile(tool, path, "session", Timestamp, mode);

        Assert.Throws<ArgumentOutOfRangeException>(() => reader.ReadChanged(file, KeepFrom));
    }

    [Fact]
    public void OpenCode_reads_assistant_usage_from_a_read_only_database()
    {
        var path = Path.Combine(Path.GetTempPath(), $"track-opencode-{Guid.NewGuid():N}.db");
        try
        {
            var builder = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false };
            using var connection = new SqliteConnection(builder.ToString());
            connection.Open();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA journal_mode=WAL;";
                command.ExecuteNonQuery();
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = """
                    CREATE TABLE session_message (
                        id TEXT,
                        session_id TEXT,
                        time_created INTEGER,
                        type TEXT,
                        data TEXT
                    );
                    INSERT INTO session_message VALUES (
                        'message-1',
                        'session-1',
                        $timestamp,
                        'assistant',
                        '{"model":{"providerID":"openai","id":"gpt-5.6"},"tokens":{"input":100,"output":30,"reasoning":5,"cache":{"read":20,"write":10}},"cost":0.01}'
                    );
                    INSERT INTO session_message VALUES (
                        'user-message',
                        'session-1',
                        $timestamp,
                        'user',
                        '{}'
                    );
                    INSERT INTO session_message VALUES ('no-tokens', 'session-1', $timestamp, 'assistant', '{}');
                    INSERT INTO session_message VALUES ('no-model', 'session-1', $timestamp, 'assistant', '{"tokens":{"input":1}}');
                    INSERT INTO session_message VALUES ('zero-tokens', 'session-1', $timestamp, 'assistant', '{"model":{"id":"empty"},"tokens":{"input":0,"output":0}}');
                    INSERT INTO session_message VALUES ('without-provider', 'session-1', $timestamp, 'assistant', '{"model":{"id":"gpt-4"},"tokens":{"input":10}}');
                    INSERT INTO session_message VALUES ('invalid-token-values', 'session-1', $timestamp, 'assistant', '{"model":{"id":"empty-invalid"},"tokens":{"input":"bad","output":-1,"cache":{"read":1.5,"write":9223372036854775808}}}');
                    INSERT INTO session_message VALUES (NULL, 'session-1', $timestamp, 'assistant', '{"model":{"id":"m"},"tokens":{"input":1}}');
                    INSERT INTO session_message VALUES ('null-data', 'session-1', $timestamp, 'assistant', NULL);
                    INSERT INTO session_message VALUES ('text-time', 'session-1', 'later', 'assistant', '{"model":{"id":"m"},"tokens":{"input":1}}');
                    INSERT INTO session_message VALUES ('huge-time', 'session-1', 9223372036854775807, 'assistant', '{"model":{"id":"m"},"tokens":{"input":1}}');
                    INSERT INTO session_message VALUES ('broken-json', 'session-1', $timestamp, 'assistant', '{"model":');
                    INSERT INTO session_message VALUES ('array-data', 'session-1', $timestamp, 'assistant', '[1]');
                    """;
                command.Parameters.AddWithValue("$timestamp", Timestamp.ToUnixTimeMilliseconds());
                command.ExecuteNonQuery();
            }

            var reader = new DocumentLogReader(new FileSystem());
            var file = new LogFile(Tool.OpenCode, path, "opencode.db", Timestamp, LogReadMode.OpenCodeDatabase);
            var records = reader.ReadChanged(file, KeepFrom)!.Records;
            var record = Assert.Single(records, r => r.DedupeKey == "message-1");

            Assert.Equal(("session-1", "openai/gpt-5.6"), (record.SessionId, record.Model));
            // OpenCode's output excludes reasoning; both are billed as output.
            Assert.Equal((100L, 10L, 20L, 35L, 5L), (record.Input, record.CacheWrite, record.CacheRead, record.Output, record.Reasoning));
            Assert.Equal("message-1", record.DedupeKey);
            Assert.Equal("gpt-4", Assert.Single(records, r => r.DedupeKey == "without-provider").Model);
            // The odd rows are skipped; the good ones still count.
            Assert.Equal(["message-1", "without-provider"], records.Select(r => r.DedupeKey).Order());
            Assert.Null(reader.ReadChanged(file, KeepFrom));

            var sidecarReader = new DocumentLogReader(new FileSystem());
            Assert.Equal(records.Count, sidecarReader.ReadChanged(file with { Path = path + "-shm" }, KeepFrom)!.Records.Count);
            var walReader = new DocumentLogReader(new FileSystem());
            Assert.Equal(records.Count, walReader.ReadChanged(file with { Path = path + "-wal" }, KeepFrom)!.Records.Count);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void Malformed_task_document_contributes_nothing_and_is_not_parsed_again_until_it_changes()
    {
        const string path = @"C:\home\Cline\task\ui_messages.json";
        var fs = new MockFileSystem();
        fs.AddFile(path, new MockFileData("""{"not":"an array"}"""));
        var reader = new DocumentLogReader(fs);
        var file = new LogFile(Tool.Cline, path, "task", Timestamp, LogReadMode.JsonDocument);

        Assert.Null(reader.ReadChanged(file, KeepFrom));
        // Its version is remembered, so an unchanged broken document is not parsed every refresh.
        Assert.Null(Assert.Single(reader.SaveCursors()).Contribution);
        Assert.Null(reader.ReadChanged(file, KeepFrom));
    }

    [Fact]
    public void A_document_that_breaks_is_read_again_once_it_changes()
    {
        const string path = @"C:\home\Cline\task\ui_messages.json";
        var fs = new MockFileSystem();
        var ts = Timestamp.ToUnixTimeMilliseconds();
        var good = $$"""[{"ts":{{ts}},"type":"say","say":"api_req_started","text":"{\"tokensIn\":10,\"tokensOut\":5}"}]""";
        fs.AddFile(path, new MockFileData(good));
        var reader = new DocumentLogReader(fs);
        var file = new LogFile(Tool.Cline, path, "task", Timestamp, LogReadMode.JsonDocument);
        Assert.Single(reader.ReadChanged(file, KeepFrom)!.Records);

        fs.File.WriteAllText(path, good[..^5]);
        Assert.Null(reader.ReadChanged(file, KeepFrom));
        Assert.Null(reader.ReadChanged(file, KeepFrom));

        fs.File.WriteAllText(path, good + " ");
        Assert.Single(reader.ReadChanged(file, KeepFrom)!.Records);
    }

    [Theory]
    [InlineData(@"C:\home\.local\share\opencode\opencode-next.db")]
    [InlineData(@"C:\home\.local\share\opencode\opencode-next.db-wal")]
    [InlineData(@"C:\home\.local\share\opencode\opencode-backup.db")]
    [InlineData(@"C:\home\.local\share\opencode\opencode copy.db")]
    public void Copies_of_the_opencode_database_are_not_counted_again(string path)
    {
        var fs = new MockFileSystem();
        fs.AddFile(path, new MockFileData("same data as opencode.db"));
        var reader = new DocumentLogReader(fs);

        Assert.Null(reader.ReadChanged(new LogFile(Tool.OpenCode, path, "opencode", Timestamp, LogReadMode.OpenCodeDatabase), KeepFrom));
        Assert.Empty(reader.SaveCursors());
    }

    [Fact]
    public void A_database_that_cannot_be_opened_is_tried_again_next_time()
    {
        var path = Path.Combine(Path.GetTempPath(), $"track-opencode-{Guid.NewGuid():N}.db");
        try
        {
            File.WriteAllText(path, "not a database, or one being rewritten right now");
            var reader = new DocumentLogReader(new FileSystem());
            var file = new LogFile(Tool.OpenCode, path, "opencode", Timestamp, LogReadMode.OpenCodeDatabase);

            Assert.Null(reader.ReadChanged(file, KeepFrom));
            Assert.Empty(reader.SaveCursors());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Missing_document_and_database_paths_are_ignored()
    {
        var fs = new MockFileSystem();
        var reader = new DocumentLogReader(fs);

        Assert.Null(reader.ReadChanged(new LogFile(Tool.Cline, @"C:\home\missing.json", "task", Timestamp, LogReadMode.JsonDocument), KeepFrom));
        Assert.Null(reader.ReadChanged(new LogFile(Tool.OpenCode, @"C:\home\opencode.db", "session", Timestamp, LogReadMode.OpenCodeDatabase), KeepFrom));
    }

    private static string GeminiMessage(string id, string model, long input, long cached, long output, long thoughts) =>
        JsonSerializer.Serialize(new
        {
            id,
            type = "gemini",
            timestamp = "2026-11-18T12:00:00Z",
            model,
            tokens = new { input, cached, output, thoughts },
        });
}
