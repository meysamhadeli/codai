using Microsoft.Extensions.AI;

namespace Codai.Providers;

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
    Core.Abstractions.IChatCompletionService CreateService(ResolvedProviderOptions options);
}
