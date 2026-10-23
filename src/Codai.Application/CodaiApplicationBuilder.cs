using Codai.Configuration;
using Codai.Core.Agents;
using Codai.Core.Chat;
using Microsoft.Extensions.DependencyInjection;

namespace Codai.Application;

/// <summary>
/// Composes the host-neutral Codai application runtime.
/// </summary>
public static class CodaiApplicationBuilder
{
    /// <summary>
    /// Registers the application runtime and exposes it through a host-neutral contract.
    /// </summary>
    public static IServiceCollection AddCodaiApplication(
        this IServiceCollection services,
        CodaiOptions options,
        string? projectRoot = null,
        bool resumePreviousSession = false,
        string? resumeTaskId = null,
        AgentMode mode = AgentModeParser.Default,
        bool allowShell = false,
        bool autoCompact = true,
        bool allowSubAgents = false,
        string? systemPrompt = null,
        string? dataDirectory = null,
        IReadOnlyList<string>? hookDirectories = null,
        CompactionStrategy compactionStrategy = CompactionStrategy.Agentic,
        string? configurationDirectory = null,
        string? globalConfigurationDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        services.AddCodaiApplicationRuntime(
            options,
            projectRoot,
            resumePreviousSession,
            resumeTaskId,
            mode,
            allowShell,
            autoCompact,
            allowSubAgents,
            systemPrompt,
            dataDirectory,
            hookDirectories,
            compactionStrategy,
            configurationDirectory,
            globalConfigurationDirectory);

        services.AddSingleton<ICodaiSession>(serviceProvider =>
            new CodaiSessionAdapter(serviceProvider.GetRequiredService<CodeSession>()));

        return services;
    }

    private sealed class CodaiSessionAdapter(CodeSession session) : ICodaiSession
    {
        public Task<int> RunAsync(CancellationToken cancellationToken = default) =>
            session.RunAsync(cancellationToken);

        public Task<int> RunTaskAsync(string request, CancellationToken cancellationToken = default) =>
            session.RunTaskAsync(request, cancellationToken);

        public Task<int> ExecutePlanAsync(string instructions = "", CancellationToken cancellationToken = default) =>
            session.ExecutePlanAsync(instructions, cancellationToken);
    }
}
