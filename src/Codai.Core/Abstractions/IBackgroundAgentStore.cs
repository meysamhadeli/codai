using Codai.Core.Agents;

namespace Codai.Core.Abstractions;

public interface IBackgroundAgentStore
{
    Task<IReadOnlyList<BackgroundAgentInfo>> LoadAsync(
        string rootDirectory,
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        string rootDirectory,
        BackgroundAgentInfo task,
        CancellationToken cancellationToken = default);
}