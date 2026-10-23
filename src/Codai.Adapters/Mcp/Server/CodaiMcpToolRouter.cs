using System.Text.Json;
using Codai.Core.Tools;
using ModelContextProtocol.Server;

namespace Codai.Mcp.Server;

/// <summary>MCP tool surface that routes calls into Codai's existing tool executor.</summary>
[McpServerToolType]
public sealed class CodaiMcpToolRouter
{
    private readonly IToolExecutor _executor;

    public CodaiMcpToolRouter(IToolExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(executor);
        _executor = executor;
    }

    /// <summary>Lists the Codai tools exposed by this MCP host.</summary>
    [McpServerTool(Name = "codai_list_tools")]
    public string ListTools() => JsonSerializer.Serialize(_executor.Tools.Select(tool => new
    {
        tool.Name,
        tool.Description,
        tool.IsReadOnly,
        Schema = JsonDocument.Parse(tool.ParametersSchema).RootElement,
    }));

    /// <summary>Invokes a Codai tool by name with a JSON argument object.</summary>
    [McpServerTool(Name = "codai_call_tool")]
    public async Task<string> CallToolAsync(
        string name,
        string arguments,
        CancellationToken cancellationToken = default)
    {
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(arguments) ? "{}" : arguments);
        var result = await _executor.ExecuteAsync(name, document.RootElement, cancellationToken)
            .ConfigureAwait(false);

        return JsonSerializer.Serialize(new
        {
            result.IsSuccess,
            result.Content,
        });
    }
}
