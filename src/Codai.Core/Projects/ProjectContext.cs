namespace Codai.Core.Projects;

/// <summary>
/// The result of scanning a project: the raw files plus the pre-rendered
/// signature blocks that are fed to the model.
/// </summary>
public sealed class ProjectContext
{
    public static ProjectContext Empty { get; } = new()
    {
        Files = [],
        RawCodes = [],
    };

    public required IReadOnlyList<FileData> Files { get; init; }

    /// <summary>
    /// Signature blocks, one per file, formatted as
    /// <c>**File: relative/path**\n\n&lt;signatures&gt;</c>.
    /// </summary>
    public required IReadOnlyList<string> RawCodes { get; init; }
}
