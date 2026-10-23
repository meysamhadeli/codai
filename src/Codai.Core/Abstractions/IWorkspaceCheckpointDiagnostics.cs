namespace Codai.Core.Abstractions;

public sealed record WorkspaceCheckpointStorageDiagnostics(
    int CheckpointCount,
    long RetainedBytes);

public interface IWorkspaceCheckpointDiagnostics
{
    Task<WorkspaceCheckpointStorageDiagnostics> GetStorageDiagnosticsAsync(
        string projectRoot,
        CancellationToken cancellationToken = default);
}