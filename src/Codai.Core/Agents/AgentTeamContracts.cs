namespace Codai.Core.Agents;

public enum AgentTeamState
{
    Running,
    Completed,
    Failed,
    Cancelled,
}

public enum AgentTeamMemberState
{
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled,
}

public enum AgentTeamBoardItemState
{
    Pending,
    InProgress,
    Completed,
    Failed,
}

public sealed record AgentTeamMember(
    string Id,
    string Description,
    string Prompt,
    AgentTeamMemberState State,
    string? Report = null,
    string? BackgroundTaskId = null);

public sealed record AgentTeamMessage(
    string From,
    string Body,
    DateTimeOffset SentAt);

public sealed record AgentTeamBoardItem(
    string Id,
    string Text,
    AgentTeamBoardItemState State,
    string? AssignedSpecialistId = null,
    DateTimeOffset? CompletedAt = null);

public sealed record AgentTeam(
    string Id,
    string Coordinator,
    AgentTeamState State,
    IReadOnlyList<AgentTeamMember> Members,
    IReadOnlyList<AgentTeamMessage> Mailbox,
    DateTimeOffset StartedAt,
    DateTimeOffset UpdatedAt,
    string? CoordinatorPlan = null,
    IReadOnlyList<AgentTeamBoardItem>? Board = null);

public interface IAgentTeamCoordinator
{
    Task<AgentTeam> StartAsync(
        string coordinator,
        IReadOnlyList<(string Description, string Prompt)> specialists,
        string? coordinatorPlan = null,
        CancellationToken cancellationToken = default);

    Task RestoreAsync(CancellationToken cancellationToken = default);

    IReadOnlyList<AgentTeam> List();

    AgentTeamBoardItem? AddBoardItem(
        string teamId,
        string text,
        string? assignedSpecialistId = null);

    bool CompleteBoardItem(string teamId, string itemId);

    bool Send(string teamId, string from, string message);

    Task<bool> CancelAsync(string teamId, CancellationToken cancellationToken = default);

    Task ShutdownAsync(CancellationToken cancellationToken = default);
}
