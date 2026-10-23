using Codai.Core.Abstractions;

namespace Codai.Core.Agents;

public sealed class NullWorkspaceCheckpointStore : IWorkspaceCheckpointStore
{
    public Task<WorkspaceCheckpoint> CaptureAsync(
        string projectRoot,
        string taskId,
        string label,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new WorkspaceCheckpoint(Guid.NewGuid().ToString("N"), taskId, label, DateTimeOffset.UtcNow));

    public Task<IReadOnlyList<WorkspaceCheckpoint>> ListAsync(
        string projectRoot,
        string taskId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<WorkspaceCheckpoint>>([]);

    public Task RestoreAsync(
        string projectRoot,
        string taskId,
        string checkpointId,
        CancellationToken cancellationToken = default) =>
        Task.FromException(new NotSupportedException("Workspace checkpoints are disabled."));

    public Task<string> CompareAsync(
        string projectRoot,
        string taskId,
        string checkpointId,
        CancellationToken cancellationToken = default) =>
        Task.FromException<string>(new NotSupportedException("Workspace checkpoints are disabled."));
}