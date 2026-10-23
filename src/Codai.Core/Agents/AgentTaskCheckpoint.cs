namespace Codai.Core.Agents;

/// <summary>A durable checkpoint inside an agent task.</summary>
public sealed record AgentTaskCheckpoint(
    string Description,
    AgentTaskStatus Status,
    DateTimeOffset UpdatedAt,
    string? Error = null,
    string? PlanStepId = null);