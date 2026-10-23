namespace Codai.Core.Tools;

/// <summary>An applied file mutation with line-based diff counts and a bounded preview.</summary>
/// <param name="Path">Project-relative file path.</param>
/// <param name="AddedLines">Number of inserted lines, excluding unchanged context.</param>
/// <param name="RemovedLines">Number of deleted lines, excluding unchanged context.</param>
/// <param name="Preview">Bounded line diff for display, not model reasoning.</param>
public sealed record ToolFileChange(string Path, int AddedLines, int RemovedLines, string Preview);
