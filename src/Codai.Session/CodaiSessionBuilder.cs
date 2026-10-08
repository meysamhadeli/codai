using Codai.Configuration;
using Codai.Core.Abstractions;
using Codai.Core.Tools;
using Codai.Providers;
using Codai.Providers.Abstractions;
using Codai.Session.Tools;
using Codai.Tokens;
using Codai.Tokens.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Codai.Session;

/// <summary>
/// Composition root: turns resolved <see cref="CodaiOptions"/> into a ready-to-run
/// <see cref="CodeSession"/>, wiring the core services, the project scanner, the
/// chat provider and the pricing catalog.
/// </summary>
public static class CodaiSessionBuilder
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
    /// <remarks>
    /// The host is responsible for registering an <see cref="IUserInterface" />
    /// implementation (the CLI registers the Spectre.Console one).
    /// </remarks>
    public static IServiceCollection AddCodaiSession(
        this IServiceCollection services,
        CodaiOptions options,
        string? projectRoot = null,
        bool resumePreviousSession = false)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        var providerOptions = ResolvedProviderOptions.From(options.AiProviderConfig);
        var root = projectRoot ?? Directory.GetCurrentDirectory();

        services.AddCodaiCore();
        services.AddCodaiAnalysis();
        services.AddCodaiTools();
        services.AddCodaiProviders();

        // The pricing catalog implements both contracts; register one instance under
        // each so there is a single source of prices.
        services.AddSingleton<IPricingCatalog>(EmbeddedPricingCatalog.Instance);
        services.AddSingleton<ICostCalculator>(sp => sp.GetRequiredService<IPricingCatalog>());

        // Tool activity is reported through the same interface as everything else, so the
        // host does not have to know tools exist. When no user interface was registered
        // there is no way to report or to ask, so the safe defaults stand: progress is
        // dropped and every state-changing tool is refused.
        services.Replace(ServiceDescriptor.Singleton<IToolObserver>(sp =>
            sp.GetService<IUserInterface>() is { } ui
                ? new InteractiveToolObserver(ui)
                : NullToolObserver.Instance));

        services.Replace(ServiceDescriptor.Singleton<IToolApprover>(sp =>
            sp.GetService<IUserInterface>() is { } ui
                ? new InteractiveToolApprover(ui)
                : DenyAllToolApprover.Instance));

        // The runner is the one seam through which the model reaches the file system,
        // so the provider layer never learns how approval or progress reporting works.
        services.AddSingleton(sp => new ToolRunner(
            sp.GetRequiredService<IToolRegistry>(),
            sp.GetRequiredService<IToolApprover>(),
            sp.GetRequiredService<IToolObserver>(),
            sp.GetRequiredService<IFileSystem>(),
            root));

        services.AddSingleton<IToolExecutor>(sp => sp.GetRequiredService<ToolRunner>());

        services.AddSingleton<IMentionResolver>(sp => new MentionResolver(sp.GetRequiredService<IFileSystem>()));

        services.AddSingleton<IChatCompletionService>(sp =>
        {
            var factory = sp.GetRequiredService<IChatClientFactory>();
            return factory.CreateService(providerOptions, sp.GetRequiredService<IToolExecutor>());
        });

        services.AddSingleton<HistoryCompactor>();

        services.AddSingleton(new SessionOptions
        {
            ProjectRoot = root,
            Provider = providerOptions.Provider,
            Model = providerOptions.Model,
            Theme = options.Theme,
            ResumePreviousSession = resumePreviousSession,
        });

        services.AddSingleton<ProjectContextLoader>();
        services.AddSingleton<ChatExchangeService>();
        services.AddSingleton<ChangeApplicator>();
        services.AddSingleton<SessionCommandHandler>();
        services.AddSingleton<CodeSession>();

        return services;
    }
}
