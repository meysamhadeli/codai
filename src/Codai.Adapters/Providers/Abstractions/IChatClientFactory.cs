using Microsoft.Extensions.AI;

namespace Codai.Providers.Abstractions;

/// <summary>
/// Builds the chat client (and the Codai-facing service adapter) for a provider.
/// </summary>
public interface IChatClientFactory
{
    /// <summary>Creates a raw <see cref="IChatClient"/> for the resolved options.</summary>
    IChatClient CreateClient(ResolvedProviderOptions options);

    /// <summary>
    /// Creates a <see cref="Core.Abstractions.IChatCompletionService"/> that reports
    /// provider/model names and token usage.
    /// </summary>
    /// <param name="options">The resolved provider options.</param>
    /// <param name="toolExecutor">
    /// When supplied, the tools it exposes are advertised to the model and invoked on
    /// its behalf. When omitted, no tools are offered.
    /// </param>
    Core.Abstractions.IChatCompletionService CreateService(
        ResolvedProviderOptions options,
        Core.Tools.IToolExecutor? toolExecutor = null);
}
