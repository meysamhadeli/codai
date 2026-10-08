namespace Codai.Core.Tools;

/// <summary>
/// A request to run a tool, shown to the user before a tool that can change something
/// is allowed to run.
/// </summary>
/// <param name="ToolName">The tool the model asked for.</param>
/// <param name="Description">That tool's description, so the prompt is self-explanatory.</param>
/// <param name="Summary">The call rendered for display, e.g. <c>run_command(cmd=dotnet test)</c>.</param>
public readonly record struct ToolApprovalRequest(
    string ToolName,
    string Description,
    string Summary);
