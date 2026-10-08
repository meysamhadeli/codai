namespace Codai.Core.Diffs;

/// <summary>
/// A code change proposed by the model for a single file.
/// </summary>
/// <param name="RelativePath">Path of the file the change applies to.</param>
/// <param name="Code">
/// The annotated file body: lines are either unchanged, prefixed with <c>+</c>
/// (added) or <c>-</c> (removed).
/// </param>
/// <param name="IsTextFile">
/// True for documentation-style files (<c>.md</c>, <c>.txt</c>) where the body is
/// taken verbatim and diff markers are not interpreted.
/// </param>
public sealed record CodeChange(string RelativePath, string Code, bool IsTextFile = false);
