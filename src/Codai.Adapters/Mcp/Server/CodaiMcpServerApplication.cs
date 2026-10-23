using Codai.Configuration;
using Codai.Core.Abstractions;
using Codai.Core.Tools;
using Codai.Mcp.DependencyInjection;
using Codai.Tools.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Codai.Mcp.Server;

/// <summary>Runs Codai tools as an MCP stdio server.</summary>
public static class CodaiMcpServerApplication
{
    /// <summary>Runs the MCP server until stdin closes or cancellation is requested.</summary>
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);

        var root = Directory.GetCurrentDirectory();
        var options = CodaiConfigurationLoader.Load(new CodaiConfigurationRequest(root, null, args));

        var builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddCodaiCore();
        builder.Services.AddCodaiAnalysis();
        ToolPluginLoader.Register(builder.Services, options.PluginPaths, root, options.DisabledPlugins);
        builder.Services.AddCodaiMcpClientTools(options.Mcp, root);
        builder.Services.AddCodaiTools(allowShell: false, allowSubAgents: false);

        builder.Services.AddSingleton(sp => new ToolRunner(
            sp.GetRequiredService<IToolRegistry>(),
            sp.GetRequiredService<IToolApprover>(),
            sp.GetRequiredService<IToolObserver>(),
            sp.GetRequiredService<IFileSystem>(),
            root,
            governor: sp.GetRequiredService<ToolCallGovernor>(),
            access: sp.GetRequiredService<ToolAccessPolicy>()));

        builder.Services.AddSingleton<IToolExecutor>(sp => sp.GetRequiredService<ToolRunner>());

        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithTools<CodaiMcpToolRouter>();

        await builder.Build().RunAsync(cancellationToken).ConfigureAwait(false);
        return 0;
    }
}
