using Codai.Configuration;
using Codai.Core.Abstractions;
using Codai.Core.Tools;
using Codai.Providers.Abstractions;
using Codai.Providers.Anthropic;
using Codai.Providers.Azure;
using Codai.Providers.Gemini;
using Codai.Providers.OpenAiCompatible;
using Microsoft.Extensions.AI;

namespace Codai.Providers;

/// <summary>
/// Default provider factory. Dispatches on the provider's wire format, which comes
/// from the single provider catalog rather than a hand-maintained list.
/// </summary>
public sealed class ChatClientFactory : IChatClientFactory
{
    /// <inheritdoc />
    public IChatClient CreateClient(ResolvedProviderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.EnsureUsable();

        var descriptor = ProviderCatalog.Get(options.Provider);

        return descriptor.WireFormat switch
        {
            ProviderWireFormat.AzureOpenAi => AzureOpenAiClientFactory.Create(options),
            ProviderWireFormat.Anthropic => new AnthropicChatClient(options),
            ProviderWireFormat.Gemini => new GeminiChatClient(options),
            ProviderWireFormat.OpenAiCompatible => OpenAiCompatibleClientFactory.Create(options),
            _ => throw new ProviderConfigurationException(
                $"Provider '{options.Provider}' uses an unsupported wire format " +
                $"'{descriptor.WireFormat}'. Supported providers: " +
                $"{string.Join(", ", ProviderCatalog.SupportedNames)}."),
        };
    }

    /// <inheritdoc />
    public IChatCompletionService CreateService(
        ResolvedProviderOptions options,
        IToolExecutor? toolExecutor = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new ChatCompletionService(CreateClient(options), options, toolExecutor);
    }
}
