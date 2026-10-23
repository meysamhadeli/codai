namespace Codai.Core.Customization;

/// <summary>
/// A skill loaded from <c>.codai/skills/&lt;name&gt;/SKILL.md</c>.
/// </summary>
/// <param name="Name">Skill name, lower-cased, taken from its folder name.</param>
/// <param name="Description">
/// When the skill applies. This is the only part shown up front, so it is what the model
/// decides on; it is taken from front matter, else the first line of the body.
/// </param>
/// <param name="Body">The instructions themselves, fetched on demand.</param>
/// <param name="SourcePath">Project-relative path, or absolute path for a global skill.</param>
public sealed record SkillDefinition(
    string Name,
    string Description,
    string Body,
    string SourcePath)
{
    /// <summary>True when this skill came from the configured global skill directory.</summary>
    public bool IsGlobal { get; init; }
}
