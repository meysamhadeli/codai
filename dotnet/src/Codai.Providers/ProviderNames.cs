namespace Codai.Providers;

/// <summary>
/// Provider identifiers understood by <see cref="ChatClientFactory"/>.
/// </summary>
public static class ProviderNames
{
    public const string OpenAi = "openai";
    public const string AzureOpenAi = "azure-openai";
    public const string Anthropic = "anthropic";
    public const string Gemini = "gemini";
    public const string Ollama = "ollama";
    public const string DeepSeek = "deepseek";
    public const string OpenRouter = "openrouter";
    public const string Qwen = "qwen";
    public const string Mistral = "mistral";
    public const string Grok = "grok";

    /// <summary>Providers that speak the OpenAI chat-completions wire format.</summary>
    public static IReadOnlySet<string> OpenAiCompatible { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        OpenAi,
        Ollama,
        DeepSeek,
        OpenRouter,
        Qwen,
        Mistral,
        Grok,
    };
}
