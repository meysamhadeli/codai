
using Codai.Core.Projects;
namespace Codai.Core.Abstractions;

/// <summary>
/// Walks a project directory and produces the file set that is sent to the model.
/// </summary>
public interface IProjectScanner
{
    Task<ProjectContext> ScanAsync(string rootDirectory, CancellationToken cancellationToken = default);
}
