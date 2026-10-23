using System.Text.Json;
using Codai.Core.Abstractions;

namespace Codai.Core.Agents;

/// <summary>Persists the latest task under <c>.codai/tasks/latest.json</c>.</summary>
public sealed class FileAgentTaskStore : IAgentTaskStore
{
    public const string RelativeFolder = ".codai/tasks";
    public const string FileName = "latest.json";
    public const int CurrentExportSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly IFileSystem _fileSystem;

    public FileAgentTaskStore(IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        _fileSystem = fileSystem;
    }

    public async Task<AgentTask?> LoadAsync(
        string rootDirectory,
        CancellationToken cancellationToken = default)
    {
        var path = GetPath(rootDirectory);
        if (!_fileSystem.FileExists(path))
        {
            return null;
        }

        try
        {
            var json = await _fileSystem.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            return DeserializeEnvelope(json).Task;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (InvalidDataException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<AgentTask>> ListAsync(
        string rootDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);

        var folder = Path.Combine(rootDirectory, RelativeFolder.Replace('/', Path.DirectorySeparatorChar));
        if (!_fileSystem.DirectoryExists(folder))
        {
            return [];
        }

        var tasks = new List<AgentTask>();
        foreach (var path in _fileSystem.EnumerateFiles(folder))
        {
            if (string.Equals(Path.GetFileName(path), FileName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                var json = await _fileSystem.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
                if (DeserializeEnvelope(json).Task is { } task)
                {
                    tasks.Add(task);
                }
            }
            catch (JsonException)
            {
            }
            catch (InvalidDataException)
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
        AgentTask task,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentNullException.ThrowIfNull(task);

        var path = GetPath(rootDirectory);
        _fileSystem.CreateDirectory(Path.Combine(rootDirectory, RelativeFolder));
        var json = SerializeEnvelope(task);
        await _fileSystem.WriteAllTextAtomicallyAsync(path, json, cancellationToken).ConfigureAwait(false);
        await _fileSystem.WriteAllTextAtomicallyAsync(GetTaskPath(rootDirectory, task.Id), json, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task ExportAsync(
        string rootDirectory,
        string taskId,
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var task = (await ListAsync(rootDirectory, cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(item => item.Id.Equals(taskId, StringComparison.OrdinalIgnoreCase));
        if (task is null)
        {
            throw new FileNotFoundException($"Task not found: {taskId}");
        }

        await _fileSystem.WriteAllTextAtomicallyAsync(path, SerializeEnvelope(task), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task ImportAsync(
        string rootDirectory,
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var json = await _fileSystem.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        var task = DeserializeEnvelope(json).Task!;
        await SaveAsync(rootDirectory, task, cancellationToken).ConfigureAwait(false);
    }

    private static string SerializeEnvelope(AgentTask task) =>
        JsonSerializer.Serialize(new AgentTaskExport(CurrentExportSchemaVersion, task), JsonOptions);

    private static AgentTaskExport DeserializeEnvelope(string json)
    {
        try
        {
            var envelope = JsonSerializer.Deserialize<AgentTaskExport>(json)
                ?? throw new InvalidDataException("Task export is empty.");
            if (envelope.SchemaVersion != CurrentExportSchemaVersion)
            {
                throw new InvalidDataException(
                    $"Unsupported task export schema version: {envelope.SchemaVersion}.");
            }

            if (envelope.Task is null || string.IsNullOrWhiteSpace(envelope.Task.Id))
            {
                throw new InvalidDataException("Task export does not contain valid task metadata.");
            }

            // Schema 1 stored the removed read-only Ask mode as 0.
            if ((int)envelope.Task.Mode == 0)
            {
                return envelope with { Task = envelope.Task with { Mode = AgentMode.Plan } };
            }

            return envelope;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Task export is not valid JSON.", ex);
        }
    }

    public static string GetPath(string rootDirectory) =>
        Path.Combine(rootDirectory, RelativeFolder.Replace('/', Path.DirectorySeparatorChar), FileName);

    public static string GetTaskPath(string rootDirectory, string taskId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);
        return Path.Combine(
            rootDirectory,
            RelativeFolder.Replace('/', Path.DirectorySeparatorChar),
            $"{taskId}.json");
    }
}

/// <summary>Schema-versioned, metadata-only task export.</summary>
public sealed record AgentTaskExport(int SchemaVersion, AgentTask? Task);