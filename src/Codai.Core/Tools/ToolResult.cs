namespace Codai.Core.Tools;

/// <summary>
/// The outcome of a tool invocation, rendered back to the model as the tool result.
/// </summary>
/// <param name="IsSuccess">
/// False when the tool could not do what was asked. The text is still sent to the
/// model, which is what lets it correct its own mistake on the next turn.
/// </param>
/// <param name="Content">The result text. Truncation is the tool's responsibility.</param>
public readonly record struct ToolResult(bool IsSuccess, string Content)
{
    /// <summary>Whether the agent must stop this request rather than continue using tools.</summary>
    public bool StopsExecution { get; init; }

    /// <summary>The tool did not run; remaining batch calls must wait for a new model round.</summary>
    public bool RequiresModelReview { get; init; }

    /// <summary>
    /// File changes that actually reached disk, including changes made before a later
    /// operation failed. Empty for tools that do not report file edits.
    /// </summary>
    public IReadOnlyList<ToolFileChange> FileChanges { get; init; } = [];

    /// <summary>A successful invocation.</summary>
    public static ToolResult Success(string content) => new(true, content);

    /// <summary>A failed invocation whose message should help the model try again.</summary>
    public static ToolResult Failure(string message) => new(false, message);
}
