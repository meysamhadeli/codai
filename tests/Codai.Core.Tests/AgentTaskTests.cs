using Codai.Core.Agents;
using FluentAssertions;

namespace Codai.Core.Tests;

public sealed class AgentTaskTests
{
    [Fact]
    public void Lifecycle_updates_one_checkpoint_and_keeps_task_identity()
    {
        var task = AgentTask.Create("fix the failing test").Start();

        var completed = task.Complete();

        completed.Id.Should().Be(task.Id);
        completed.Request.Should().Be("fix the failing test");
        completed.Status.Should().Be(AgentTaskStatus.Completed);
        completed.Checkpoints.Should().ContainSingle()
            .Which.Status.Should().Be(AgentTaskStatus.Completed);
    }

    [Fact]
    public void Failure_and_cancellation_record_recoverable_state()
    {
        var failed = AgentTask.Create("run diagnostics").Start().Fail("tests failed");
        var cancelled = AgentTask.Create("refactor files").Start().Cancel();

        failed.Status.Should().Be(AgentTaskStatus.Failed);
        failed.Checkpoints.Single().Error.Should().Be("tests failed");
        cancelled.Status.Should().Be(AgentTaskStatus.Cancelled);
        cancelled.Checkpoints.Single().Error.Should().Be("Cancelled");
    }

    [Fact]
    public void Starting_a_checkpoint_completes_previous_phase()
    {
        var task = AgentTask.Create("make a change")
            .Start()
            .StartCheckpoint("Generate response")
            .CompleteCheckpoint()
            .StartCheckpoint("Apply changes");

        task.Checkpoints.Should().HaveCount(3);
        task.Checkpoints[0].Status.Should().Be(AgentTaskStatus.Completed);
        task.Checkpoints[1].Status.Should().Be(AgentTaskStatus.Completed);
        task.Checkpoints[2].Description.Should().Be("Apply changes");
        task.Checkpoints[2].Status.Should().Be(AgentTaskStatus.InProgress);
    }

    [Fact]
    public void Completing_a_checkpoint_keeps_task_and_plan_step_active()
    {
        var task = AgentTask.Create("make a change")
            .Start()
            .StartPlanStep("generate-response")
            .StartCheckpoint("Generate response", "generate-response");

        var updated = task.CompleteCheckpoint();

        updated.Status.Should().Be(AgentTaskStatus.InProgress);
        updated.Checkpoints[^1].Status.Should().Be(AgentTaskStatus.Completed);
        updated.Checkpoints[^1].PlanStepId.Should().Be("generate-response");
        updated.Plan[0].Status.Should().Be(AgentTaskStatus.InProgress);
        updated.Plan[1].Status.Should().Be(AgentTaskStatus.Pending);
        task.Checkpoints[^1].Status.Should().Be(AgentTaskStatus.InProgress);
    }

    [Fact]
    public void Plan_steps_are_durable_and_linked_to_execution_checkpoints()
    {
        var task = AgentTask.Create("make a change")
            .Start()
            .StartPlanStep("generate-response")
            .StartCheckpoint("Generate response", "generate-response")
            .CompleteCheckpoint()
            .CompletePlanStep("generate-response")
            .StartPlanStep("apply-changes")
            .StartCheckpoint("Apply changes", "apply-changes")
            .CompleteCheckpoint()
            .CompletePlanStep("apply-changes")
            .Complete();

        task.Plan.Select(step => step.Status).Should().Equal(
            AgentTaskStatus.Completed,
            AgentTaskStatus.Completed);
        task.Checkpoints[^2].PlanStepId.Should().Be("generate-response");
        task.Checkpoints[^1].PlanStepId.Should().Be("apply-changes");
    }
}
