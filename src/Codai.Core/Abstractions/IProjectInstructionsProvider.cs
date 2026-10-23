using Codai.Core.Prompting;

namespace Codai.Core.Abstractions;

/// <summary>
/// Finds the project-level instruction file — the <c>AGENTS.md</c> / <c>CLAUDE.md</c>
/// convention — and returns its contents for the system prompt.
/// </summary>
public interface IProjectInstructionsProvider
{
    /// <summary>
    /// Loads the first existing instruction file under
    /// <paramref name="rootDirectory"/>.
    /// </summary>
    /// <returns>The instructions, or <c>null</c> when the project has none.</returns>
    Task<ProjectInstructions?> LoadAsync(
        string rootDirectory,
        CancellationToken cancellationToken = default);
}
