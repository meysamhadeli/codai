using Codai.Core.Agents;
using Codai.Core.Abstractions;

namespace Codai.Application.Interaction;

public sealed class InteractiveAgentProgressSink : IAgentProgressSink
{
    private readonly IUserInterface _ui;

    public InteractiveAgentProgressSink(IUserInterface ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        _ui = ui;
    }

    public ValueTask PublishAsync(
        AgentProgressEvent progress,
        CancellationToken cancellationToken = default)
    {
        switch (progress.Kind)
        {
            case AgentProgressEventKind.Started:
                _ui.WriteInfo($"Started background task {progress.TaskId}: {progress.Description}");
                break;
            case AgentProgressEventKind.Completed:
                _ui.WriteSuccess($"Background task {progress.TaskId} completed.");
                break;
            case AgentProgressEventKind.Failed:
                _ui.WriteError($"Background task {progress.TaskId} failed: {progress.Message}");
                break;
            case AgentProgressEventKind.Cancelled:
                _ui.WriteInfo($"Background task {progress.TaskId} cancelled.");
                break;
            case AgentProgressEventKind.CheckpointCaptureFailed:
                _ui.WriteError($"Checkpoint capture failed for task {progress.TaskId}: {progress.Message}");
                break;
            case AgentProgressEventKind.CheckpointCompareCompleted:
                _ui.WriteInfo($"Compared checkpoint {progress.Message} for task {progress.TaskId}.");
                break;
            case AgentProgressEventKind.CheckpointCompareFailed:
                _ui.WriteError($"Checkpoint comparison failed for task {progress.TaskId}: {progress.Message}");
                break;
            case AgentProgressEventKind.CheckpointRestoreCompleted:
                _ui.WriteSuccess($"Restored checkpoint {progress.Message} for task {progress.TaskId}.");
                break;
            case AgentProgressEventKind.CheckpointRestoreFailed:
                _ui.WriteError($"Checkpoint restore failed for task {progress.TaskId}: {progress.Message}");
                break;
        }

        return ValueTask.CompletedTask;
    }
}