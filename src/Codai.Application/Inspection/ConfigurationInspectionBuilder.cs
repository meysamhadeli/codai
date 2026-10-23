using Codai.Configuration;
using Codai.Core.Abstractions;
using Codai.Core.Agents;
using Codai.Core.Chat;
using Codai.Core.Customization;
using Codai.Core.Tools;
using Codai.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Codai.Application.Inspection;

/// <summary>Composes configuration inspection without loading executable integrations.</summary>
public static class ConfigurationInspectionBuilder
{
    /// <summary>Registers offline inspection; no provider, plugin, MCP or hook is executed.</summary>
    public static IServiceCollection AddCodaiConfigurationInspection(
        this IServiceCollection services,
        CodaiOptions options,
        string projectRoot,
        AgentMode mode = AgentModeParser.Default,
        bool allowShell = false,
        bool autoCompact = true,
        bool allowSubAgents = false,
        string? dataDirectory = null,
        IReadOnlyList<string>? hookDirectories = null,
        CompactionStrategy compactionStrategy = CompactionStrategy.Agentic,
        string? configurationDirectory = null,
        string? globalConfigurationDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        services.AddCodaiCore();
        services.AddCodaiTools(allowShell, allowSubAgents);
        services.Replace(ServiceDescriptor.Singleton(new AgentModeState(mode)));
        services.Replace(ServiceDescriptor.Singleton(new ToolCallGovernor
        {
            MaxConsecutiveFailures = options.Budget.MaxConsecutiveToolFailures,
        }));
        services.Replace(ServiceDescriptor.Singleton(new AgentBudget
        {
            MaxTokens = options.Budget.MaxTokens,
            MaxCost = options.Budget.MaxCost,
            MaxToolCalls = options.Budget.MaxToolCalls > 0
                ? options.Budget.MaxToolCalls : AgentBudget.DefaultMaxToolCalls,
        }));
        services.AddInspectionContext(options, ResolvedProviderOptions.From(options.AiProviderConfig),
            projectRoot, allowShell, autoCompact, allowSubAgents, dataDirectory, hookDirectories,
            compactionStrategy, configurationDirectory, externalToolsInspected: false,
            globalConfigurationDirectory: globalConfigurationDirectory);
        return services;
    }

    internal static void AddInspectionContext(this IServiceCollection services,
        CodaiOptions options, ResolvedProviderOptions provider, string root,
        bool allowShell, bool autoCompact, bool allowSubAgents, string? dataDirectory,
        IReadOnlyList<string>? hookDirectories, CompactionStrategy compactionStrategy,
        string? configurationDirectory, bool externalToolsInspected = true,
        string? globalConfigurationDirectory = null)
    {
        if (globalConfigurationDirectory is not null)
        {
            services.Replace(ServiceDescriptor.Singleton<IProjectRuleCatalog>(provider =>
                new ProjectRuleCatalog(provider.GetRequiredService<IFileSystem>(), globalConfigurationDirectory)));
            services.Replace(ServiceDescriptor.Singleton<ICommandCatalog>(provider =>
                new CommandCatalog(provider.GetRequiredService<IFileSystem>(), globalConfigurationDirectory)));
            services.Replace(ServiceDescriptor.Singleton<ISkillCatalog>(provider =>
                new SkillCatalog(provider.GetRequiredService<IFileSystem>(), globalConfigurationDirectory)));
        }

        services.AddSingleton(new ConfigurationInspectionContext
        {
            ProjectRoot = root,
            ConfigurationDirectory = configurationDirectory ?? root,
            GlobalConfigurationDirectory = globalConfigurationDirectory,
            ConfigurationFile = options.LoadedFrom,
            DataDirectory = dataDirectory,
            Provider = provider.Provider,
            Model = provider.Model,
            Theme = options.Theme,
            AutoCompact = autoCompact,
            CompactionStrategy = compactionStrategy.ToString().ToLowerInvariant(),
            BaseUrl = provider.BaseUrl,
            ReasoningEffort = provider.ReasoningEffort,
            Temperature = provider.Temperature,
            MaxTokens = provider.MaxTokens,
            Stream = provider.Stream,
            ApiKeyConfigured = !string.IsNullOrWhiteSpace(provider.ApiKey),
            AllowShell = allowShell,
            AllowSubAgents = allowSubAgents,
            DisabledTools = options.DisabledTools,
            McpServers = options.Mcp.Servers,
            PluginPaths = options.PluginPaths,
            HookDirectories = hookDirectories ?? [],
            ExternalToolsInspected = externalToolsInspected,
        });
        services.AddSingleton<Func<IToolRegistry>>(provider => () => provider.GetRequiredService<IToolRegistry>());
        services.AddSingleton<ConfigurationInspector>();
    }
}
