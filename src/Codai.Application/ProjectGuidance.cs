using Codai.Core.Customization;
using Codai.Core.Prompting;

namespace Codai.Application;

/// <summary>
/// The project's own guidance for the assistant: the instructions file it ships and the
/// skills it makes available.
/// </summary>
/// <param name="Instructions">
/// The project instructions, or <c>null</c> when the project has none.
/// </param>
/// <param name="Skills">
/// The project's enabled skills; empty when none are enabled. Names, descriptions,
/// and instruction bodies reach every request. Resources are fetched on demand.
/// </param>
/// <remarks>
/// Both are loaded before the first model request and refreshed after skills are managed,
/// because both belong in the system prompt of every request. Grouping them keeps
/// that single loading step in one place instead of threading two values through the
/// session loop.
/// </remarks>
public sealed record ProjectGuidance(
    ProjectInstructions? Instructions,
    IReadOnlyList<SkillDefinition> Skills)
{
    /// <summary>Durable project knowledge loaded from <c>.codai/memory</c>.</summary>
    public IReadOnlyList<ProjectMemoryDocument> Memory { get; init; } = [];

    /// <summary>
    /// The project's rules. Unconditional rules reach every request; a rule with an
    /// <c>applyTo</c> glob reaches a request only when it references a matching file.
    /// </summary>
    public IReadOnlyList<ProjectRule> Rules { get; init; } = [];

    /// <summary>Guidance for a project that ships none.</summary>
    public static ProjectGuidance Empty { get; } = new(null, []);
}
