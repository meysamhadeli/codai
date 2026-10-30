using System.Text.Json;
using Codai.Core.Tokens;
using Codai.Pricing.Abstractions;

namespace Codai.Pricing;

/// <summary>
/// <see cref="IPricingCatalog"/> backed by the embedded <c>model_pricing.json</c>.
/// </summary>
public sealed class EmbeddedPricingCatalog : IPricingCatalog
{
    private const string ResourceName = "Codai.Pricing.Data.model_pricing.json";

    private static readonly Lazy<Dictionary<string, ModelPricing>> Pricing = new(
        Load,
        LazyThreadSafetyMode.ExecutionAndPublication);

    public static EmbeddedPricingCatalog Instance { get; } = new();

    public ModelPricing? Find(string provider, string model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return null;
        }

        var normalizedModel = model.Trim().ToLowerInvariant();
        var normalizedProvider = provider?.Trim().ToLowerInvariant() ?? string.Empty;

        foreach (var key in CandidateKeys(normalizedProvider, normalizedModel))
        {
            if (Pricing.Value.TryGetValue(key, out var pricing))
            {
                return pricing;
            }
        }

        return null;
    }

    public decimal CalculateCost(string provider, string model, TokenUsage usage)
    {
        var pricing = Find(provider, model);
        if (pricing is null)
        {
            return 0m;
        }

        var tier = pricing.InputTokenTiers.Where(item => usage.InputTokens >= item.MinInputTokens)
            .MaxBy(item => item.MinInputTokens);
        var cost = (usage.InputTokens * (tier?.InputCostPerToken ?? pricing.InputCostPerToken)) +
                   (usage.OutputTokens * (tier?.OutputCostPerToken ?? pricing.OutputCostPerToken));

        return (decimal)cost;
    }

    /// <inheritdoc />
    public decimal Calculate(string provider, string model, TokenUsage usage) =>
        CalculateCost(provider, model, usage);

    private static IEnumerable<string> CandidateKeys(string provider, string model)
    {
        if (string.IsNullOrEmpty(provider))
        {
            yield return model;
            yield break;
        }

        var prefix = provider == "azure-openai" ? "azure" : provider;
        yield return $"{prefix}/{model}";
        if (model.StartsWith(prefix + "/", StringComparison.Ordinal))
        {
            yield return model;
        }
    }

    private static Dictionary<string, ModelPricing> Load()
    {
        var assembly = typeof(EmbeddedPricingCatalog).Assembly;

        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{ResourceName}' was not found. Available: {string.Join(", ", assembly.GetManifestResourceNames())}");

        using var document = JsonDocument.Parse(stream);

        var result = new Dictionary<string, ModelPricing>(StringComparer.OrdinalIgnoreCase);

        if (document.RootElement.TryGetProperty("models", out var models) &&
            models.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in models.EnumerateObject())
            {
                var pricing = property.Value.Deserialize(PricingJsonContext.Default.ModelPricing);
                if (pricing is not null)
                {
                    result[property.Name] = pricing;
                    if (!property.Name.Contains('/', StringComparison.Ordinal) &&
                        property.Value.TryGetProperty("litellm_provider", out var owner) && owner.ValueKind == JsonValueKind.String)
                    {
                        result.TryAdd($"{owner.GetString()}/{property.Name}", pricing);
                    }
                }
            }
        }

        if (document.RootElement.TryGetProperty("current_models", out var currentModels) &&
            currentModels.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in currentModels.EnumerateObject())
            {
                var pricing = property.Value.Deserialize(PricingJsonContext.Default.ModelPricing);
                if (pricing is not null)
                {
                    if (result.TryGetValue(property.Name, out var historical))
                    {
                        if (!property.Value.TryGetProperty("max_tokens", out _))
                        {
                            pricing.MaxTokens = historical.MaxTokens;
                        }

                        if (!property.Value.TryGetProperty("max_input_tokens", out _))
                        {
                            pricing.MaxInputTokens = historical.MaxInputTokens;
                        }

                        if (!property.Value.TryGetProperty("max_output_tokens", out _))
                        {
                            pricing.MaxOutputTokens = historical.MaxOutputTokens;
                        }

                        if (!property.Value.TryGetProperty("mode", out _))
                        {
                            pricing.Mode = historical.Mode;
                        }

                        if (!property.Value.TryGetProperty("supports_function_calling", out _))
                        {
                            pricing.SupportsFunctionCalling = historical.SupportsFunctionCalling;
                        }
                    }

                    result[property.Name] = pricing;
                }
            }
        }

        return result;
    }
}
