namespace Codai.Core.Agents;

/// <summary>A durable execution step in an agent task plan.</summary>
public sealed record AgentTaskPlanStep(
    string Id,
    string Description,
    AgentTaskStatus Status,
    DateTimeOffset UpdatedAt,
    string? Error = null);