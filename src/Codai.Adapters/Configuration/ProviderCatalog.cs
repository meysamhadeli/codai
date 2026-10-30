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
        new(OpenAi, "https://api.openai.com/v1", "gpt-5.4-mini", ProviderWireFormat.OpenAiCompatible)
        {
            SuggestedModels = ["gpt-6-astra", "gpt-6.1-sol", "gpt-6.1-terra", "gpt-6-luna", "gpt-5.5", "gpt-5.4", "gpt-5.4-nano", "gpt-5.2", "gpt-5.1", "gpt-5", "gpt-5-mini", "gpt-5-nano", "gpt-4.1", "gpt-4.1-mini", "gpt-4.1-nano"],
        },
        new(AzureOpenAi, "https://{your-resource}.openai.azure.com", "gpt-5.4-mini", ProviderWireFormat.AzureOpenAi),
        new(Anthropic, "https://api.anthropic.com/v1", "claude-sonnet-4-6", ProviderWireFormat.Anthropic)
        {
            SuggestedModels = ["claude-sonnet-5-5", "claude-opus-5-5", "claude-haiku-5-5", "claude-fable-5-1", "claude-opus-5", "claude-sonnet-5", "claude-fable-5", "claude-opus-4-8", "claude-opus-4-7", "claude-opus-4-6", "claude-opus-4-5", "claude-haiku-4-5"],
        },
        new(Gemini, "https://generativelanguage.googleapis.com/v1beta", "gemini-3.8-flash", ProviderWireFormat.Gemini)
        {
            SuggestedModels = ["gemini-4-argon", "gemini-3.6-flash", "gemini-3.5-flash-lite", "gemini-3.1-flash-lite", "gemini-3.1-pro-preview", "gemini-3-flash-preview", "gemini-2.5-pro", "gemini-2.5-flash", "gemini-2.5-flash-lite"],
        },
        new(Ollama, "http://localhost:11434/api", "qwen3-coder", ProviderWireFormat.OpenAiCompatible, RequiresApiKey: false),
        new(DeepSeek, "https://api.deepseek.com", "deepseek-flash", ProviderWireFormat.OpenAiCompatible)
        {
            SuggestedModels = ["deepseek-v4-pro"],
        },
        new(OpenRouter, "https://openrouter.ai/api/v1", "openai/gpt-5.4-mini", ProviderWireFormat.OpenAiCompatible)
        {
            SuggestedModels = ["openai/gpt-6-astra", "openai/gpt-6.1-sol", "openai/gpt-6.1-terra", "openai/gpt-6-luna", "anthropic/claude-sonnet-5.5", "anthropic/claude-opus-5.5", "anthropic/claude-haiku-5.5", "anthropic/claude-fable-5.1", "google/gemini-4-argon", "google/gemini-3.8-flash", "deepseek/deepseek-v4.1-flash-1017", "deepseek/deepseek-v4-pro-0813", "qwen/qwen3.8-flash", "qwen/qwen3.7-plus", "mistralai/mistral-large-4-0", "x-ai/grok-4.7"],
        },
        new(Qwen, "https://dashscope.aliyuncs.com/compatible-mode/v1", "qwen3.7-plus", ProviderWireFormat.OpenAiCompatible)
        {
            SuggestedModels = ["qwen3.8-max", "qwen3.8-flash", "qwen3.8-omni-flash", "qwen3-coder-plus", "qwen3-coder-flash", "qwen-plus", "qwen-max", "qwen-flash"],
        },
        new(Mistral, "https://api.mistral.ai/v1", "mistral-medium-3-5", ProviderWireFormat.OpenAiCompatible)
        {
            SuggestedModels = ["mistral-large-4", "mistral-small-2603", "mistral-large-latest", "mistral-small-latest", "codestral-latest", "ministral-3b-2512", "ministral-8b-2512", "ministral-14b-2512"],
        },
        new(Grok, "https://api.x.ai/v1", "grok-4.7", ProviderWireFormat.OpenAiCompatible)
        {
            SuggestedModels = ["grok-4.6", "grok-4.5", "grok-4.3", "grok-4.20-0309-reasoning", "grok-4.20-0309-non-reasoning", "grok-build-0.1"],
        },
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
