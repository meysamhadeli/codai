namespace Codai.Core.Customization;

/// <summary>
/// One project or global rule loaded from a rules directory.
/// </summary>
/// <param name="Name">File name of the rule without its extension.</param>
/// <param name="ApplyTo">
/// Globs the rule applies to. Empty means the rule always applies; a non-empty list makes
/// the rule conditional, and it is only used when a file the request references matches
/// one of them.
/// </param>
/// <param name="Body">The rule text itself.</param>
/// <param name="SourcePath">Project-relative path, or an absolute path for a global rule, with forward slashes.</param>
public sealed record ProjectRule(
    string Name,
    IReadOnlyList<string> ApplyTo,
    string Body,
    string SourcePath)
{
    /// <summary>True when the rule came from the configured global rules directory.</summary>
    public bool IsGlobal { get; init; }

    /// <summary>True when the rule only applies to matching files.</summary>
    public bool IsConditional => ApplyTo.Count > 0;
}
