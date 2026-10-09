namespace Codai.Core.Customization;

/// <summary>
/// A skill loaded from <c>.codai/skills/&lt;name&gt;/SKILL.md</c>.
/// </summary>
/// <param name="Name">Skill name, lower-cased. Taken from front matter, else the folder name.</param>
/// <param name="Description">
/// When the skill applies. This is the only part shown up front, so it is what the model
/// decides on; it is taken from front matter, else the first line of the body.
/// </param>
/// <param name="Body">The instructions themselves, fetched on demand.</param>
/// <param name="SourcePath">Relative path of the file it came from.</param>
public sealed record SkillDefinition(
    string Name,
    string Description,
    string Body,
    string SourcePath);
