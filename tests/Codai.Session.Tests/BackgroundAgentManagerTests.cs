using Codai.Core.Abstractions;
using Codai.Core.Agents;
using Codai.Session.Tests.Fakes;
using FluentAssertions;

namespace Codai.Session.Tests;

public sealed class BackgroundAgentManagerTests
{
    private const string Root = @"C:\project";

    [Fact]
    public async Task Keeps_completed_report_in_task_list()
    {
        var subAgent = new ControlledSubAgent();
        var manager = new BackgroundAgentManager(subAgent);
        var task = manager.Start("Inspect auth", "Find token validation.");

        task.State.Should().Be(BackgroundAgentState.Running);
        subAgent.Release(SubAgentResult.Success("Found in AuthService.cs."));

        await WaitForAsync(() => manager.List().Single().State == BackgroundAgentState.Completed);

        var stored = manager.List().Should().ContainSingle().Subject;
        stored.State.Should().Be(BackgroundAgentState.Completed);
        stored.Report.Should().Be("Found in AuthService.cs.");
    }

    [Fact]
    public async Task Cancellation_marks_running_task_cancelled()
    {
        var subAgent = new ControlledSubAgent();
        var manager = new BackgroundAgentManager(subAgent);
        var task = manager.Start("Inspect auth", "Find token validation.");

        manager.Cancel(task.Id).Should().BeTrue();
        await Assert.ThrowsAsync<TaskCanceledException>(() => subAgent.Completed);
        await WaitForAsync(() => manager.List().Single().State == BackgroundAgentState.Cancelled);

        manager.List().Single().State.Should().Be(BackgroundAgentState.Cancelled);
        manager.Cancel(task.Id).Should().BeFalse();
    }

    [Fact]
    public async Task Restore_restarts_an_interrupted_task()
    {
        var fileSystem = new InMemoryFileSystem(Root);
        var store = new FileBackgroundAgentStore(fileSystem);
        var interrupted = new BackgroundAgentInfo(
            "task-1",
            "Inspect auth",
            BackgroundAgentState.Running,
            null,
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow,
            "Find token validation.");
        await store.SaveAsync(Root, interrupted);

        var subAgent = new ControlledSubAgent();
        var manager = new BackgroundAgentManager(subAgent, store, projectRoot: Root);
        await manager.RestoreAsync();
        subAgent.Release(SubAgentResult.Success("Resumed."));

        await WaitForAsync(() => manager.List().Single().State == BackgroundAgentState.Completed);
        manager.List().Single().Report.Should().Be("Resumed.");
    }

    [Fact]
    public async Task Restore_does_not_duplicate_a_task_with_an_active_lease()
    {
        var fileSystem = new InMemoryFileSystem(Root);
        var store = new FileBackgroundAgentStore(fileSystem);
        var active = new BackgroundAgentInfo(
            "task-1",
            "Inspect auth",
            BackgroundAgentState.Running,
            null,
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow,
            "Find token validation.",
            "existing-run",
            DateTimeOffset.UtcNow.AddMinutes(1));
        await store.SaveAsync(Root, active);

        var subAgent = new ControlledSubAgent();
        var manager = new BackgroundAgentManager(subAgent, store, projectRoot: Root);
        await manager.RestoreAsync();

        manager.List().Should().ContainSingle().Which.State.Should().Be(BackgroundAgentState.Running);
        subAgent.Completed.IsCompletedSuccessfully.Should().BeTrue();
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        condition().Should().BeTrue();
    }

    private sealed class ControlledSubAgent : ISubAgent
    {
        private readonly TaskCompletionSource<SubAgentResult> _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<SubAgentResult> Completed { get; private set; } =
            Task.FromResult(SubAgentResult.Failure("Not started."));

        public Task<SubAgentResult> RunAsync(
            SubAgentRequest request,
            CancellationToken cancellationToken = default)
        {
            Completed = WaitAsync(cancellationToken);
            return Completed;
        }

        public void Release(SubAgentResult result) => _release.TrySetResult(result);

        private async Task<SubAgentResult> WaitAsync(CancellationToken cancellationToken) =>
            await _release.Task.WaitAsync(cancellationToken);
    }
}