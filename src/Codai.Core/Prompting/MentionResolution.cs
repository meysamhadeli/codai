namespace Codai.Core.Prompting;

/// <summary>
/// The outcome of resolving the <c>@path</c> mentions of one request.
/// </summary>
/// <param name="Context">
/// The rendered file blocks, or <c>null</c> when nothing could be attached.
/// </param>
/// <param name="Files">Relative paths of the files that were attached, in order.</param>
/// <param name="Unresolved">
/// Mentions that matched no file, so the user can be told about the typo instead of
/// silently getting a prompt without the context they asked for.
/// </param>
public sealed record MentionResolution(
    string? Context,
    IReadOnlyList<string> Files,
    IReadOnlyList<string> Unresolved)
{
    /// <summary>No mentions, or none that could be resolved.</summary>
    public static MentionResolution Empty { get; } = new(null, [], []);
}
