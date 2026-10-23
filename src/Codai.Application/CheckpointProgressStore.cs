using Codai.Core.Abstractions;
using Codai.Core.Agents;

namespace Codai.Application;

public sealed class CheckpointProgressStore : IWorkspaceCheckpointStore, IWorkspaceCheckpointDiagnostics
{
    private readonly IWorkspaceCheckpointStore _inner;
    private readonly IAgentProgressSink _progress;

    public CheckpointProgressStore(IWorkspaceCheckpointStore inner, IAgentProgressSink progress)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _progress = progress ?? throw new ArgumentNullException(nameof(progress));
    }

    public async Task<WorkspaceCheckpoint> CaptureAsync(string projectRoot, string taskId, string label, CancellationToken cancellationToken = default)
    {
        await PublishAsync(AgentProgressEventKind.CheckpointCaptureStarted, taskId, label, cancellationToken).ConfigureAwait(false);
        try
        {
            var result = await _inner.CaptureAsync(projectRoot, taskId, label, cancellationToken).ConfigureAwait(false);
            await PublishAsync(AgentProgressEventKind.CheckpointCaptureCompleted, taskId, result.Id, cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (Exception ex)
        {
            await PublishFailureAsync(AgentProgressEventKind.CheckpointCaptureFailed, taskId, ex).ConfigureAwait(false);
            throw;
        }
    }

    public Task<IReadOnlyList<WorkspaceCheckpoint>> ListAsync(string projectRoot, string taskId, CancellationToken cancellationToken = default) =>
        _inner.ListAsync(projectRoot, taskId, cancellationToken);

    public Task RestoreAsync(string projectRoot, string taskId, string checkpointId, CancellationToken cancellationToken = default) =>
        RestoreAsync(projectRoot, taskId, checkpointId, WorkspaceCheckpointRestoreMode.FilesOnly, cancellationToken);

    public async Task RestoreAsync(string projectRoot, string taskId, string checkpointId, WorkspaceCheckpointRestoreMode mode, CancellationToken cancellationToken = default)
    {
        await PublishAsync(AgentProgressEventKind.CheckpointRestoreStarted, taskId, checkpointId, cancellationToken).ConfigureAwait(false);
        try
        {
            await _inner.RestoreAsync(projectRoot, taskId, checkpointId, mode, cancellationToken).ConfigureAwait(false);
            await PublishAsync(AgentProgressEventKind.CheckpointRestoreCompleted, taskId, checkpointId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await PublishFailureAsync(AgentProgressEventKind.CheckpointRestoreFailed, taskId, ex).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<string> CompareAsync(string projectRoot, string taskId, string checkpointId, CancellationToken cancellationToken = default)
    {
        await PublishAsync(AgentProgressEventKind.CheckpointCompareStarted, taskId, checkpointId, cancellationToken).ConfigureAwait(false);
        try
        {
            var result = await _inner.CompareAsync(projectRoot, taskId, checkpointId, cancellationToken).ConfigureAwait(false);
            await PublishAsync(AgentProgressEventKind.CheckpointCompareCompleted, taskId, checkpointId, cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (Exception ex)
        {
            await PublishFailureAsync(AgentProgressEventKind.CheckpointCompareFailed, taskId, ex).ConfigureAwait(false);
            throw;
        }
    }

    public Task<WorkspaceCheckpointStorageDiagnostics> GetStorageDiagnosticsAsync(string projectRoot, CancellationToken cancellationToken = default) =>
        _inner is IWorkspaceCheckpointDiagnostics diagnostics
            ? diagnostics.GetStorageDiagnosticsAsync(projectRoot, cancellationToken)
            : Task.FromResult(new WorkspaceCheckpointStorageDiagnostics(0, 0));

    private async Task PublishAsync(AgentProgressEventKind kind, string taskId, string message, CancellationToken cancellationToken)
    {
        try
        {
            await _progress.PublishAsync(new AgentProgressEvent(taskId, "checkpoint", kind, message, DateTimeOffset.UtcNow), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
    }

    private Task PublishFailureAsync(AgentProgressEventKind kind, string taskId, Exception exception) =>
        PublishAsync(kind, taskId, exception.Message, CancellationToken.None);
}