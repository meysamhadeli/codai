using Codai.Analysis;
using Codai.Configuration;
using Codai.Core.Abstractions;
using Codai.Providers;
using Codai.Tokens;
using Microsoft.Extensions.DependencyInjection;

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
    /// <remarks>
    /// The host is responsible for registering an <see cref="IUserInterface" />
    /// implementation (the CLI registers the Spectre.Console one).
    /// </remarks>
    public static IServiceCollection AddCodaiSession(
        this IServiceCollection services,
        CodaiOptions options,
        string? projectRoot = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        var providerOptions = ResolvedProviderOptions.From(options.AiProviderConfig);
        var root = projectRoot ?? Directory.GetCurrentDirectory();

        services.AddCodaiCore();
        services.AddCodaiAnalysis();
        services.AddCodaiProviders();
        services.AddSingleton<IPricingCatalog>(EmbeddedPricingCatalog.Instance);
        services.AddSingleton<ICostCalculator>(EmbeddedPricingCatalog.Instance);

        services.AddSingleton<IChatCompletionService>(sp =>
        {
            var factory = sp.GetRequiredService<IChatClientFactory>();
            return factory.CreateService(providerOptions);
        });

        services.AddSingleton(new SessionOptions
        {
            ProjectRoot = root,
            WorkingDirectory = root,
            Provider = providerOptions.Provider,
            Model = providerOptions.Model,
            Theme = options.Theme,
        });

        services.AddSingleton<CodeSession>();

        return services;
    }
}
