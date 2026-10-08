using Codai.Core.Abstractions;
using Codai.Core.Assets;
using Codai.Core.Diffs;
using Codai.Core.History;
using Codai.Core.IO;
using Codai.Core.Prompting;
using Codai.Core.Tokens;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Composition helpers for the core services.
/// </summary>
public static class CoreServiceCollectionExtensions
{
    /// <summary>Registers the always-available core services.</summary>
    public static IServiceCollection AddCodaiCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IChatHistory, ChatHistory>();
        services.AddSingleton<ITokenTracker, TokenTracker>();
        services.AddSingleton<IFileSystem>(PhysicalFileSystem.Instance);
        services.AddSingleton<IPromptBuilder>(_ => new PromptBuilder(EmbeddedResources.FullContextTemplate));
        services.AddSingleton<IRequestedContextResolver>(_ => new RequestedContextResolver(PhysicalFileSystem.Instance));
        services.AddSingleton<ICodeChangeExtractor, CodeChangeExtractor>();
        services.AddSingleton<IDiffApplier>(_ => new DiffApplier(PhysicalFileSystem.Instance));

        return services;
    }
}
