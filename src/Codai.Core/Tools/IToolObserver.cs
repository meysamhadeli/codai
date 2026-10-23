using System.Text.Json;
using Codai.Core.Abstractions;

namespace Codai.Core.Tools;

/// <summary>
/// Receives progress notifications while tools run, so the session can show what the
/// model is doing instead of appearing to hang.
/// </summary>
public interface IToolObserver
{
    /// <summary>A tool is about to run. <paramref name="summary"/> is for display only.</summary>
    void OnToolStarted(string toolName, string summary);

    void OnToolStarted(string toolName, string summary, JsonElement arguments) => OnToolStarted(toolName, summary);

    /// <summary>
    /// A tool finished. <paramref name="detail"/> carries the failure reason on error,
    /// and a short result summary otherwise.
    /// </summary>
    void OnToolCompleted(string toolName, bool succeeded, string detail);

    /// <summary>Bounded live command output; legacy observers may ignore it.</summary>
    void OnToolOutput(string toolName, ProcessOutput output) { }

    void OnToolResult(string toolName, JsonElement arguments, ToolResult result) { }

    /// <summary>A tool was refused before it ran.</summary>
    void OnToolDenied(string toolName, string summary);
}
