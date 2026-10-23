using Microsoft.Extensions.Configuration;

namespace Codai.Configuration;

/// <summary>
/// Provider/model settings. Key names match the original <c>codai-config.yml</c>
/// so existing configuration files keep working.
/// </summary>
public sealed class AiProviderOptions
{
    /// <summary>Provider identifier, e.g. <c>openai</c>, <c>azure-openai</c>, <c>anthropic</c>.</summary>
    [ConfigurationKeyName("provider")]
    public string Provider { get; set; } = CodaiDefaults.Provider;

    /// <summary>Endpoint override. The provider default applies when unset.</summary>
    [ConfigurationKeyName("base_url")]
    public string? BaseUrl { get; set; }

    /// <summary>Model override. The provider default applies when unset.</summary>
    [ConfigurationKeyName("model")]
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// When false the provider is asked for a single completion instead of a stream.
    /// </summary>
    [ConfigurationKeyName("stream")]
    public bool Stream { get; set; } = true;

    /// <summary>Sampling temperature, between 0 and 2.</summary>
    [ConfigurationKeyName("temperature")]
    public float? Temperature { get; set; }

    /// <summary>
    /// Reasoning effort for reasoning-capable models: <c>low</c>, <c>medium</c> or
    /// <c>high</c>. Unset means "use the provider default".
    /// </summary>
    [ConfigurationKeyName("reasoning_effort")]
    public ReasoningEffortLevel? ReasoningEffort { get; set; }

    /// <summary>Maximum number of tokens to generate. Unset means "provider default".</summary>
    [ConfigurationKeyName("max_tokens")]
    public int MaxTokens { get; set; }

    /// <summary>API key. Prefer the <c>API_KEY</c> environment variable over storing it here.</summary>
    [ConfigurationKeyName("api_key")]
    public string? ApiKey { get; set; }

    [ConfigurationKeyName("account")]
    public string? Account { get; set; }

    /// <summary>
    /// Version string for providers that need one. Used by Anthropic as the
    /// <c>anthropic-version</c> header.
    /// </summary>
    [ConfigurationKeyName("api_version")]
    public string? ApiVersion { get; set; }
}
