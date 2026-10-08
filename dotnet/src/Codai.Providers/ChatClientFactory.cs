using Codai.Providers.Anthropic;
using Codai.Providers.Azure;
using Codai.Providers.Gemini;
using Codai.Providers.OpenAiCompatible;
using Microsoft.Extensions.AI;

namespace Codai.Providers;

/// <summary>
/// Default provider factory. Dispatches on the provider name to the appropriate
/// <see cref="IChatClient"/> implementation.
/// </summary>
public sealed class ChatClientFactory : IChatClientFactory
{
    public IChatClient CreateClient(ResolvedProviderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.EnsureUsable();

        if (string.Equals(options.Provider, ProviderNames.AzureOpenAi, StringComparison.OrdinalIgnoreCase))
        {
            return AzureOpenAiClientFactory.Create(options);
        }

        if (string.Equals(options.Provider, ProviderNames.Anthropic, StringComparison.OrdinalIgnoreCase))
        {
            return new AnthropicChatClient(options);
        }

        if (string.Equals(options.Provider, ProviderNames.Gemini, StringComparison.OrdinalIgnoreCase))
        {
            return new GeminiChatClient(options);
        }

        if (ProviderNames.OpenAiCompatible.Contains(options.Provider))
        {
            return OpenAiCompatibleClientFactory.Create(options);
        }

        throw new ProviderConfigurationException(
            $"Unsupported provider '{options.Provider}'. " +
            $"Supported providers: {string.Join(", ", ProviderNames.OpenAiCompatible.Concat([ProviderNames.AzureOpenAi, ProviderNames.Anthropic, ProviderNames.Gemini]).Order())}.");
    }

    public Core.Abstractions.IChatCompletionService CreateService(ResolvedProviderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var client = CreateClient(options);
        return new ChatCompletionService(client, options);
    }
}
