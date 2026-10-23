using System.Text.Json;
using Codai.Core.Abstractions;

namespace Codai.Core.Agents;

public sealed class FileBackgroundAgentStore : IBackgroundAgentStore
{
    public const string RelativeFolder = ".codai/tasks/background";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly IFileSystem _fileSystem;

    public FileBackgroundAgentStore(IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        _fileSystem = fileSystem;
    }

    public async Task<IReadOnlyList<BackgroundAgentInfo>> LoadAsync(
        string rootDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        var folder = Path.Combine(rootDirectory, RelativeFolder.Replace('/', Path.DirectorySeparatorChar));
        if (!_fileSystem.DirectoryExists(folder))
        {
            return [];
        }

        var tasks = new List<BackgroundAgentInfo>();
        foreach (var path in _fileSystem.EnumerateFiles(folder))
        {
            try
            {
                var json = await _fileSystem.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
                if (JsonSerializer.Deserialize<BackgroundAgentInfo>(json) is { } task)
                {
                    tasks.Add(task);
                }
            }
            catch (JsonException)
            {
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return tasks.OrderByDescending(task => task.UpdatedAt).ToArray();
    }

    public async Task SaveAsync(
        string rootDirectory,
        BackgroundAgentInfo task,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentNullException.ThrowIfNull(task);

        var folder = Path.Combine(rootDirectory, RelativeFolder.Replace('/', Path.DirectorySeparatorChar));
        _fileSystem.CreateDirectory(folder);
        var json = JsonSerializer.Serialize(task, JsonOptions);
        await _fileSystem.WriteAllTextAtomicallyAsync(
            Path.Combine(folder, $"{task.Id}.json"),
            json,
            cancellationToken).ConfigureAwait(false);
    }
}