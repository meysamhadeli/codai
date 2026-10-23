using System.Text.Json.Serialization;

namespace Codai.Pricing;

/// <summary>
/// Per-model pricing and limits, mirroring the LiteLLM-style data file.
/// </summary>
public sealed class ModelPricing
{
    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; set; }

    [JsonPropertyName("max_input_tokens")]
    public int MaxInputTokens { get; set; }

    [JsonPropertyName("max_output_tokens")]
    public int MaxOutputTokens { get; set; }

    [JsonPropertyName("input_cost_per_token")]
    public double InputCostPerToken { get; set; }

    [JsonPropertyName("output_cost_per_token")]
    public double OutputCostPerToken { get; set; }

    [JsonPropertyName("cache_read_input_token_cost")]
    public double CacheReadInputTokenCost { get; set; }

    [JsonPropertyName("mode")]
    public string? Mode { get; set; }

    [JsonPropertyName("supports_function_calling")]
    public bool SupportsFunctionCalling { get; set; }
}
