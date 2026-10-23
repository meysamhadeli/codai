namespace Codai.Core.Agents;

/// <summary>
/// Durable state for one request being handled by the agent runtime.
/// </summary>
public sealed record AgentTask(
    string Id,
    string Request,
    AgentTaskStatus Status,
    IReadOnlyList<AgentTaskCheckpoint> Checkpoints,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    /// <summary>Ordered execution plan for this task.</summary>
    public IReadOnlyList<AgentTaskPlanStep> Plan { get; init; } = [];

    public AgentMode Mode { get; init; } = AgentMode.Agent;

    public string? ProposedPlan { get; init; }

    public string? SourcePlanId { get; init; }

    /// <summary>Creates a new task with one initial checkpoint.</summary>
    public static AgentTask Create(string request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request);

        var now = DateTimeOffset.UtcNow;
        return new AgentTask(
            Guid.NewGuid().ToString("N"),
            request,
            AgentTaskStatus.Pending,
            [new AgentTaskCheckpoint("Handle request", AgentTaskStatus.Pending, now)],
            now,
            now)
        {
            Plan =
            [
                new AgentTaskPlanStep("generate-response", "Generate response", AgentTaskStatus.Pending, now),
                new AgentTaskPlanStep("apply-changes", "Apply changes", AgentTaskStatus.Pending, now),
            ],
        };
    }

    /// <summary>Moves the task into active execution.</summary>
    public AgentTask Start() => Update(AgentTaskStatus.InProgress, null);

    /// <summary>Adds a pending execution checkpoint to the task.</summary>
    public AgentTask StartPlanStep(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var now = DateTimeOffset.UtcNow;
        var steps = Plan.Select(step => step.Id.Equals(id, StringComparison.OrdinalIgnoreCase)
            ? step with { Status = AgentTaskStatus.InProgress, UpdatedAt = now, Error = null }
            : step).ToArray();

        if (steps.Length == 0 || !steps.Any(step => step.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Plan step '{id}' does not exist.");
        }

        return this with { Plan = steps, Status = AgentTaskStatus.InProgress, UpdatedAt = now };
    }

    /// <summary>Marks a plan step complete.</summary>
    public AgentTask CompletePlanStep(string id) => UpdatePlanStep(id, AgentTaskStatus.Completed, null);

    /// <summary>Starts a checkpoint associated with a plan step.</summary>
    public AgentTask StartCheckpoint(string description, string? planStepId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        var now = DateTimeOffset.UtcNow;
        var checkpoints = Checkpoints.Count == 0
            ? []
            : Checkpoints.SkipLast(1).Append(Checkpoints[^1] with
            {
                Status = AgentTaskStatus.Completed,
                UpdatedAt = now,
            });

        return this with
        {
            Status = AgentTaskStatus.InProgress,
            Checkpoints = [.. checkpoints, new AgentTaskCheckpoint(description, AgentTaskStatus.InProgress, now, PlanStepId: planStepId)],
            UpdatedAt = now,
        };
    }

    /// <summary>Marks the active checkpoint complete without completing the task.</summary>
    public AgentTask CompleteCheckpoint()
    {
        var now = DateTimeOffset.UtcNow;
        var checkpoint = Checkpoints[^1] with
        {
            Status = AgentTaskStatus.Completed,
            UpdatedAt = now,
            Error = null,
        };
        return this with
        {
            Checkpoints = [.. Checkpoints.SkipLast(1), checkpoint],
            UpdatedAt = now,
        };
    }

    /// <summary>Marks the task as successfully completed.</summary>
    public AgentTask Complete() => Update(AgentTaskStatus.Completed, null);

    /// <summary>Marks the task as failed and records the reason.</summary>
    public AgentTask Fail(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return Update(AgentTaskStatus.Failed, error);
    }

    /// <summary>Marks the task as cancelled.</summary>
    public AgentTask Cancel() => Update(AgentTaskStatus.Cancelled, "Cancelled");

    private AgentTask Update(AgentTaskStatus status, string? error)
    {
        var now = DateTimeOffset.UtcNow;
        var checkpoint = Checkpoints[^1] with { Status = status, UpdatedAt = now, Error = error };
        var plan = Plan
            .Select(step => step.Status == AgentTaskStatus.InProgress
                ? step with { Status = status, UpdatedAt = now, Error = error }
                : step)
            .ToArray();
        return this with
        {
            Status = status,
            Checkpoints = [.. Checkpoints.SkipLast(1), checkpoint],
            Plan = plan,
            UpdatedAt = now,
        };
    }

    private AgentTask UpdatePlanStep(string id, AgentTaskStatus status, string? error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var now = DateTimeOffset.UtcNow;
        var found = false;
        var plan = Plan.Select(step =>
        {
            if (!step.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
            {
                return step;
            }

            found = true;
            return step with { Status = status, UpdatedAt = now, Error = error };
        }).ToArray();

        if (!found)
        {
            throw new InvalidOperationException($"Plan step '{id}' does not exist.");
        }

        return this with { Plan = plan, UpdatedAt = now };
    }
}
