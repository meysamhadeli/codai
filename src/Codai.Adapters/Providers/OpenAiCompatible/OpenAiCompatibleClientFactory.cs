using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;

namespace Codai.Providers.OpenAiCompatible;

/// <summary>
/// Creates <see cref="IChatClient"/> instances for providers that expose the OpenAI
/// chat-completions wire format (OpenAI, DeepSeek, OpenRouter, Qwen, Mistral, Grok, Ollama).
/// </summary>
internal static class OpenAiCompatibleClientFactory
{
    public static IChatClient Create(ResolvedProviderOptions options)
    {
        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var endpoint))
        {
            throw new ProviderConfigurationException(
                $"Provider '{options.Provider}' has an invalid base URL: '{options.BaseUrl}'.");
        }

        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = endpoint,
        };

        // Local runtimes such as Ollama do not validate the key, but the SDK still
        // requires a non-empty credential.
        var credential = new ApiKeyCredential(options.ApiKey ?? "not-required");
        var client = new OpenAIClient(credential, clientOptions);

        return client.GetChatClient(options.Model).AsIChatClient();
    }
}
