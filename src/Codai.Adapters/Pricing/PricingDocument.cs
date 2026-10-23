using System.Text.Json.Serialization;

namespace Codai.Pricing;

/// <summary>
/// Root object of <c>model_pricing.json</c>.
/// </summary>
public sealed class PricingDocument
{
    [JsonPropertyName("models")]
    public Dictionary<string, ModelPricing> Models { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
