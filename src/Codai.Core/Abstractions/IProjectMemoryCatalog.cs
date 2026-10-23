using Codai.Core.Customization;

namespace Codai.Core.Abstractions;

/// <summary>Loads durable project knowledge for the session prompt.</summary>
public interface IProjectMemoryCatalog
{
    Task<IReadOnlyList<ProjectMemoryDocument>> LoadAsync(
        string rootDirectory,
        CancellationToken cancellationToken = default);
}
