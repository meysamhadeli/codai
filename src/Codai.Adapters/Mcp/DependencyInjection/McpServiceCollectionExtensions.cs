using Codai.Configuration;
using Codai.Core.Tools;
using Codai.Mcp.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Codai.Mcp.DependencyInjection;

/// <summary>Composition helpers for Codai MCP adapters.</summary>
public static class McpServiceCollectionExtensions
{
    /// <summary>
    /// Discovers configured MCP server tools and registers them as ordinary Codai tools.
    /// </summary>
    /// <remarks>Deferred discovery requires the host to initialize <see cref="IAsyncToolRegistry"/> before model requests.
    /// An optional server source reloads selection and definitions at initialization, not during eager registration.</remarks>
    public static IServiceCollection AddCodaiMcpClientTools(
        this IServiceCollection services,
        McpOptions options,
        string projectRoot,
        IMcpToolDiscoverer? discoverer = null,
        bool deferDiscovery = false,
        IEnumerable<string>? disabledTools = null,
        Func<CancellationToken, Task<IReadOnlyList<McpServerOptions>>>? serverSource = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);

        if (options.Servers.Count == 0 && (!deferDiscovery || serverSource is null))
        {
            return services;
        }

        if (deferDiscovery)
        {
            var original = services.LastOrDefault(descriptor => descriptor.ServiceType == typeof(IToolRegistry));
            services.Replace(ServiceDescriptor.Singleton<IToolRegistry>(provider => new DeferredMcpToolRegistry(
                ((IToolRegistry)(original?.ImplementationInstance ?? original?.ImplementationFactory?.Invoke(provider) ??
                    (original?.ImplementationType is { } type ? ActivatorUtilities.CreateInstance(provider, type) :
                        new ToolRegistry(provider.GetServices<ITool>())))).Tools,
                disabledTools ?? [], async cancellationToken =>
                {
                    var servers = serverSource is null ? options.Servers :
                        await serverSource(cancellationToken).ConfigureAwait(false);
                    var enabled = servers.Where(server => server.Enabled).ToArray();
                    if (enabled.Length == 0)
                    {
                        return [];
                    }
                    if (discoverer is not null)
                    {
                        return await discoverer.DiscoverAsync(enabled, projectRoot, cancellationToken).ConfigureAwait(false);
                    }
                    var stdio = new McpStdioToolDiscoverer().DiscoverAsync(
                        enabled.Where(server => string.Equals(server.Transport, "stdio", StringComparison.OrdinalIgnoreCase)), projectRoot, cancellationToken);
                    var remote = new McpRemoteToolDiscoverer().DiscoverAsync(
                        enabled.Where(server => !string.Equals(server.Transport, "stdio", StringComparison.OrdinalIgnoreCase)), projectRoot, cancellationToken);
                    await Task.WhenAll(stdio, remote).ConfigureAwait(false);
                    return [.. await stdio.ConfigureAwait(false), .. await remote.ConfigureAwait(false)];
                })));
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
