using Codai.Core.Customization;

namespace Codai.Core.Abstractions;

/// <summary>
/// The skills a project ships in <c>.codai/skills</c>.
/// </summary>
/// <remarks>
/// Only the name and description of a skill reach the prompt. The body is fetched by the
/// <c>read_skill</c> tool when the model decides a skill applies, which is what keeps
/// every skill in the project from costing context on every request.
/// </remarks>
public interface ISkillCatalog
{
    /// <summary>Loads every valid skill in <paramref name="rootDirectory"/>, sorted by name.</summary>
    Task<IReadOnlyList<SkillDefinition>> LoadAsync(
        string rootDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>Finds one skill by name, or <c>null</c> when there is none by that name.</summary>
    Task<SkillDefinition?> FindAsync(
        string rootDirectory,
        string name,
        CancellationToken cancellationToken = default);
}
