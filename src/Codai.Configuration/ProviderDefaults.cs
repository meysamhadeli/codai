namespace Codai.Configuration;

/// <summary>
/// Default endpoints for every supported provider. Centralising these fixes the
/// original bug where several providers ignored their default base URL.
/// </summary>
public static class ProviderDefaults
{
    private static readonly Dictionary<string, string> BaseUrls = new(StringComparer.OrdinalIgnoreCase)
    {
        ["openai"] = "https://api.openai.com/v1",
        ["azure-openai"] = "https://{your-resource}.openai.azure.com",
        ["anthropic"] = "https://api.anthropic.com/v1",
        ["gemini"] = "https://generativelanguage.googleapis.com/v1beta",
        ["ollama"] = "http://localhost:11434/api",
        ["deepseek"] = "https://api.deepseek.com",
        ["openrouter"] = "https://openrouter.ai/api/v1",
        ["qwen"] = "https://dashscope.aliyuncs.com/compatible-mode/v1",
        ["mistral"] = "https://api.mistral.ai/v1",
        ["grok"] = "https://api.x.ai/v1",
    };

    /// <summary>
    /// Default model per provider. DeepSeek always uses <c>deepseek-flash</c> unless
    /// overridden explicitly.
    /// </summary>
    private static readonly Dictionary<string, string> DefaultModels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["openai"] = "gpt-4o",
        ["azure-openai"] = "gpt-4o",
        ["anthropic"] = "claude-3-5-sonnet-latest",
        ["gemini"] = "gemini-1.5-pro",
        ["ollama"] = "llama3.1",
        ["deepseek"] = "deepseek-flash",
        ["openrouter"] = "openai/gpt-4o",
        ["qwen"] = "qwen-plus",
        ["mistral"] = "mistral-large-latest",
        ["grok"] = "grok-2-latest",
    };

    /// <summary>All provider identifiers understood by the CLI.</summary>
    public static IReadOnlyCollection<string> SupportedProviders => BaseUrls.Keys;

    public static bool IsSupported(string? provider) =>
        !string.IsNullOrWhiteSpace(provider) && BaseUrls.ContainsKey(provider);

    public static string GetDefaultBaseUrl(string provider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);

        return BaseUrls.TryGetValue(provider, out var url)
            ? url
            : throw new NotSupportedException(
                $"Unsupported provider '{provider}'. Supported: {string.Join(", ", BaseUrls.Keys)}.");
    }

    /// <summary>
    /// Resolves the effective base URL: an explicit value wins, otherwise the
    /// provider default is used.
    /// </summary>
    public static string ResolveBaseUrl(string provider, string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        return GetDefaultBaseUrl(provider);
    }

    /// <summary>The default model for a provider.</summary>
    public static string GetDefaultModel(string provider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);

        return DefaultModels.TryGetValue(provider, out var model)
            ? model
            : throw new NotSupportedException(
                $"Unsupported provider '{provider}'. Supported: {string.Join(", ", DefaultModels.Keys)}.");
    }

    /// <summary>
    /// Resolves the effective model: an explicit value wins, otherwise the provider
    /// default applies (DeepSeek defaults to <c>deepseek-flash</c>).
    /// </summary>
    public static string ResolveModel(string provider, string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim();
        }

        return GetDefaultModel(provider);
    }
}
