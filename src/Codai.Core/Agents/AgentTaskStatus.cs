namespace Codai.Core.Agents;

/// <summary>Lifecycle state of a user-requested agent task.</summary>
public enum AgentTaskStatus
{
    Pending,
    InProgress,
    Completed,
    Failed,
    Cancelled,
}