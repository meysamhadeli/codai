using Codai.Core.Agents;

namespace Codai.Core.Abstractions;

/// <summary>Persists the latest agent task so interrupted work can be resumed.</summary>
public interface IAgentTaskStore
{
    Task<AgentTask?> LoadAsync(
        string rootDirectory,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AgentTask>> ListAsync(
        string rootDirectory,
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        string rootDirectory,
        AgentTask task,
        CancellationToken cancellationToken = default);

    Task ExportAsync(
        string rootDirectory,
        string taskId,
        string path,
        CancellationToken cancellationToken = default);

    Task ImportAsync(
        string rootDirectory,
        string path,
        CancellationToken cancellationToken = default);
}