using System.Text.Json;
using Codai.Core.Tools;

namespace Codai.Mcp.Client;

/// <summary>Adapts one discovered MCP tool to Codai's tool contract.</summary>
internal sealed class McpToolAdapter : ToolBase
{
    private readonly McpToolDefinition _definition;

    public McpToolAdapter(McpToolDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        _definition = definition;
    }

    /// <inheritdoc />
    public override string Name => _definition.Name;

    /// <inheritdoc />
    public override string Description => _definition.Description;

    /// <inheritdoc />
    public override bool IsReadOnly => _definition.IsReadOnly;

    /// <inheritdoc />
    public override bool RequiresApproval => _definition.RequiresApproval;

    /// <inheritdoc />
    public override string ParametersSchema => _definition.ParametersSchema;

    /// <inheritdoc />
    public override async Task<ToolResult> InvokeAsync(
        JsonElement arguments,
        ToolContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _definition.InvokeAsync(arguments, context, cancellationToken).ConfigureAwait(false);
            return ToolResult.Success(result);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ToolResult.Failure($"MCP tool '{Name}' failed: {ex.Message}");
        }
    }
}
