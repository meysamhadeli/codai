using System.Text.Json;
using System.Text.Json.Serialization;
using Codai.Core.Abstractions;

namespace Codai.Core.Chat;

/// <summary>
/// Persists the conversation as JSON under <c>.codai/sessions/latest.json</c>.
/// </summary>
/// <remarks>
/// The file lives inside the project, which is what makes <c>--continue</c> work
/// without a global cache directory to discover. It is hidden from the project scan by
/// the built-in ignore rules, so the assistant never reads its own transcript back as
/// if it were project context.
/// </remarks>
public sealed class FileSessionStore : ISessionStore
{
    /// <summary>Folder, relative to the project root, that holds the session state.</summary>
    public const string RelativeFolder = ".codai/sessions";

    /// <summary>Name of the session file.</summary>
    public const string FileName = "latest.json";

    /// <summary>Format version, so an incompatible future file can be detected.</summary>
    public const int CurrentVersion = 1;

    /// <summary>
    /// Largest session file that will be read back. A transcript written by Codai stays
    /// far below this; anything larger did not come from here and must not be pushed
    /// into the context window.
    /// </summary>
    public const long MaxFileSizeBytes = 4 * 1024 * 1024;

    private readonly IFileSystem _fileSystem;

    /// <summary>Creates the store.</summary>
    public FileSessionStore(IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        _fileSystem = fileSystem;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ChatTurn>> LoadAsync(
        string rootDirectory,
        CancellationToken cancellationToken = default)
        => await LoadPathAsync(GetPath(rootDirectory), cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ChatTurn>> LoadAsync(
        string rootDirectory,
        string taskId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);
        return await LoadPathAsync(GetTaskPath(rootDirectory, taskId), cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<ChatTurn>> LoadPathAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!_fileSystem.FileExists(path))
        {
            return [];
        }

        try
        {
            if (_fileSystem.GetFileSize(path) > MaxFileSizeBytes)
            {
                return [];
            }

            var json = await _fileSystem.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            var stored = JsonSerializer.Deserialize(json, SessionJsonContext.Default.StoredSession);

            if (stored is null || stored.Version != CurrentVersion)
            {
                return [];
            }

            return stored.Turns;
        }
        catch (JsonException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(
        string rootDirectory,
        IReadOnlyList<ChatTurn> turns,
        CancellationToken cancellationToken = default)
        => await SavePathAsync(GetPath(rootDirectory), rootDirectory, turns, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task SaveAsync(
        string rootDirectory,
        string taskId,
        IReadOnlyList<ChatTurn> turns,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);
        await SavePathAsync(GetTaskPath(rootDirectory, taskId), rootDirectory, turns, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task SavePathAsync(
        string path,
        string rootDirectory,
        IReadOnlyList<ChatTurn> turns,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentNullException.ThrowIfNull(turns);

        if (turns.Count == 0)
        {
            Delete(path);
            return;
        }

        var stored = new StoredSession(
            CurrentVersion,
            DateTimeOffset.UtcNow,
            [.. turns]);

        var json = JsonSerializer.Serialize(stored, SessionJsonContext.Default.StoredSession);

        _fileSystem.CreateDirectory(Path.Combine(rootDirectory, RelativeFolder));
        await _fileSystem.WriteAllTextAtomicallyAsync(path, json, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Absolute path of the session file for <paramref name="rootDirectory"/>.</summary>
    public static string GetPath(string rootDirectory) =>
        Path.Combine(
            rootDirectory,
            RelativeFolder.Replace('/', Path.DirectorySeparatorChar),
            FileName);

    public static string GetTaskPath(string rootDirectory, string taskId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);
        return Path.Combine(
            rootDirectory,
            RelativeFolder.Replace('/', Path.DirectorySeparatorChar),
            $"{taskId}.json");
    }

    private void Delete(string path)
    {
        try
        {
            if (_fileSystem.FileExists(path))
            {
                _fileSystem.DeleteFile(path);
            }
        }
        catch (IOException)
        {
            // Forgetting the session is best-effort.
        }
        catch (UnauthorizedAccessException)
        {
            // Forgetting the session is best-effort.
        }
    }

    /// <summary>The on-disk shape of a stored session.</summary>
    internal sealed record StoredSession(
        int Version,
        DateTimeOffset SavedAt,
        ChatTurn[] Turns);
}

/// <summary>Source-generated JSON used by <see cref="FileSessionStore"/>.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(FileSessionStore.StoredSession))]
internal sealed partial class SessionJsonContext : JsonSerializerContext;
