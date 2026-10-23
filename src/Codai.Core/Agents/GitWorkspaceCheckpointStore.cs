using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Codai.Core.Abstractions;
using Codai.Core.Chat;

namespace Codai.Core.Agents;

public sealed class GitWorkspaceCheckpointStore : IWorkspaceCheckpointStore, IWorkspaceCheckpointDiagnostics
{
    public const string RelativeFolder = ".codai/checkpoints";
    private const string RepositoryFolder = "git";
    private const string MetadataFolder = "metadata";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> RepositoryGates = new(StringComparer.OrdinalIgnoreCase);

    private readonly IFileSystem _fileSystem;
    private readonly IProcessRunner _processes;
    private readonly int _retention;

    public GitWorkspaceCheckpointStore(IFileSystem fileSystem, IProcessRunner processes, int retention = 10)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _processes = processes ?? throw new ArgumentNullException(nameof(processes));
        _retention = retention >= 1 ? retention : throw new ArgumentOutOfRangeException(nameof(retention));
    }

    public async Task<WorkspaceCheckpoint> CaptureAsync(
        string projectRoot,
        string taskId,
        string label,
        CancellationToken cancellationToken = default)
    {
        Validate(projectRoot, taskId, label);
        var gate = RepositoryGates.GetOrAdd(Path.GetFullPath(projectRoot), _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
        var checkpoint = new WorkspaceCheckpoint(Guid.NewGuid().ToString("N"), taskId, label, DateTimeOffset.UtcNow);
        var repository = GetRepositoryPath(projectRoot, taskId);
        _fileSystem.CreateDirectory(Path.Combine(projectRoot, RelativeFolder, MetadataFolder, taskId));
        await EnsureRepositoryAsync(projectRoot, repository, cancellationToken).ConfigureAwait(false);

        var excludeDirectory = Path.Combine(repository, "info");
        _fileSystem.CreateDirectory(excludeDirectory);
        await _fileSystem.WriteAllTextAtomicallyAsync(Path.Combine(excludeDirectory, "exclude"), "/.codai/\n", cancellationToken)
            .ConfigureAwait(false);
        await RunGitAsync(projectRoot, repository, "add -A", cancellationToken).ConfigureAwait(false);
        var commit = await RunGitAsync(
            projectRoot,
            repository,
            $"-c user.name=Codai -c user.email=codai@localhost commit --allow-empty -m {Quote(checkpoint.Label)}",
            cancellationToken).ConfigureAwait(false);
        var revision = await RunGitAsync(projectRoot, repository, "rev-parse HEAD", cancellationToken).ConfigureAwait(false);
        var stored = checkpoint with { Revision = revision.StandardOutput.Trim() };
        var metadataPath = GetMetadataPath(projectRoot, stored);
        await _fileSystem.WriteAllTextAtomicallyAsync(
            metadataPath,
            JsonSerializer.Serialize(stored, JsonOptions),
            cancellationToken).ConfigureAwait(false);
        var taskPath = FileAgentTaskStore.GetTaskPath(projectRoot, taskId);
        if (_fileSystem.FileExists(taskPath))
        {
            var task = await _fileSystem.ReadAllTextAsync(taskPath, cancellationToken).ConfigureAwait(false);
            await _fileSystem.WriteAllTextAtomicallyAsync(
                Path.Combine(Path.GetDirectoryName(metadataPath)!, $"{stored.Id}.task.json"), task, cancellationToken)
                .ConfigureAwait(false);
        }

        var sessionPath = FileSessionStore.GetTaskPath(projectRoot, taskId);
        if (_fileSystem.FileExists(sessionPath))
        {
            var session = await _fileSystem.ReadAllTextAsync(sessionPath, cancellationToken).ConfigureAwait(false);
            await _fileSystem.WriteAllTextAtomicallyAsync(
                Path.Combine(Path.GetDirectoryName(metadataPath)!, $"{stored.Id}.session.json"), session, cancellationToken)
                .ConfigureAwait(false);
        }
            await PruneMetadataAsync(projectRoot, taskId, cancellationToken).ConfigureAwait(false);
        return stored;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<WorkspaceCheckpoint>> ListAsync(
        string projectRoot,
        string taskId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ValidateId(taskId);
        var folder = Path.Combine(projectRoot, RelativeFolder, MetadataFolder, taskId);
        if (!_fileSystem.DirectoryExists(folder))
        {
            return [];
        }

        var result = new List<WorkspaceCheckpoint>();
        foreach (var path in _fileSystem.EnumerateFiles(folder))
        {
            if (!string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var json = await _fileSystem.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
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
        var gate = RepositoryGates.GetOrAdd(Path.GetFullPath(projectRoot), _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
        var metadataPath = Path.Combine(projectRoot, RelativeFolder, MetadataFolder, taskId, $"{checkpointId}.json");
        if (!_fileSystem.FileExists(metadataPath))
        {
            throw new FileNotFoundException("Checkpoint was not found.", checkpointId);
        }

        var checkpoint = JsonSerializer.Deserialize<WorkspaceCheckpoint>(
            await _fileSystem.ReadAllTextAsync(metadataPath, cancellationToken).ConfigureAwait(false))
            ?? throw new IOException("Checkpoint metadata is invalid.");
        var repository = GetRepositoryPath(projectRoot, taskId);
        await RunGitAsync(projectRoot, repository, $"reset --hard {Quote(checkpoint.Revision)}", cancellationToken)
            .ConfigureAwait(false);
        await RunGitAsync(projectRoot, repository, "clean -fdx -e .codai/", cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
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
            var taskPath = Path.Combine(projectRoot, RelativeFolder, MetadataFolder, taskId, $"{checkpointId}.task.json");
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

            var sessionPath = Path.Combine(projectRoot, RelativeFolder, MetadataFolder, taskId, $"{checkpointId}.session.json");
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
        var metadataPath = Path.Combine(projectRoot, RelativeFolder, MetadataFolder, taskId, $"{checkpointId}.json");
        if (!_fileSystem.FileExists(metadataPath))
        {
            throw new FileNotFoundException("Checkpoint was not found.", checkpointId);
        }

        var checkpoint = JsonSerializer.Deserialize<WorkspaceCheckpoint>(
            await _fileSystem.ReadAllTextAsync(metadataPath, cancellationToken).ConfigureAwait(false))
            ?? throw new IOException("Checkpoint metadata is invalid.");
        var repository = GetRepositoryPath(projectRoot, taskId);
        var result = await RunGitAsync(
            projectRoot,
            repository,
            $"diff --no-ext-diff --unified=3 {Quote(checkpoint.Revision)} --",
            cancellationToken).ConfigureAwait(false);
        var diff = new StringBuilder(result.StandardOutput.TrimEnd());
        var untracked = await RunGitAsync(
            projectRoot,
            repository,
            "ls-files --others --exclude-standard",
            cancellationToken).ConfigureAwait(false);

        foreach (var relativePath in untracked.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var normalizedPath = relativePath.Trim().Replace('\\', '/');
            var path = Path.Combine(projectRoot, normalizedPath.Replace('/', Path.DirectorySeparatorChar));
            var contents = await _fileSystem.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            if (diff.Length > 0)
            {
                diff.AppendLine();
            }

            diff.AppendLine($"diff --git a/{normalizedPath} b/{normalizedPath}");
            diff.AppendLine("new file mode 100644");
            diff.AppendLine("--- /dev/null");
            diff.AppendLine($"+++ b/{normalizedPath}");
            diff.AppendLine("@@ -0,0 +1,1 @@");
            foreach (var line in contents.Split('\n'))
            {
                diff.Append('+').AppendLine(line.TrimEnd('\r'));
            }
        }

        return diff.ToString().Trim();
    }

    public async Task<WorkspaceCheckpointStorageDiagnostics> GetStorageDiagnosticsAsync(
        string projectRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        var metadataRoot = Path.Combine(projectRoot, RelativeFolder, MetadataFolder);
        var checkpointCount = 0;
        long retainedBytes = 0;
        if (_fileSystem.DirectoryExists(metadataRoot))
        {
            foreach (var file in EnumerateFilesRecursively(metadataRoot))
            {
                cancellationToken.ThrowIfCancellationRequested();
                retainedBytes += _fileSystem.GetFileSize(file);
                if (Path.GetExtension(file).Equals(".json", StringComparison.OrdinalIgnoreCase) &&
                    !file.EndsWith(".task.json", StringComparison.OrdinalIgnoreCase))
                {
                    checkpointCount++;
                }
            }
        }

        var repositoryRoot = GetRepositoryRoot(projectRoot);
        if (_fileSystem.DirectoryExists(repositoryRoot))
        {
            foreach (var taskDirectory in _fileSystem.EnumerateDirectories(repositoryRoot))
            {
                var repository = Path.Combine(taskDirectory, RepositoryFolder);
                if (!_fileSystem.DirectoryExists(repository))
                {
                    continue;
                }

                foreach (var file in EnumerateFilesRecursively(repository))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    retainedBytes += _fileSystem.GetFileSize(file);
                }
            }
        }

        return new WorkspaceCheckpointStorageDiagnostics(checkpointCount, retainedBytes);
    }

    private async Task EnsureRepositoryAsync(string projectRoot, string repository, CancellationToken cancellationToken)
    {
        var lockPath = Path.Combine(repository, "index.lock");
        if (_fileSystem.FileExists(lockPath))
        {
            _fileSystem.DeleteFile(lockPath);
        }

        if (_fileSystem.DirectoryExists(Path.Combine(repository, "objects")))
        {
            return;
        }

        _fileSystem.CreateDirectory(repository);
        var result = await _processes.RunAsync(
            $"git init --bare {Quote(repository)}",
            projectRoot,
            TimeSpan.FromSeconds(30),
            cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new IOException($"Git checkpoint repository could not be initialized: {result.StandardError.Trim()}");
        }
    }

    private async Task<ProcessResult> RunGitAsync(
        string projectRoot,
        string repository,
        string arguments,
        CancellationToken cancellationToken,
        bool allowFailure = false)
    {
        var command = $"git --git-dir={Quote(repository)} --work-tree={Quote(projectRoot)} {arguments}";
        var result = await _processes.RunAsync(command, projectRoot, TimeSpan.FromSeconds(30), cancellationToken)
            .ConfigureAwait(false);
        if (!result.Succeeded && !allowFailure)
        {
            var detail = result.StandardError.Trim();
            if (detail.Length > 800)
            {
                detail = detail[..800].TrimEnd() + "...";
            }
            throw new IOException($"Git checkpoint command failed: {detail}");
        }

        return result;
    }

    private static string GetRepositoryPath(string projectRoot, string taskId) =>
        Path.Combine(
            GetRepositoryRoot(projectRoot),
            taskId,
            RepositoryFolder);

    private static string GetRepositoryRoot(string projectRoot) => Path.Combine(
        Path.GetTempPath(),
        "Codai",
        "checkpoints",
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(projectRoot)))));

    private static string GetMetadataPath(string projectRoot, WorkspaceCheckpoint checkpoint) =>
        Path.Combine(projectRoot, RelativeFolder, MetadataFolder, checkpoint.TaskId, $"{checkpoint.Id}.json");

    private async Task PruneMetadataAsync(string projectRoot, string taskId, CancellationToken cancellationToken)
    {
        var folder = Path.Combine(projectRoot, RelativeFolder, MetadataFolder, taskId);
        var metadata = new List<(string Path, WorkspaceCheckpoint Checkpoint)>();
        foreach (var path in _fileSystem.EnumerateFiles(folder))
        {
            if (Path.GetExtension(path) != ".json" || path.EndsWith(".task.json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var json = await _fileSystem.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            if (JsonSerializer.Deserialize<WorkspaceCheckpoint>(json) is { } checkpoint)
            {
                metadata.Add((path, checkpoint));
            }
        }

        foreach (var stale in metadata.OrderByDescending(item => item.Checkpoint.CreatedAt).Skip(_retention))
        {
            cancellationToken.ThrowIfCancellationRequested();
            _fileSystem.DeleteFile(stale.Path);
            var taskPath = Path.Combine(folder, $"{stale.Checkpoint.Id}.task.json");
            if (_fileSystem.FileExists(taskPath))
            {
                _fileSystem.DeleteFile(taskPath);
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

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

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
}