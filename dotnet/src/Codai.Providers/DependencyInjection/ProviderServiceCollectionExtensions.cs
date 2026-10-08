using Codai.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Composition helpers for chat providers.
/// </summary>
public static class ProviderServiceCollectionExtensions
{
    /// <summary>Registers the provider factory.</summary>
    public static IServiceCollection AddCodaiProviders(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IChatClientFactory, ChatClientFactory>();

        return services;
    }
}
