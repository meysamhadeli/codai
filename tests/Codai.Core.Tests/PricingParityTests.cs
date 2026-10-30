using System.Text.Json;
using Codai.Pricing;
using FluentAssertions;

namespace Codai.Core.Tests;

/// <summary>
/// Guards the relationship between the embedded pricing catalog and the original Go
/// snapshot (frozen into <c>Golden/model_pricing.json</c>). The snapshot is the
/// baseline: every model it defines must remain in the historical models section
/// with identical rates. Reviewed current_models override runtime prices separately.
/// Historical additions must be declared here so drift stays intentional.
/// </summary>
public class PricingParityTests
{
    /// <summary>
    /// Models added on top of the Go snapshot, with the reason. Anything else that
    /// differs is a bug.
    /// </summary>
    private static readonly Dictionary<string, string> ExpectedAdditions = new(StringComparer.Ordinal)
    {
        ["deepseek/deepseek-flash"] =
            "DeepSeek default model. The Go snapshot only lists deepseek-chat/deepseek-reasoner.",
    };

    [Fact]
    public void Historical_catalog_contains_every_model_from_the_go_snapshot_with_identical_rates()
    {
        // Frozen copy of the Go snapshot, embedded in this project so the assertion
        // survives removal of the Go sources; a missing golden is a hard failure.
        using var go = JsonDocument.Parse(GoldenFiles.ReadPricingCatalog());
        var goModels = go.RootElement.GetProperty("models");

        using var stream = typeof(EmbeddedPricingCatalog).Assembly.GetManifestResourceStream("Codai.Pricing.Data.model_pricing.json")!;
        using var catalog = JsonDocument.Parse(stream);
        var historicalModels = catalog.RootElement.GetProperty("models");

        var mismatches = new List<string>();

        foreach (var entry in goModels.EnumerateObject())
        {
            var key = entry.Name;

            // Some snapshot entries (audio, embeddings, ...) carry no token rates.
            if (!entry.Value.TryGetProperty("input_cost_per_token", out var inputElement) ||
                !entry.Value.TryGetProperty("output_cost_per_token", out var outputElement))
            {
                continue;
            }

            var inputCost = inputElement.GetDouble();
            var outputCost = outputElement.GetDouble();

            if (!historicalModels.TryGetProperty(key, out var pricing))
            {
                mismatches.Add($"missing model '{key}'");
                continue;
            }

            var actualInput = pricing.GetProperty("input_cost_per_token").GetDouble();
            var actualOutput = pricing.GetProperty("output_cost_per_token").GetDouble();
            if (Math.Abs(actualInput - inputCost) > double.Epsilon)
            {
                mismatches.Add($"{key}: input {actualInput} != {inputCost}");
            }

            if (Math.Abs(actualOutput - outputCost) > double.Epsilon)
            {
                mismatches.Add($"{key}: output {actualOutput} != {outputCost}");
            }
        }

        mismatches.Should().BeEmpty(
            "every Go pricing entry must survive the port unchanged");
    }

    [Fact]
    public void Only_the_declared_models_are_added_beyond_the_go_snapshot()
    {
        using var go = JsonDocument.Parse(GoldenFiles.ReadPricingCatalog());
        var goKeys = go.RootElement.GetProperty("models")
            .EnumerateObject()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        // Read the catalog straight from the assembly so the raw key set is visible.
        var assembly = typeof(EmbeddedPricingCatalog).Assembly;
        using var stream = assembly.GetManifestResourceStream("Codai.Pricing.Data.model_pricing.json")!;
        using var document = JsonDocument.Parse(stream);

        var catalogKeys = document.RootElement.GetProperty("models")
            .EnumerateObject()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        var added = catalogKeys.Except(goKeys, StringComparer.Ordinal).OrderBy(key => key).ToList();
        var removed = goKeys.Except(catalogKeys, StringComparer.Ordinal).OrderBy(key => key).ToList();

        removed.Should().BeEmpty("the port must not drop any Go pricing entry");

        added.Should().BeEquivalentTo(
            ExpectedAdditions.Keys,
            "added models must be declared in ExpectedAdditions so drift is intentional");
    }
}
