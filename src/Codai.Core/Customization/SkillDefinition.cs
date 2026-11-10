namespace Codai.Core.Customization;

/// <summary>
/// A skill loaded from <c>.codai/skills/&lt;name&gt;/SKILL.md</c>.
/// </summary>
/// <param name="Name">Skill name, lower-cased, taken from its folder name.</param>
/// <param name="Description">
/// When the skill applies, shown beside its name and active instructions. Taken from
/// front matter, else the first line of the body.
/// </param>
/// <param name="Body">Instructions included in every request while the skill is enabled.</param>
/// <param name="SourcePath">Project-relative path, or absolute path for a global skill.</param>
public sealed record SkillDefinition(
    string Name,
    string Description,
    string Body,
    string SourcePath)
{
    /// <summary>True when this skill came from the configured global skill directory.</summary>
    public bool IsGlobal { get; init; }

    /// <summary>Whether this skill contributes automatic guidance; manual commands remain available when false.</summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>Whether this skill may be explicitly invoked and suggested as a slash command.</summary>
    public bool UserInvocable { get; init; } = true;

    /// <summary>Prevents automatic model guidance even when this skill is checked.</summary>
    public bool DisableModelInvocation { get; init; }

    /// <summary>Workspace-relative globs limiting automatic activation to referenced files; empty means unconditional.</summary>
    public IReadOnlyList<string> Paths { get; init; } = [];
}
