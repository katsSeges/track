using System.IO.Abstractions;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Track.Core.Ingest;

/// <summary>Everything Track needs to continue after a restart without rereading old logs.</summary>
public sealed record Snapshot(
    int Version,
    DateTimeOffset SavedUtc,
    IReadOnlyList<Snapshot.Bucket> Buckets,
    IReadOnlyList<Snapshot.Session> Sessions,
    IReadOnlyList<FileCursor> Cursors)
{
    public IReadOnlyList<DocumentCursor> DocumentCursors { get; init; } = [];

    public sealed record Bucket(Tool Tool, string Model, DateTimeOffset HourUtc, TokenCounts Tokens);

    public sealed record Session(Tool Tool, string SessionId, SessionUsage Usage);
}

/// <summary>Compile-time JSON code for the snapshot: no reflection, fast start, safe for trimmed builds.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true)]
[JsonSerializable(typeof(Snapshot))]
internal sealed partial class SnapshotJson : JsonSerializerContext;

/// <summary>
/// Saves and loads <see cref="Snapshot"/> as JSON (default <c>%LOCALAPPDATA%\Track\snapshot.json</c>).
/// Saving writes a temporary file and then swaps it in, so a crash never leaves half a file.
/// Loading returns null for a missing, unreadable, corrupt or older-version file, which means
/// "read all logs again".
/// </summary>
public sealed class SnapshotStore(IFileSystem fileSystem, string path)
{
    public const int CurrentVersion = 1;

    public string FilePath => path;

    public static string DefaultPath(string localAppData) => Path.Combine(localAppData, "Track", "snapshot.json");

    public Snapshot? Load()
    {
        try
        {
            if (!fileSystem.File.Exists(path))
            {
                return null;
            }

            var snapshot = JsonSerializer.Deserialize(fileSystem.File.ReadAllText(path), SnapshotJson.Default.Snapshot);
            return snapshot?.Version == CurrentVersion
                ? snapshot with { DocumentCursors = snapshot.DocumentCursors ?? [] }
                : null;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Returns false if the file could not be written (Track keeps running either way).</summary>
    public bool Save(Snapshot snapshot)
    {
        var temp = path + ".tmp";
        try
        {
            fileSystem.Directory.CreateDirectory(fileSystem.Path.GetDirectoryName(path)!);
            fileSystem.File.WriteAllText(temp, JsonSerializer.Serialize(snapshot, SnapshotJson.Default.Snapshot));
            fileSystem.File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
