using System.Text.Json;

namespace Codai.Core.Tools;

/// <summary>
/// A capability the model can invoke: a name, a description and a JSON Schema for its
/// arguments, plus the code that runs it.
/// </summary>
/// <remarks>
/// Implementations receive arguments that came from a model response and must treat
/// them as untrusted: validate types, resolve paths through
/// <see cref="Codai.Core.IO.ProjectPaths"/> and fail with
/// <see cref="ToolResult.Failure"/> rather than throwing.
/// </remarks>
public interface ITool
{
    /// <summary>Identifier the model calls, e.g. <c>read_file</c>.</summary>
    string Name { get; }

    /// <summary>One-line description shown to the model and in approval prompts.</summary>
    string Description { get; }

    /// <summary>
    /// True when the tool cannot change anything the user cares about. Read-only tools
    /// skip the approval prompt; anything that writes or executes must return false.
    /// </summary>
    bool IsReadOnly { get; }

    /// <summary>
    /// JSON Schema for the arguments object, as a string. Parsed once and cached by
    /// <see cref="ToolBase"/>.
    /// </summary>
    string ParametersSchema { get; }

    /// <summary>
    /// The parsed form of <see cref="ParametersSchema"/>. This is what is advertised to
    /// the model.
    /// </summary>
    JsonElement Schema { get; }

    /// <summary>
    /// Runs the tool. Implementations return a failed result for bad input instead of
    /// throwing, so a model mistake becomes feedback rather than a crashed session.
    /// </summary>
    Task<ToolResult> InvokeAsync(
        JsonElement arguments,
        ToolContext context,
        CancellationToken cancellationToken = default);
}
