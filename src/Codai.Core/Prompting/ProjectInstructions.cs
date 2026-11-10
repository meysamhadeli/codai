namespace Codai.Core.Prompting;

/// <summary>
/// Applicable instruction guidance for the current request and its referenced directory scopes.
/// </summary>
/// <param name="SourcePath">
/// Relative path of the first source document, using forward slashes.
/// </param>
/// <param name="Content">The instructions themselves, already trimmed and capped.</param>
public sealed record ProjectInstructions(string SourcePath, string Content)
{
    /// <summary>Directory scope relative to the selected workspace; ancestors use parent segments.</summary>
    public string ScopePath { get; init; } = ".";

    /// <summary>Individual source documents making up the combined guidance.</summary>
    public IReadOnlyList<ProjectInstructions> Documents { get; init; } = [];
}
