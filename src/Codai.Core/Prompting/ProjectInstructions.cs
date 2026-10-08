namespace Codai.Core.Prompting;

/// <summary>
/// The project-level instructions that were found and are being followed for the
/// whole session.
/// </summary>
/// <param name="SourcePath">
/// Relative path of the file the instructions came from, using forward slashes.
/// </param>
/// <param name="Content">The instructions themselves, already trimmed and capped.</param>
public sealed record ProjectInstructions(string SourcePath, string Content);
