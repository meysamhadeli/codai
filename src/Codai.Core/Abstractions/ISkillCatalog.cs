using Codai.Core.Customization;

namespace Codai.Core.Abstractions;

/// <summary>
/// The skills a project ships in <c>.codai/skills</c>.
/// </summary>
/// <remarks>
/// Enabled skills contribute automatic request guidance. The full catalog includes
/// unchecked skills for management and explicit slash invocation independently of
/// automatic selection. Enabled-only lookup remains available for model skill tools.
/// </remarks>
public interface ISkillCatalog
{
    /// <summary>Loads active skills, including nested scopes revealed by workspace-relative referenced files.</summary>
    Task<IReadOnlyList<SkillDefinition>> LoadForFilesAsync(string rootDirectory, IReadOnlyList<string> referencedFiles,
        CancellationToken cancellationToken = default) => LoadAsync(rootDirectory, cancellationToken);

    /// <summary>Loads enabled valid skills in <paramref name="rootDirectory"/>, sorted by name.</summary>
    Task<IReadOnlyList<SkillDefinition>> LoadAsync(
        string rootDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>Loads available and disabled skills for management, including their enabled state.</summary>
    Task<IReadOnlyList<SkillDefinition>> LoadAllAsync(string rootDirectory, CancellationToken cancellationToken = default) =>
        LoadAsync(rootDirectory, cancellationToken);

    /// <summary>Atomically saves which discovered skills are enabled for this project.</summary>
    Task SaveEnabledAsync(string rootDirectory, IReadOnlyList<string> enabledNames, CancellationToken cancellationToken = default) =>
        Task.FromException(new NotSupportedException("This skill catalog does not support editing skill availability."));

    /// <summary>Finds one skill by name, or <c>null</c> when there is none by that name.</summary>
    Task<SkillDefinition?> FindAsync(
        string rootDirectory,
        string name,
        CancellationToken cancellationToken = default);
}
