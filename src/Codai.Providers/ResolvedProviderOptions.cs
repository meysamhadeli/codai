using Codai.Configuration;

namespace Codai.Providers;

/// <summary>
/// Fully resolved provider settings: base URL defaults applied and validated.
/// </summary>
public sealed record ResolvedProviderOptions
{
    public required string Provider { get; init; }

    public required string Model { get; init; }

    public required string BaseUrl { get; init; }

    public string? ApiKey { get; init; }

    public string? ApiVersion { get; init; }

    public float? Temperature { get; init; }

    /// <summary>Reasoning effort for reasoning-capable models, when configured.</summary>
    public ReasoningEffortLevel? ReasoningEffort { get; init; }

    public int MaxTokens { get; init; }

    /// <summary>
    /// When false the provider is asked for a single (non-streamed) completion.
    /// The original Go implementation always streamed; this flag makes the
    /// documented <c>stream</c> setting actually take effect.
    /// </summary>
    public bool Stream { get; init; } = true;

    /// <summary>
    /// Builds the settings, applying the provider's default base URL when none was
    /// supplied (fixing the original bug where defaults were silently ignored).
    /// </summary>
    public static ResolvedProviderOptions From(AiProviderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var provider = options.Provider.Trim();

        return new ResolvedProviderOptions
        {
            Provider = provider,
            Model = ProviderDefaults.ResolveModel(provider, options.Model),
            BaseUrl = ProviderDefaults.ResolveBaseUrl(provider, options.BaseUrl),
            ApiKey = string.IsNullOrWhiteSpace(options.ApiKey) ? null : options.ApiKey,
            ApiVersion = string.IsNullOrWhiteSpace(options.ApiVersion) ? null : options.ApiVersion,
            Temperature = options.Temperature,
            ReasoningEffort = options.ReasoningEffort,
            MaxTokens = options.MaxTokens,
            Stream = options.Stream,
        };
    }

    /// <summary>Provider requires an API key to authenticate.</summary>
    public bool RequiresApiKey =>
        !ProviderCatalog.TryGet(Provider, out var descriptor) || descriptor.RequiresApiKey;

    public void EnsureUsable()
    {
        if (RequiresApiKey && string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new ProviderConfigurationException(
                $"Provider '{Provider}' requires an API key. Set it in codai-config.yml, " +
                "the API_KEY environment variable, or the --api_key flag.");
        }
    }
}
