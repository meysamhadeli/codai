using Codai.Application.Inspection;
using Codai.Application.Interaction;
using Codai.Configuration;
using Codai.Core.Abstractions;
using Codai.Core.Agents;
using Codai.Core.Chat;
using Codai.Core.Prompting;
using Codai.Core.Tools;
using Codai.Mcp.DependencyInjection;
using Codai.Pricing;
using Codai.Pricing.Abstractions;
using Codai.Providers;
using Codai.Providers.Abstractions;
using Codai.Tools.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Codai.Application;

/// <summary>
/// Composition root: turns resolved <see cref="CodaiOptions"/> into a ready-to-run
/// <see cref="CodeSession"/>, wiring the core services, the project scanner, the
/// chat provider and the pricing catalog.
/// </summary>
public static class CodaiRuntimeBuilder
{
    /// <summary>
    /// Registers every service the interactive session needs.
    /// </summary>
    /// <param name="services">The service collection to populate.</param>
    /// <param name="options">Resolved configuration.</param>
    /// <param name="projectRoot">Directory to scan; defaults to the current directory.</param>
    /// <param name="resumePreviousSession">
    /// True when the caller asked to continue the stored conversation instead of
    /// starting a fresh one.
    /// </param>
    /// <param name="resumeTaskId">Optional task ID whose conversation should be resumed.</param>
    /// <param name="mode">The mode the session starts in; the user can change it with <c>:mode</c>.</param>
    /// <param name="allowShell">
    /// True when <c>run_command</c> may be offered to the model. Every call to it still
    /// asks the registered <see cref="IToolApprover"/> first.
    /// </param>
    /// <param name="autoCompact">
    /// True when a conversation that outgrows the model's context window should be
    /// summarised automatically.
    /// </param>
    /// <param name="allowSubAgents">
    /// True when <c>task</c> may be offered to the model, which lets it hand an
    /// investigation to a nested agent with a context window of its own. Every use spends
    /// a model call, and no approval prompt stands in the way, so a host opts in
    /// deliberately.
    /// </param>
    /// <param name="systemPrompt">Optional replacement for the default system template.</param>
    /// <param name="dataDirectory">Optional isolated state directory; workspace files retain their project root.</param>
    /// <param name="hookDirectories">Ordered manifest directories; null or empty disables hooks.</param>
    /// <param name="compactionStrategy">How automatic/manual compaction creates replacement history.</param>
    /// <param name="configurationDirectory">
    /// Directory configuration was discovered from, when it differs from the project root;
    /// reported by <c>:config</c>.
    /// </param>
    /// <param name="globalConfigurationDirectory">Optional global customization root; null loads project catalogs only.</param>
    /// <remarks>
    /// The host is responsible for registering an <see cref="IUserInterface" />
    /// implementation (the CLI registers the Spectre.Console one).
    /// </remarks>
    public static IServiceCollection AddCodaiApplicationRuntime(
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

        var providerOptions = ResolvedProviderOptions.From(options.AiProviderConfig);
        var planOptions = ResolveModeProviderOptions(options, providerOptions, options.PlanProvider, options.PlanModel);
        var agentOptions = ResolveModeProviderOptions(options, providerOptions, options.AgentProvider, options.AgentModel);
        var root = projectRoot ?? Directory.GetCurrentDirectory();

        services.AddCodaiCore();
        services.Replace(ServiceDescriptor.Singleton<IWorkspaceCheckpointStore>(sp =>
            options.CheckpointsEnabled
                ? new CheckpointProgressStore(
                    new GitWorkspaceCheckpointStore(
                        sp.GetRequiredService<IFileSystem>(),
                        sp.GetRequiredService<IProcessRunner>(),
                        options.CheckpointRetention),
                    sp.GetRequiredService<IAgentProgressSink>())
                : new NullWorkspaceCheckpointStore()));
        if (dataDirectory is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
            var stateDirectory = Path.GetFullPath(dataDirectory);
            services.AddSingleton(sp => new IsolatedStateStores(sp.GetRequiredService<IFileSystem>(), stateDirectory));
            services.Replace(ServiceDescriptor.Singleton<ISessionStore>(sp => sp.GetRequiredService<IsolatedStateStores>()));
            services.Replace(ServiceDescriptor.Singleton<IAgentTaskStore>(sp => sp.GetRequiredService<IsolatedStateStores>()));
            services.Replace(ServiceDescriptor.Singleton<IBackgroundAgentStore>(sp => sp.GetRequiredService<IsolatedStateStores>()));
            services.Replace(ServiceDescriptor.Singleton<IAgentTeamStore>(sp => sp.GetRequiredService<IsolatedStateStores>()));
            services.Replace(ServiceDescriptor.Singleton<ITranscriptStore>(sp => sp.GetRequiredService<IsolatedStateStores>()));
        }

        if (systemPrompt is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(systemPrompt);
            services.Replace(ServiceDescriptor.Singleton<IPromptBuilder>(sp =>
                new PromptBuilder(systemPrompt, sp.GetRequiredService<IToolRegistry>().Tools)));
        }

        services.Replace(ServiceDescriptor.Singleton(new ToolCallGovernor
        {
            MaxConsecutiveFailures = options.Budget.MaxConsecutiveToolFailures,
        }));
        services.Replace(ServiceDescriptor.Singleton(new AgentBudget
        {
            MaxTokens = options.Budget.MaxTokens,
            MaxCost = options.Budget.MaxCost,
            MaxToolCalls = options.Budget.MaxToolCalls > 0
                ? options.Budget.MaxToolCalls
                : AgentBudget.DefaultMaxToolCalls,
        }));
        services.AddCodaiAnalysis();
        ToolPluginLoader.Register(services, options.PluginPaths, root, options.DisabledPlugins);
        services.AddCodaiMcpClientTools(options.Mcp, root);
        services.TryAddSingleton<IClarificationHandler>(sp =>
            sp.GetService<IUserInterface>() is { } ui
                ? new InteractiveClarificationHandler(ui)
                : UnavailableClarificationHandler.Instance);
        services.AddCodaiTools(allowShell, allowSubAgents, options.DisabledTools);
        services.AddCodaiProviders();

        // The mode the session was started in replaces the safe default the core
        // registered. The holder is a singleton because :mode changes it in place.
        services.Replace(ServiceDescriptor.Singleton(new AgentModeState(mode)));
        services.AddSingleton(new ModeModelState(planOptions, agentOptions));

        // The pricing catalog implements both contracts; register one instance under
        // each so there is a single source of prices.
        services.AddSingleton<IPricingCatalog>(EmbeddedPricingCatalog.Instance);
        services.AddSingleton<ICostCalculator>(sp => sp.GetRequiredService<IPricingCatalog>());

        // Tool activity is reported through the same interface as everything else, so the
        // host does not have to know tools exist. When no user interface was registered
        // there is no way to report or to ask, so the safe defaults stand: progress is
        // dropped and every state-changing tool is refused.
        services.Replace(ServiceDescriptor.Singleton<IToolObserver>(sp => new FootprintObserver(
            sp.GetService<IUserInterface>() is { } ui
                ? new InteractiveToolObserver(ui, sp.GetRequiredService<IAgentProgressSink>())
                : NullToolObserver.Instance,
            sp.GetRequiredService<SessionFootprint>())));

        services.Replace(ServiceDescriptor.Singleton<IAgentProgressSink>(sp =>
            sp.GetService<IUserInterface>() is { } ui
                ? new InteractiveAgentProgressSink(ui)
                : NullAgentProgressSink.Instance));

        services.Replace(ServiceDescriptor.Singleton<IToolApprover>(sp =>
            sp.GetService<IUserInterface>() is { } ui
                ? new InteractiveToolApprover(ui)
                : DenyAllToolApprover.Instance));

        services.AddSingleton<IAgentHooks>(sp => hookDirectories is { Count: > 0 }
            ? new HookRunner(sp.GetRequiredService<IFileSystem>(), sp.GetRequiredService<IProcessRunner>(),
                sp.GetRequiredService<IToolApprover>(), sp.GetRequiredService<AgentModeState>(),
                hookDirectories, allowShell, sp.GetRequiredService<IAgentProgressSink>(),
                sp.GetServices<IPluginAgentHook>())
            : NullAgentHooks.Instance);

        // The runner is the one seam through which the model reaches the file system,
        // so the provider layer never learns how approval or progress reporting works.
        // It shares the request governor with the exchange service, which resets it at
        // the start of every user request, and the access policy, which the exchange
        // service fills in from the mode the request was built for.
        services.AddSingleton<RequestRuleGuidance>();
        services.AddSingleton(sp => new ToolRunner(
            sp.GetRequiredService<IToolRegistry>(),
            sp.GetRequiredService<IToolApprover>(),
            sp.GetRequiredService<IToolObserver>(),
            sp.GetRequiredService<IFileSystem>(),
            root,
            governor: sp.GetRequiredService<ToolCallGovernor>(),
            access: sp.GetRequiredService<ToolAccessPolicy>(),
            budget: sp.GetRequiredService<AgentBudget>(),
            hooks: sp.GetRequiredService<IAgentHooks>(),
            guidance: sp.GetRequiredService<RequestRuleGuidance>(),
            checkpoints: sp.GetRequiredService<IWorkspaceCheckpointStore>(),
            taskContext: sp.GetRequiredService<AgentTaskContext>()));

        services.AddSingleton<IToolExecutor>(sp => sp.GetRequiredService<ToolRunner>());

        // The nested agent loop. It builds its own client over the read-only tools, so it
        // needs the same registry the session uses, and it is registered only when the
        // host asked for the tool that reaches it: composing a loop nobody can start would
        // be a provider client built for nothing.
        if (allowSubAgents)
        {
            services.AddSingleton<ISubAgent>(sp => new SubAgentRunner(
                sp.GetRequiredService<IChatClientFactory>(),
                agentOptions,
                sp.GetRequiredService<IToolRegistry>(),
                sp.GetRequiredService<IFileSystem>(),
                sp.GetRequiredService<ITokenTracker>(),
                sp.GetService<IUserInterface>(),
                root,
                maxConsecutiveToolFailures: options.Budget.MaxConsecutiveToolFailures));
            services.AddSingleton<IBackgroundAgentManager>(sp => new BackgroundAgentManager(
                sp.GetRequiredService<ISubAgent>(),
                sp.GetRequiredService<IBackgroundAgentStore>(),
                sp.GetRequiredService<IAgentProgressSink>(),
                root));
            services.AddSingleton<IAgentTeamCoordinator>(sp => new AgentTeamCoordinator(
                sp.GetRequiredService<IBackgroundAgentManager>(),
                sp.GetRequiredService<IAgentTeamStore>(),
                root));
        }

        services.AddSingleton<IMentionResolver>(sp => new MentionResolver(sp.GetRequiredService<IFileSystem>()));

        services.AddSingleton<IChatCompletionService>(sp => new ModeAwareChatCompletionService(
            sp.GetRequiredService<AgentModeState>(),
            sp.GetRequiredService<ModeModelState>(),
            sp.GetRequiredService<IChatClientFactory>(),
            sp.GetRequiredService<IToolExecutor>(),
            sp.GetRequiredService<IChatClientFactory>().CreateService(
                planOptions, sp.GetRequiredService<IToolExecutor>()),
            sp.GetRequiredService<IChatClientFactory>().CreateService(
                agentOptions, sp.GetRequiredService<IToolExecutor>())));

        services.AddSingleton<HistoryCompactor>();
        services.AddSingleton<BackgroundSummarizer>();
        services.AddSingleton(new SessionFootprint());
        services.AddSingleton<ContextRestorer>();
        services.AddSingleton<MicroCompactor>();
        services.AddSingleton<AutoCompactor>();

        services.AddSingleton(new SessionOptions
        {
            ProjectRoot = root,
            Provider = providerOptions.Provider,
            Model = providerOptions.Model,
            Theme = options.Theme,
            ResumePreviousSession = resumePreviousSession,
            ResumeTaskId = resumeTaskId,
            Mode = mode,
            AutoCompact = autoCompact,
            CompactionStrategy = compactionStrategy,
            CheckpointRetention = options.CheckpointRetention,
            CheckpointsEnabled = options.CheckpointsEnabled,
        });

        services.AddInspectionContext(options, providerOptions, root, allowShell, autoCompact,
            allowSubAgents, dataDirectory, hookDirectories, compactionStrategy, configurationDirectory,
            globalConfigurationDirectory: globalConfigurationDirectory);

        services.AddSingleton<ProjectContextLoader>();
        services.AddSingleton<ProjectGuidanceLoader>();
        services.AddSingleton<UserCommandResolver>();
        services.AddSingleton<ChatExchangeService>();
        services.AddSingleton<ChangeApplicator>();
        if (allowShell)
        {
            services.AddSingleton<AutomaticRepairWorkflow>();
        }
        services.AddSingleton<SessionCommandHandler>();
        services.AddSingleton<SessionSettingsWorkflow>();
        services.AddSingleton<CodeSession>();

        return services;
    }

    private static ResolvedProviderOptions ResolveModeProviderOptions(
        CodaiOptions options,
        ResolvedProviderOptions fallback,
        string? provider,
        string? model)
    {
        if (string.IsNullOrWhiteSpace(provider) && string.IsNullOrWhiteSpace(model))
        {
            return fallback;
        }

        var configured = new AiProviderOptions
        {
            Provider = string.IsNullOrWhiteSpace(provider) ? fallback.Provider : provider,
            Model = string.IsNullOrWhiteSpace(model) ? fallback.Model : model,
            BaseUrl = options.AiProviderConfig.BaseUrl,
            Stream = options.AiProviderConfig.Stream,
            Temperature = options.AiProviderConfig.Temperature,
            ReasoningEffort = options.AiProviderConfig.ReasoningEffort,
            MaxTokens = options.AiProviderConfig.MaxTokens,
            ApiKey = options.AiProviderConfig.ApiKey,
            Account = string.IsNullOrWhiteSpace(provider) || provider == fallback.Provider ? options.AiProviderConfig.Account : null,
            ApiVersion = options.AiProviderConfig.ApiVersion,
        };
        return ResolvedProviderOptions.From(configured);
    }
}
