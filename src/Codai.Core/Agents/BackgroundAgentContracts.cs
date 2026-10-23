namespace Codai.Core.Agents;

public enum BackgroundAgentState
{
    Running,
    Completed,
    Failed,
    Cancelled,
}

public sealed record BackgroundAgentInfo(
    string Id,
    string Description,
    BackgroundAgentState State,
    string? Report,
    DateTimeOffset StartedAt,
    DateTimeOffset UpdatedAt,
    string? Prompt = null,
    string? LeaseId = null,
    DateTimeOffset? LeaseExpiresAt = null);

public enum AgentProgressEventKind
{
    Started,
    ModelStarted,
    ModelDelta,
    ModelCompleted,
    ToolStarted,
    ToolCompleted,
    ToolDenied,
    Completed,
    Failed,
    Cancelled,
    HookStarted,
    HookCompleted,
    HookFailed,
    CommandStandardOutput,
    CommandStandardError,
    CheckpointCaptureStarted,
    CheckpointCaptureCompleted,
    CheckpointCaptureFailed,
    CheckpointCompareStarted,
    CheckpointCompareCompleted,
    CheckpointCompareFailed,
    CheckpointRestoreStarted,
    CheckpointRestoreCompleted,
    CheckpointRestoreFailed,
    TaskResumeStarted,
    TaskResumeCompleted,
    TaskResumeFailed,
}

public sealed record AgentProgressEvent(
    string TaskId,
    string Description,
    AgentProgressEventKind Kind,
    string? Message,
    DateTimeOffset OccurredAt);

public interface IAgentProgressSink
{
    ValueTask PublishAsync(AgentProgressEvent progress, CancellationToken cancellationToken = default);
}

public sealed class NullAgentProgressSink : IAgentProgressSink
{
    public static NullAgentProgressSink Instance { get; } = new();

    private NullAgentProgressSink() { }

    public ValueTask PublishAsync(
        AgentProgressEvent progress,
        CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
}

public interface IBackgroundAgentManager
{
    BackgroundAgentInfo Start(string description, string prompt);

    Task<BackgroundAgentInfo> WaitAsync(string id, CancellationToken cancellationToken = default);

    Task RestoreAsync(CancellationToken cancellationToken = default);

    IReadOnlyList<BackgroundAgentInfo> List();

    bool Cancel(string id);
}
