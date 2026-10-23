using System.Text.Json;

namespace Codai.Core.Tools;

/// <summary>
/// Runs tools on behalf of the agent loop: approval, dispatch, progress reporting and
/// failure containment.
/// </summary>
/// <remarks>
/// This is the seam the provider layer talks to, so the model-calling code never needs
/// to know how approval is obtained or which tools exist.
/// </remarks>
public interface IToolExecutor
{
    /// <summary>The tools offered to the model.</summary>
    IReadOnlyList<ITool> Tools { get; }

    /// <summary>
    /// Runs one tool. Never throws for bad arguments or a failing tool: those become a
    /// failed <see cref="ToolResult"/> that is sent back to the model as feedback.
    /// Cancellation is the exception and propagates.
    /// </summary>
    Task<ToolResult> ExecuteAsync(
        string toolName,
        JsonElement arguments,
        CancellationToken cancellationToken = default);
}
