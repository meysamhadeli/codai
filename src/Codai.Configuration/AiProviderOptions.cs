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

    [ConfigurationKeyName("base_url")]
    public string? BaseUrl { get; set; }

    [ConfigurationKeyName("model")]
    public string Model { get; set; } = string.Empty;

    [ConfigurationKeyName("stream")]
    public bool Stream { get; set; } = true;

    [ConfigurationKeyName("temperature")]
    public float? Temperature { get; set; }

    [ConfigurationKeyName("reasoning_effort")]
    public string? ReasoningEffort { get; set; }

    [ConfigurationKeyName("encoding_format")]
    public string EncodingFormat { get; set; } = "float";

    [ConfigurationKeyName("max_tokens")]
    public int MaxTokens { get; set; }

    [ConfigurationKeyName("api_key")]
    public string? ApiKey { get; set; }

    [ConfigurationKeyName("api_version")]
    public string? ApiVersion { get; set; }
}
