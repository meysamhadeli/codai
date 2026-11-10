using Codai.Core.Prompting;

namespace Codai.Core.Abstractions;

/// <summary>
/// Finds applicable standard instruction documents and returns combined, directory-scoped model guidance.
/// </summary>
public interface IProjectInstructionsProvider
{
    /// <summary>
    /// Loads applicable standard instruction files for
    /// <paramref name="rootDirectory"/>.
    /// </summary>
    /// <returns>The instructions, or <c>null</c> when the project has none.</returns>
    Task<ProjectInstructions?> LoadAsync(
        string rootDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>Loads broad guidance plus nested guidance for referenced workspace files on the current request.</summary>
    /// <remarks>The default implementation preserves hosts that implement only broad instruction loading.</remarks>
    Task<ProjectInstructions?> LoadForFilesAsync(string rootDirectory, IReadOnlyList<string> files,
        CancellationToken cancellationToken = default) => LoadAsync(rootDirectory, cancellationToken);
}
