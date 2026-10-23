using System.Text.Json;
using Codai.Core.Tools;

namespace Codai.Mcp.Client;

/// <summary>A tool discovered from an MCP server.</summary>
/// <param name="Name">Codai-facing tool name.</param>
/// <param name="Description">Description shown to the model and approval prompt.</param>
/// <param name="ParametersSchema">JSON Schema for the arguments object.</param>
/// <param name="IsReadOnly">Whether Codai may offer the tool in read-only modes.</param>
/// <param name="RequiresApproval">Whether Codai should request approval before calling the tool.</param>
/// <param name="InvokeAsync">Invokes the remote capability.</param>
public sealed record McpToolDefinition(
    string Name,
    string Description,
    string ParametersSchema,
    bool IsReadOnly,
    bool RequiresApproval,
    Func<JsonElement, ToolContext, CancellationToken, Task<string>> InvokeAsync);
