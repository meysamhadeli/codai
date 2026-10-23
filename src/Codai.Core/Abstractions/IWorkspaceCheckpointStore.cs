using Codai.Core.Agents;

namespace Codai.Core.Abstractions;

public interface IWorkspaceCheckpointStore
{
    Task<WorkspaceCheckpoint> CaptureAsync(
        string projectRoot,
        string taskId,
        string label,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkspaceCheckpoint>> ListAsync(
        string projectRoot,
        string taskId,
        CancellationToken cancellationToken = default);

    Task RestoreAsync(
        string projectRoot,
        string taskId,
        string checkpointId,
        CancellationToken cancellationToken = default);

    async Task RestoreAsync(
        string projectRoot,
        string taskId,
        string checkpointId,
        WorkspaceCheckpointRestoreMode mode,
        CancellationToken cancellationToken = default)
    {
        if (mode != WorkspaceCheckpointRestoreMode.FilesOnly)
        {
            throw new NotSupportedException("This checkpoint store does not persist task metadata.");
        }

        await RestoreAsync(projectRoot, taskId, checkpointId, cancellationToken).ConfigureAwait(false);
    }

    Task<string> CompareAsync(
        string projectRoot,
        string taskId,
        string checkpointId,
        CancellationToken cancellationToken = default);
}