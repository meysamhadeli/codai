namespace Codai.Configuration;

/// <summary>
/// How a provider expects to be talked to on the wire. Drives the dispatch in the
/// provider factory so a new provider only has to be described once.
/// </summary>
public enum ProviderWireFormat
{
    /// <summary>OpenAI chat-completions format (OpenAI, Ollama, DeepSeek, OpenRouter, Qwen, Mistral, Grok).</summary>
    OpenAiCompatible,

    /// <summary>Azure OpenAI deployments, addressed by deployment name.</summary>
    AzureOpenAi,

    /// <summary>Anthropic Messages API.</summary>
    Anthropic,

    /// <summary>Google Gemini <c>generateContent</c> API.</summary>
    Gemini,
}
