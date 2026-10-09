namespace Codai.Core.Customization;

/// <summary>
/// A user-defined <c>:command</c> loaded from <c>.codai/commands/*.md</c>.
/// </summary>
/// <param name="Name">
/// Command name without the leading colon, lower-cased. Taken from the file name.
/// </param>
/// <param name="Description">
/// What the command does. Shown by <c>:commands</c>; taken from front matter, or from
/// the first line of the body when the file has none.
/// </param>
/// <param name="ArgumentHint">Optional reminder of what arguments the command takes.</param>
/// <param name="Body">The prompt this command expands to.</param>
/// <param name="SourcePath">Relative path of the file it came from.</param>
public sealed record CommandDefinition(
    string Name,
    string Description,
    string ArgumentHint,
    string Body,
    string SourcePath);
