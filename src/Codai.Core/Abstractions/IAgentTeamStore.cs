using Codai.Core.Agents;

namespace Codai.Core.Abstractions;

public interface IAgentTeamStore
{
    Task<IReadOnlyList<AgentTeam>> LoadAsync(
        string rootDirectory,
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        string rootDirectory,
        AgentTeam team,
        CancellationToken cancellationToken = default);
}
