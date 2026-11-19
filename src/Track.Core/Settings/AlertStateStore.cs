using System.IO.Abstractions;
using System.Text.Json;

namespace Track.Core.Settings;

public sealed record AlertStateLoadResult(bool Success, IReadOnlySet<string> FiredKeys, string? Error);

public sealed record AlertStateSaveResult(bool Success, string? Error);

/// <summary>Persists budget threshold fire keys separately from user-facing settings.</summary>
public sealed class AlertStateStore(IFileSystem fileSystem, string path)
{
    public AlertStateLoadResult Load()
    {
        if (!fileSystem.File.Exists(path))
        {
            return new AlertStateLoadResult(true, new HashSet<string>(StringComparer.Ordinal), null);
        }

        try
        {
            var keys = JsonSerializer.Deserialize<string[]>(fileSystem.File.ReadAllText(path));
            return keys is null
                ? new AlertStateLoadResult(false, new HashSet<string>(StringComparer.Ordinal), "Budget alert state is empty or invalid.")
                : new AlertStateLoadResult(true, keys.ToHashSet(StringComparer.Ordinal), null);
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException)
        {
            return new AlertStateLoadResult(false, new HashSet<string>(StringComparer.Ordinal), error.Message);
        }
    }

    public AlertStateSaveResult Save(IReadOnlySet<string> firedKeys)
    {
        var temp = path + ".tmp";
        try
        {
            fileSystem.Directory.CreateDirectory(fileSystem.Path.GetDirectoryName(path)!);
            fileSystem.File.WriteAllText(temp, JsonSerializer.Serialize(firedKeys.Order(StringComparer.Ordinal).ToArray()));
            fileSystem.File.Move(temp, path, overwrite: true);
            return new AlertStateSaveResult(true, null);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return new AlertStateSaveResult(false, error.Message);
        }
    }
}
