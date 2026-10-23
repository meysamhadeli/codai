using System.Text;
using System.Text.Json;
using Codai.Core.Abstractions;
using Codai.Core.Chat;

namespace Codai.Core.Agents;

public sealed class FileWorkspaceCheckpointStore : IWorkspaceCheckpointStore, IWorkspaceCheckpointDiagnostics
{
    public const string RelativeFolder = ".codai/checkpoints";
    public const long MaxFileBytes = 2 * 1024 * 1024;
    public const long MaxSnapshotBytes = 32 * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };
    private readonly IFileSystem _fileSystem;
    private readonly int _retention;

    public FileWorkspaceCheckpointStore(IFileSystem fileSystem, int retention = 10)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _retention = retention >= 1 ? retention : throw new ArgumentOutOfRangeException(nameof(retention));
    }

    public async Task<WorkspaceCheckpoint> CaptureAsync(
        string projectRoot,
        string taskId,
        string label,
        CancellationToken cancellationToken = default)
    {
        Validate(projectRoot, taskId, label);
        var checkpoint = new WorkspaceCheckpoint(Guid.NewGuid().ToString("N"), taskId, label, DateTimeOffset.UtcNow);
        var files = new List<SnapshotFile>();
        long totalBytes = 0;

        foreach (var path in EnumerateFiles(projectRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var size = _fileSystem.GetFileSize(path);
            if (size > MaxFileBytes)
            {
                continue;
            }

            var contents = await _fileSystem.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            totalBytes += contents.Length;
            if (totalBytes > MaxSnapshotBytes)
            {
                throw new IOException($"Checkpoint exceeds {MaxSnapshotBytes} characters.");
            }

            files.Add(new SnapshotFile(Path.GetRelativePath(projectRoot, path), contents));
        }

        var folder = GetCheckpointFolder(projectRoot, checkpoint);
        _fileSystem.CreateDirectory(folder);
        await _fileSystem.WriteAllTextAtomicallyAsync(
            Path.Combine(folder, "checkpoint.json"),
            JsonSerializer.Serialize(checkpoint, JsonOptions),
            cancellationToken).ConfigureAwait(false);
        var snapshot = JsonSerializer.Serialize(new Snapshot(files), JsonOptions);
        await _fileSystem.WriteAllTextAtomicallyAsync(Path.Combine(folder, "snapshot.json"), snapshot, cancellationToken)
            .ConfigureAwait(false);
        var taskPath = FileAgentTaskStore.GetTaskPath(projectRoot, taskId);
        if (_fileSystem.FileExists(taskPath))
        {
            var task = await _fileSystem.ReadAllTextAsync(taskPath, cancellationToken).ConfigureAwait(false);
            await _fileSystem.WriteAllTextAtomicallyAsync(Path.Combine(folder, "task.json"), task, cancellationToken)
                .ConfigureAwait(false);
        }

        var sessionPath = FileSessionStore.GetTaskPath(projectRoot, taskId);
        if (_fileSystem.FileExists(sessionPath))
        {
            var session = await _fileSystem.ReadAllTextAsync(sessionPath, cancellationToken).ConfigureAwait(false);
            await _fileSystem.WriteAllTextAtomicallyAsync(Path.Combine(folder, "session.json"), session, cancellationToken)
                .ConfigureAwait(false);
        }
            await PruneAsync(projectRoot, taskId, cancellationToken).ConfigureAwait(false);
        return checkpoint;
    }

    public async Task<IReadOnlyList<WorkspaceCheckpoint>> ListAsync(
        string projectRoot,
        string taskId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ValidateId(taskId);
        var folder = Path.Combine(projectRoot, RelativeFolder, taskId);
        if (!_fileSystem.DirectoryExists(folder))
        {
            return [];
        }

        var result = new List<WorkspaceCheckpoint>();
        foreach (var checkpointFolder in _fileSystem.EnumerateDirectories(folder))
        {
            var metadataPath = Path.Combine(checkpointFolder, "checkpoint.json");
            if (!_fileSystem.FileExists(metadataPath))
            {
                continue;
            }

            var json = await _fileSystem.ReadAllTextAsync(metadataPath, cancellationToken).ConfigureAwait(false);
            if (JsonSerializer.Deserialize<WorkspaceCheckpoint>(json) is { } checkpoint)
            {
                result.Add(checkpoint);
            }
        }

        return result.OrderByDescending(item => item.CreatedAt).ToArray();
    }

    public async Task RestoreAsync(
        string projectRoot,
        string taskId,
        string checkpointId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ValidateId(taskId);
        ValidateId(checkpointId);
        var folder = Path.Combine(projectRoot, RelativeFolder, taskId, checkpointId);
        var snapshotPath = Path.Combine(folder, "snapshot.json");
        if (!_fileSystem.FileExists(snapshotPath))
        {
            throw new FileNotFoundException("Checkpoint was not found.", checkpointId);
        }

        var snapshot = JsonSerializer.Deserialize<Snapshot>(
            await _fileSystem.ReadAllTextAsync(snapshotPath, cancellationToken).ConfigureAwait(false))
            ?? throw new IOException("Checkpoint snapshot is invalid.");
        var expected = snapshot.Files.Select(file => Normalize(file.RelativePath)).ToHashSet(GetComparer());

        foreach (var path in EnumerateFiles(projectRoot).ToArray())
        {
            if (!expected.Contains(Normalize(Path.GetRelativePath(projectRoot, path))))
            {
                _fileSystem.DeleteFile(path);
            }
        }

        foreach (var file in snapshot.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = Path.Combine(projectRoot, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            _fileSystem.CreateDirectory(Path.GetDirectoryName(path)!);
            await _fileSystem.WriteAllTextAsync(path, file.Contents, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task RestoreAsync(
        string projectRoot,
        string taskId,
        string checkpointId,
        WorkspaceCheckpointRestoreMode mode,
        CancellationToken cancellationToken = default)
    {
        if (mode is WorkspaceCheckpointRestoreMode.FilesOnly or WorkspaceCheckpointRestoreMode.FilesAndTask)
        {
            await RestoreAsync(projectRoot, taskId, checkpointId, cancellationToken).ConfigureAwait(false);
        }

        if (mode is WorkspaceCheckpointRestoreMode.TaskOnly or WorkspaceCheckpointRestoreMode.FilesAndTask)
        {
            var taskPath = Path.Combine(projectRoot, RelativeFolder, taskId, checkpointId, "task.json");
            if (!_fileSystem.FileExists(taskPath))
            {
                throw new NotSupportedException("Checkpoint has no task metadata snapshot.");
            }

            var task = await _fileSystem.ReadAllTextAsync(taskPath, cancellationToken).ConfigureAwait(false);
            _fileSystem.CreateDirectory(Path.Combine(projectRoot, FileAgentTaskStore.RelativeFolder));
            await _fileSystem.WriteAllTextAtomicallyAsync(FileAgentTaskStore.GetPath(projectRoot), task, cancellationToken)
                .ConfigureAwait(false);
            await _fileSystem.WriteAllTextAtomicallyAsync(FileAgentTaskStore.GetTaskPath(projectRoot, taskId), task, cancellationToken)
                .ConfigureAwait(false);

            var sessionPath = Path.Combine(projectRoot, RelativeFolder, taskId, checkpointId, "session.json");
            if (!_fileSystem.FileExists(sessionPath))
            {
                throw new NotSupportedException("Checkpoint has no conversation snapshot.");
            }

            var session = await _fileSystem.ReadAllTextAsync(sessionPath, cancellationToken).ConfigureAwait(false);
            await _fileSystem.WriteAllTextAtomicallyAsync(FileSessionStore.GetTaskPath(projectRoot, taskId), session, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async Task<string> CompareAsync(
        string projectRoot,
        string taskId,
        string checkpointId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ValidateId(taskId);
        ValidateId(checkpointId);
        var snapshotPath = Path.Combine(projectRoot, RelativeFolder, taskId, checkpointId, "snapshot.json");
        if (!_fileSystem.FileExists(snapshotPath))
        {
            throw new FileNotFoundException("Checkpoint was not found.", checkpointId);
        }

        var snapshot = JsonSerializer.Deserialize<Snapshot>(
            await _fileSystem.ReadAllTextAsync(snapshotPath, cancellationToken).ConfigureAwait(false))
            ?? throw new IOException("Checkpoint snapshot is invalid.");
        var expected = snapshot.Files.ToDictionary(file => Normalize(file.RelativePath), GetComparer());
        var current = new Dictionary<string, string>(GetComparer());
        foreach (var path in EnumerateFiles(projectRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            current[Normalize(Path.GetRelativePath(projectRoot, path))] =
                await _fileSystem.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        }

        var diff = new StringBuilder();
        foreach (var path in expected.Keys.Union(current.Keys, GetComparer()).OrderBy(path => path))
        {
            if (!expected.TryGetValue(path, out var before))
            {
                AppendFileDiff(diff, path, null, current[path]);
            }
            else if (!current.TryGetValue(path, out var after))
            {
                AppendFileDiff(diff, path, before.Contents, null);
            }
            else if (!string.Equals(before.Contents, after, StringComparison.Ordinal))
            {
                AppendFileDiff(diff, path, before.Contents, after);
            }
        }

        return diff.ToString().TrimEnd();
    }

    public async Task<WorkspaceCheckpointStorageDiagnostics> GetStorageDiagnosticsAsync(
        string projectRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        var root = Path.Combine(projectRoot, RelativeFolder);
        if (!_fileSystem.DirectoryExists(root))
        {
            return new WorkspaceCheckpointStorageDiagnostics(0, 0);
        }

        var checkpointCount = 0;
        long retainedBytes = 0;
        foreach (var file in EnumerateFilesRecursively(root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            retainedBytes += _fileSystem.GetFileSize(file);
            if (string.Equals(Path.GetFileName(file), "checkpoint.json", StringComparison.OrdinalIgnoreCase))
            {
                checkpointCount++;
            }
        }

        return new WorkspaceCheckpointStorageDiagnostics(checkpointCount, retainedBytes);
    }

    private IEnumerable<string> EnumerateFiles(string directory)
    {
        foreach (var file in _fileSystem.EnumerateFiles(directory))
        {
            yield return file;
        }

        foreach (var child in _fileSystem.EnumerateDirectories(directory))
        {
            if (string.Equals(Path.GetFileName(child), ".codai", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var file in EnumerateFiles(child))
            {
                yield return file;
            }
        }
    }

    private IEnumerable<string> EnumerateFilesRecursively(string directory)
    {
        foreach (var file in _fileSystem.EnumerateFiles(directory))
        {
            yield return file;
        }

        foreach (var child in _fileSystem.EnumerateDirectories(directory))
        {
            foreach (var file in EnumerateFilesRecursively(child))
            {
                yield return file;
            }
        }
    }

    private static string GetCheckpointFolder(string projectRoot, WorkspaceCheckpoint checkpoint) =>
        Path.Combine(projectRoot, RelativeFolder, checkpoint.TaskId, checkpoint.Id);

    private async Task PruneAsync(string projectRoot, string taskId, CancellationToken cancellationToken)
    {
        var folders = new List<(string Path, WorkspaceCheckpoint Checkpoint)>();
        var taskFolder = Path.Combine(projectRoot, RelativeFolder, taskId);
        foreach (var folder in _fileSystem.EnumerateDirectories(taskFolder))
        {
            var metadataPath = Path.Combine(folder, "checkpoint.json");
            if (!_fileSystem.FileExists(metadataPath))
            {
                continue;
            }

            var json = await _fileSystem.ReadAllTextAsync(metadataPath, cancellationToken).ConfigureAwait(false);
            if (JsonSerializer.Deserialize<WorkspaceCheckpoint>(json) is { } checkpoint)
            {
                folders.Add((folder, checkpoint));
            }
        }

        foreach (var stale in folders.OrderByDescending(item => item.Checkpoint.CreatedAt).Skip(_retention))
        {
            cancellationToken.ThrowIfCancellationRequested();
            _fileSystem.DeleteDirectory(stale.Path, recursive: true);
        }
    }

    private static void Validate(string projectRoot, string taskId, string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ValidateId(taskId);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
    }

    private static void ValidateId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Any(character => !char.IsLetterOrDigit(character) && character != '-'))
        {
            throw new ArgumentException("Checkpoint identifiers must contain only letters, digits, or '-'.", nameof(value));
        }
    }

    private static string Normalize(string path) => path.Replace('\\', '/');

    private static void AppendFileDiff(StringBuilder diff, string path, string? before, string? after)
    {
        if (diff.Length > 0)
        {
            diff.AppendLine();
        }

        diff.AppendLine($"--- {(before is null ? "/dev/null" : $"a/{path}")}");
        diff.AppendLine($"+++ {(after is null ? "/dev/null" : $"b/{path}")}");
        diff.AppendLine($"@@ -{(before is null ? "0,0" : "1,1")} +{(after is null ? "0,0" : "1,1")} @@");
        if (before is not null)
        {
            foreach (var line in before.Split('\n'))
            {
                diff.Append('-').AppendLine(line.TrimEnd('\r'));
            }
        }

        if (after is not null)
        {
            foreach (var line in after.Split('\n'))
            {
                diff.Append('+').AppendLine(line.TrimEnd('\r'));
            }
        }
    }

    private static StringComparer GetComparer() =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private sealed record Snapshot(IReadOnlyList<SnapshotFile> Files);
    private sealed record SnapshotFile(string RelativePath, string Contents);
}