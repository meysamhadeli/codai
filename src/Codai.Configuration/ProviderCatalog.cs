namespace Codai.Configuration;

/// <summary>
/// The single source of truth for the providers Codai supports.
/// </summary>
/// <remarks>
/// Base URL, default model and wire format used to live in three separate places
/// (a base-URL table, a default-model table and a hard-coded if-chain in the provider
/// factory), which made it easy to add a provider in one place and forget another.
/// They now all come from <see cref="All"/>.
/// </remarks>
public static class ProviderCatalog
{
    /// <summary>OpenAI.</summary>
    public const string OpenAi = "openai";

    /// <summary>Azure OpenAI.</summary>
    public const string AzureOpenAi = "azure-openai";

    /// <summary>Anthropic.</summary>
    public const string Anthropic = "anthropic";

    /// <summary>Google Gemini.</summary>
    public const string Gemini = "gemini";

    /// <summary>Ollama (local).</summary>
    public const string Ollama = "ollama";

    /// <summary>DeepSeek.</summary>
    public const string DeepSeek = "deepseek";

    /// <summary>OpenRouter.</summary>
    public const string OpenRouter = "openrouter";

    /// <summary>Qwen (DashScope compatible mode).</summary>
    public const string Qwen = "qwen";

    /// <summary>Mistral.</summary>
    public const string Mistral = "mistral";

    /// <summary>Grok (xAI).</summary>
    public const string Grok = "grok";

    /// <summary>Every supported provider, in documentation order.</summary>
    public static IReadOnlyList<ProviderDescriptor> All { get; } =
    [
        new(OpenAi, "https://api.openai.com/v1", "gpt-4o", ProviderWireFormat.OpenAiCompatible),
        new(AzureOpenAi, "https://{your-resource}.openai.azure.com", "gpt-4o", ProviderWireFormat.AzureOpenAi),
        new(Anthropic, "https://api.anthropic.com/v1", "claude-3-5-sonnet-latest", ProviderWireFormat.Anthropic),
        new(Gemini, "https://generativelanguage.googleapis.com/v1beta", "gemini-1.5-pro", ProviderWireFormat.Gemini),
        new(Ollama, "http://localhost:11434/api", "llama3.1", ProviderWireFormat.OpenAiCompatible, RequiresApiKey: false),
        new(DeepSeek, "https://api.deepseek.com", "deepseek-flash", ProviderWireFormat.OpenAiCompatible),
        new(OpenRouter, "https://openrouter.ai/api/v1", "openai/gpt-4o", ProviderWireFormat.OpenAiCompatible),
        new(Qwen, "https://dashscope.aliyuncs.com/compatible-mode/v1", "qwen-plus", ProviderWireFormat.OpenAiCompatible),
        new(Mistral, "https://api.mistral.ai/v1", "mistral-large-latest", ProviderWireFormat.OpenAiCompatible),
        new(Grok, "https://api.x.ai/v1", "grok-2-latest", ProviderWireFormat.OpenAiCompatible),
    ];

    /// <summary>Every provider identifier, for help text and error messages.</summary>
    public static IReadOnlyCollection<string> SupportedNames =>
        All.Select(descriptor => descriptor.Name).ToArray();

    /// <summary>
    /// Looks up a provider by identifier. Comparison is case-insensitive and surrounded
    /// whitespace is ignored, so <c>" DeepSeek "</c> resolves.
    /// </summary>
    public static bool TryGet(string? name, out ProviderDescriptor descriptor)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            var trimmed = name.Trim();

            foreach (var candidate in All)
            {
                if (string.Equals(candidate.Name, trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    descriptor = candidate;
                    return true;
                }
            }
        }

        descriptor = null!;
        return false;
    }

    /// <summary>Looks up a provider, throwing when it is not supported.</summary>
    public static ProviderDescriptor Get(string provider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);

        return TryGet(provider, out var descriptor)
            ? descriptor
            : throw new NotSupportedException(
                $"Unsupported provider '{provider}'. Supported: {string.Join(", ", SupportedNames)}.");
    }
}
