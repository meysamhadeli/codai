using Codai.Core.Abstractions;

namespace Codai.Core.Agents;

/// <summary>Non-persisting task store used by embedded and unit-test hosts.</summary>
public sealed class NullAgentTaskStore : IAgentTaskStore
{
    public static NullAgentTaskStore Instance { get; } = new();

    private NullAgentTaskStore() { }

    public Task<AgentTask?> LoadAsync(
        string rootDirectory,
        CancellationToken cancellationToken = default) => Task.FromResult<AgentTask?>(null);

    public Task<IReadOnlyList<AgentTask>> ListAsync(
        string rootDirectory,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AgentTask>>([]);

    public Task SaveAsync(
        string rootDirectory,
        AgentTask task,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task ExportAsync(
        string rootDirectory,
        string taskId,
        string path,
        CancellationToken cancellationToken = default) =>
        Task.FromException(new NotSupportedException("Task export is unavailable in this host."));

    public Task ImportAsync(
        string rootDirectory,
        string path,
        CancellationToken cancellationToken = default) =>
        Task.FromException(new NotSupportedException("Task import is unavailable in this host."));
}

public sealed class NullBackgroundAgentStore : IBackgroundAgentStore
{
    public static NullBackgroundAgentStore Instance { get; } = new();

    private NullBackgroundAgentStore() { }

    public Task<IReadOnlyList<BackgroundAgentInfo>> LoadAsync(
        string rootDirectory,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<BackgroundAgentInfo>>([]);

    public Task SaveAsync(
        string rootDirectory,
        BackgroundAgentInfo task,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public sealed class NullAgentTeamStore : IAgentTeamStore
{
    public static NullAgentTeamStore Instance { get; } = new();

    private NullAgentTeamStore() { }

    public Task<IReadOnlyList<AgentTeam>> LoadAsync(
        string rootDirectory,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AgentTeam>>([]);

    public Task SaveAsync(
        string rootDirectory,
        AgentTeam team,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}
