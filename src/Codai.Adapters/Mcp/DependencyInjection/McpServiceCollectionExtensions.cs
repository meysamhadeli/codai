using Codai.Configuration;
using Codai.Core.Tools;
using Codai.Mcp.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Codai.Mcp.DependencyInjection;

/// <summary>Composition helpers for Codai MCP adapters.</summary>
public static class McpServiceCollectionExtensions
{
    /// <summary>
    /// Discovers configured MCP server tools and registers them as ordinary Codai tools.
    /// </summary>
    public static IServiceCollection AddCodaiMcpClientTools(
        this IServiceCollection services,
        McpOptions options,
        string projectRoot,
        IMcpToolDiscoverer? discoverer = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);

        if (options.Servers.Count == 0)
        {
            return services;
        }

        IReadOnlyList<McpToolDefinition> definitions;
        if (discoverer is not null)
        {
            definitions = discoverer.DiscoverAsync(options.Servers.Where(server => server.Enabled), projectRoot)
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }
        else
        {
            var enabled = options.Servers.Where(server => server.Enabled).ToArray();
            var stdio = new McpStdioToolDiscoverer().DiscoverAsync(
                enabled.Where(server => string.Equals(server.Transport, "stdio", StringComparison.OrdinalIgnoreCase)), projectRoot)
                .ConfigureAwait(false).GetAwaiter().GetResult();
            var remote = new McpRemoteToolDiscoverer().DiscoverAsync(
                enabled.Where(server => !string.Equals(server.Transport, "stdio", StringComparison.OrdinalIgnoreCase)), projectRoot)
                .ConfigureAwait(false).GetAwaiter().GetResult();
            definitions = [.. stdio, .. remote];
        }

        foreach (var definition in definitions)
        {
            services.AddSingleton<ITool>(new McpToolAdapter(definition));
        }

        return services;
    }
}
