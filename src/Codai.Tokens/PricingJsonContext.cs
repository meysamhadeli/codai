using System.Text.Json;
using System.Text.Json.Serialization;

namespace Codai.Tokens;

/// <summary>
/// Source-generated serialization context so the pricing data can be read under
/// trimming and Native AOT.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(PricingDocument))]
[JsonSerializable(typeof(ModelPricing))]
internal sealed partial class PricingJsonContext : JsonSerializerContext;
