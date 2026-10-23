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

        var cost = (usage.InputTokens * pricing.InputCostPerToken) +
                   (usage.OutputTokens * pricing.OutputCostPerToken);

        return (decimal)cost;
    }

    /// <inheritdoc />
    public decimal Calculate(string provider, string model, TokenUsage usage) =>
        CalculateCost(provider, model, usage);

    private static IEnumerable<string> CandidateKeys(string provider, string model)
    {
        // Azure deployments are keyed as "azure/<model>" in the data file.
        if (provider.StartsWith("azure", StringComparison.Ordinal))
        {
            yield return $"azure/{model}";
        }

        yield return model;

        if (!string.IsNullOrEmpty(provider))
        {
            yield return $"{provider}/{model}";
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
                }
            }
        }

        return result;
    }
}
