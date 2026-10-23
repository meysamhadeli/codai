using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;

namespace Codai.Providers.Azure;

/// <summary>
/// Creates <see cref="IChatClient"/> instances for Azure OpenAI deployments.
/// </summary>
internal static class AzureOpenAiClientFactory
{
    public static IChatClient Create(ResolvedProviderOptions options)
    {
        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var endpoint))
        {
            throw new ProviderConfigurationException(
                $"Azure OpenAI has an invalid base URL: '{options.BaseUrl}'.");
        }

        var apiKey = options.ApiKey
            ?? throw new ProviderConfigurationException("Azure OpenAI requires an API key.");

        var client = new AzureOpenAIClient(endpoint, new AzureKeyCredential(apiKey));

        return client.GetChatClient(options.Model).AsIChatClient();
    }
}
