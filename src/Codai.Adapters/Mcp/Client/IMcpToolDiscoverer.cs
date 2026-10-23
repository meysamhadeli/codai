using Codai.Configuration;

namespace Codai.Mcp.Client;

/// <summary>Discovers Codai-facing tools from configured MCP servers.</summary>
public interface IMcpToolDiscoverer
{
    /// <summary>Starts configured servers and returns their discovered tools.</summary>
    Task<IReadOnlyList<McpToolDefinition>> DiscoverAsync(
        IEnumerable<McpServerOptions> servers,
        string projectRoot,
        CancellationToken cancellationToken = default);
}
