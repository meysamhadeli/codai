using Codai.Core.Agents;
using FluentAssertions;

namespace Codai.Session.Tests;

public sealed class AgentTeamCoordinatorTests
{
    [Fact]
    public async Task Runs_bounded_specialists_and_puts_reports_in_mailbox()
    {
        var manager = new ControlledBackgroundAgentManager();
        var coordinator = new AgentTeamCoordinator(manager);

        var team = await coordinator.StartAsync("review coordinator", [
            ("auth specialist", "Inspect auth."),
            ("test specialist", "Inspect tests."),
        ]);

        manager.ReleaseAll("Findings");
        await WaitForAsync(() => coordinator.List().Single().State == AgentTeamState.Completed);

        var completed = coordinator.List().Single();
        completed.Members.Should().OnlyContain(member => member.State == AgentTeamMemberState.Completed);
        completed.Mailbox.Should().HaveCount(2);
        completed.Members.Should().OnlyContain(member => !string.IsNullOrWhiteSpace(member.BackgroundTaskId));
    }

    [Fact]
    public async Task Rejects_more_than_four_specialists_and_bounds_mailbox()
    {
        var manager = new ControlledBackgroundAgentManager();
        var coordinator = new AgentTeamCoordinator(manager);
        var specialists = Enumerable.Range(1, AgentTeamCoordinator.MaxMembers + 1)
            .Select(index => ($"specialist {index}", "Inspect."))
            .ToArray();

        await FluentActions.Awaiting(() => coordinator.StartAsync("coordinator", specialists))
            .Should().ThrowAsync<ArgumentOutOfRangeException>();

        var team = await coordinator.StartAsync("coordinator", [("specialist", "Inspect.")]);
        for (var index = 0; index < AgentTeamCoordinator.MaxMailboxMessages; index++)
        {
            coordinator.Send(team.Id, "user", $"message {index}").Should().BeTrue();
        }

        coordinator.Send(team.Id, "user", "one too many").Should().BeFalse();
        await coordinator.CancelAsync(team.Id);
    }

    [Fact]
    public async Task Cancelling_team_cancels_every_specialist_and_waits_for_shutdown()
    {
        var manager = new ControlledBackgroundAgentManager();
        var coordinator = new AgentTeamCoordinator(manager);
        var team = await coordinator.StartAsync("coordinator", [
            ("one", "Inspect."),
            ("two", "Inspect."),
        ]);

        (await coordinator.CancelAsync(team.Id)).Should().BeTrue();
        coordinator.List().Single().State.Should().Be(AgentTeamState.Cancelled);
        manager.Cancelled.Should().HaveCount(2);
    }

    [Fact]
    public async Task Persists_host_plan_and_manages_bounded_assigned_board_items()
    {
        var manager = new ControlledBackgroundAgentManager();
        var coordinator = new AgentTeamCoordinator(manager);
        var team = await coordinator.StartAsync("coordinator", [("specialist", "Inspect.")], coordinatorPlan: "Review auth first.");
        var specialistId = team.Members.Single().Id;

        var item = coordinator.AddBoardItem(team.Id, "Review authentication flow", specialistId);

        item.Should().NotBeNull();
        item!.State.Should().Be(AgentTeamBoardItemState.InProgress);
        coordinator.List().Single().CoordinatorPlan.Should().Be("Review auth first.");
        coordinator.CompleteBoardItem(team.Id, item.Id).Should().BeTrue();
        coordinator.List().Single().Board.Should().ContainSingle()
            .Which.State.Should().Be(AgentTeamBoardItemState.Completed);
        coordinator.CompleteBoardItem(team.Id, item.Id).Should().BeFalse();

        await coordinator.CancelAsync(team.Id);
    }

    [Fact]
    public async Task Rejects_oversized_board_items_and_more_than_thirty_two_items()
    {
        var manager = new ControlledBackgroundAgentManager();
        var coordinator = new AgentTeamCoordinator(manager);
        var team = await coordinator.StartAsync("coordinator", [("specialist", "Inspect.")]);

        coordinator.AddBoardItem(team.Id, new string('x', AgentTeamCoordinator.MaxBoardItemText + 1)).Should().BeNull();
        for (var index = 0; index < AgentTeamCoordinator.MaxBoardItems; index++)
        {
            coordinator.AddBoardItem(team.Id, $"item {index}").Should().NotBeNull();
        }

        coordinator.AddBoardItem(team.Id, "one too many").Should().BeNull();
        await coordinator.CancelAsync(team.Id);
    }

    [Fact]
    public async Task Marks_assigned_board_items_failed_when_specialist_fails()
    {
        var manager = new ControlledBackgroundAgentManager();
        var coordinator = new AgentTeamCoordinator(manager);
        var team = await coordinator.StartAsync("coordinator", [("specialist", "Inspect.")]);
        var item = coordinator.AddBoardItem(team.Id, "Inspect failure path", team.Members.Single().Id);

        manager.ReleaseAll("failed", BackgroundAgentState.Failed);
        await WaitForAsync(() => coordinator.List().Single().State == AgentTeamState.Failed);

        coordinator.List().Single().Board.Should().ContainSingle()
            .Which.State.Should().Be(AgentTeamBoardItemState.Failed);
        item.Should().NotBeNull();
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (condition()) return;
            await Task.Delay(10);
        }

        condition().Should().BeTrue();
    }

    private sealed class ControlledBackgroundAgentManager : IBackgroundAgentManager
    {
        private readonly Dictionary<string, TaskCompletionSource<BackgroundAgentInfo>> _tasks = [];
        private int _nextId;

        public List<string> Cancelled { get; } = [];

        public BackgroundAgentInfo Start(string description, string prompt)
        {
            var id = $"task-{++_nextId}";
            var now = DateTimeOffset.UtcNow;
            _tasks[id] = new(TaskCreationOptions.RunContinuationsAsynchronously);
            return new BackgroundAgentInfo(id, description, BackgroundAgentState.Running, null, now, now, prompt);
        }

        public Task<BackgroundAgentInfo> WaitAsync(string id, CancellationToken cancellationToken = default) =>
            _tasks[id].Task.WaitAsync(cancellationToken);

        public Task RestoreAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public IReadOnlyList<BackgroundAgentInfo> List() => [];

        public bool Cancel(string id)
        {
            Cancelled.Add(id);
            var now = DateTimeOffset.UtcNow;
            _tasks[id].TrySetResult(new BackgroundAgentInfo(id, id, BackgroundAgentState.Cancelled, "Cancelled", now, now));
            return true;
        }

        public void ReleaseAll(string report, BackgroundAgentState state = BackgroundAgentState.Completed)
        {
            foreach (var pair in _tasks)
            {
                var now = DateTimeOffset.UtcNow;
                pair.Value.TrySetResult(new BackgroundAgentInfo(pair.Key, pair.Key, state, report, now, now));
            }
        }
    }
}
