namespace Codai.Core.Agents;

public sealed record WorkspaceCheckpoint(
    string Id,
    string TaskId,
    string Label,
    DateTimeOffset CreatedAt)
{
    public string Revision { get; init; } = string.Empty;
}

public enum WorkspaceCheckpointRestoreMode
{
    FilesOnly,
    TaskOnly,
    FilesAndTask,
}