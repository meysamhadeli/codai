using Codai.Core.Abstractions;
using Codai.Core.Assets;
using Codai.Core.Chat;
using Codai.Core.Diffs;
using Codai.Core.IO;
using Codai.Core.Prompting;
using Codai.Core.Tokens;
using Codai.Core.Tools;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
        services.AddSingleton<ICostCalculator>(NullCostCalculator.Instance);
        services.AddSingleton<IFileSystem>(PhysicalFileSystem.Instance);

        // The tool registry is registered by the tool package, which is optional here: a
        // host without tools gets exactly the prompt it had before tools existed.
        services.AddSingleton<IPromptBuilder>(sp => new PromptBuilder(
            EmbeddedResources.FullContextTemplate,
            sp.GetService<IToolRegistry>()?.Tools));

        services.AddSingleton<IRequestedContextResolver>(_ => new RequestedContextResolver(PhysicalFileSystem.Instance));
        services.AddSingleton<ICodeChangeExtractor, CodeChangeExtractor>();
        services.AddSingleton<IDiffApplier>(_ => new DiffApplier(PhysicalFileSystem.Instance));

        // Safe defaults: nothing may change state until a host registers a real
        // approver, and progress messages are dropped until one registers an observer.
        services.TryAddSingleton<IToolApprover>(DenyAllToolApprover.Instance);
        services.TryAddSingleton<IToolObserver>(NullToolObserver.Instance);

        return services;
    }
}
