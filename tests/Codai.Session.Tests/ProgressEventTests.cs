using Codai.Core.Abstractions;
using Codai.Core.Agents;
using FluentAssertions;

namespace Codai.Session.Tests;

public sealed class ProgressEventTests
{
    [Fact]
    public void Tool_observer_publishes_start_completion_and_denial_events()
    {
        var sink = new RecordingProgressSink();
        var observer = new InteractiveToolObserver(new Fakes.FakeUserInterface(), sink);

        observer.OnToolStarted("read_file", "read_file(path=a.cs)");
        observer.OnToolCompleted("read_file", true, "ok");
        observer.OnToolDenied("write_file", "write_file(path=a.cs)");

        sink.Events.Select(progress => progress.Kind).Should().Equal(
            AgentProgressEventKind.ToolStarted,
            AgentProgressEventKind.ToolCompleted,
            AgentProgressEventKind.ToolDenied);
    }

    [Fact]
    public async Task Checkpoint_store_publishes_start_and_completion_for_each_operation()
    {
        var sink = new RecordingProgressSink();
        var checkpoint = new WorkspaceCheckpoint("checkpoint-1", "task-1", "label", DateTimeOffset.UtcNow);
        var store = new CheckpointProgressStore(new FakeCheckpointStore(checkpoint), sink);

        await store.CaptureAsync("project", "task-1", "label");
        await store.CompareAsync("project", "task-1", checkpoint.Id);
        await store.RestoreAsync("project", "task-1", checkpoint.Id);

        sink.Events.Select(progress => progress.Kind).Should().Equal(
            AgentProgressEventKind.CheckpointCaptureStarted,
            AgentProgressEventKind.CheckpointCaptureCompleted,
            AgentProgressEventKind.CheckpointCompareStarted,
            AgentProgressEventKind.CheckpointCompareCompleted,
            AgentProgressEventKind.CheckpointRestoreStarted,
            AgentProgressEventKind.CheckpointRestoreCompleted);
    }

    private sealed class RecordingProgressSink : IAgentProgressSink
    {
        public List<AgentProgressEvent> Events { get; } = [];

        public ValueTask PublishAsync(
            AgentProgressEvent progress,
            CancellationToken cancellationToken = default)
        {
            Events.Add(progress);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeCheckpointStore(WorkspaceCheckpoint checkpoint) : IWorkspaceCheckpointStore
    {
        public Task<WorkspaceCheckpoint> CaptureAsync(string projectRoot, string taskId, string label, CancellationToken cancellationToken = default) =>
            Task.FromResult(checkpoint);

        public Task<IReadOnlyList<WorkspaceCheckpoint>> ListAsync(string projectRoot, string taskId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkspaceCheckpoint>>([checkpoint]);

        public Task RestoreAsync(string projectRoot, string taskId, string checkpointId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<string> CompareAsync(string projectRoot, string taskId, string checkpointId, CancellationToken cancellationToken = default) =>
            Task.FromResult(string.Empty);
    }
}