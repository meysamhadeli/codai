using Codai.Core.Tokens;
using Codai.Pricing;
using FluentAssertions;

namespace Codai.Core.Tests;

public class PricingCatalogTests
{
    private readonly EmbeddedPricingCatalog _catalog = EmbeddedPricingCatalog.Instance;

    [Fact]
    public void Finds_a_known_openai_model()
    {
        var pricing = _catalog.Find("openai", "gpt-4o");

        pricing.Should().NotBeNull();
        pricing!.InputCostPerToken.Should().BeGreaterThan(0);
        pricing.OutputCostPerToken.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Is_case_insensitive()
    {
        _catalog.Find("OpenAI", "GPT-4O").Should().NotBeNull();
    }

    [Fact]
    public void Resolves_azure_deployments_via_model_fallback()
    {
        _catalog.Find("azure-openai", "gpt-4o").Should().NotBeNull();
    }

    [Fact]
    public void Returns_null_for_unknown_model()
    {
        _catalog.Find("openai", "definitely-not-a-model").Should().BeNull();
    }

    [Fact]
    public void Returns_zero_cost_for_unknown_model()
    {
        _catalog.Calculate("openai", "definitely-not-a-model", new TokenUsage(1000, 500))
            .Should().Be(0m);
    }

    [Fact]
    public void Calculates_cost_from_input_and_output_rates()
    {
        var pricing = _catalog.Find("openai", "gpt-4o")!;

        var expected = (decimal)((1000 * pricing.InputCostPerToken) + (500 * pricing.OutputCostPerToken));

        _catalog.Calculate("openai", "gpt-4o", new TokenUsage(1000, 500))
            .Should().Be(expected);
    }

    [Fact]
    public void Costs_nothing_for_zero_usage()
    {
        _catalog.Calculate("openai", "gpt-4o", TokenUsage.Empty).Should().Be(0m);
    }

    [Fact]
    public void Knows_the_deepseek_flash_model()
    {
        var pricing = _catalog.Find("deepseek", "deepseek-flash");

        pricing.Should().NotBeNull(
            "deepseek-flash is the DeepSeek default model and must have pricing");

        // Official DeepSeek rates: $0.30 peak / $0.15 off-peak per 1M input tokens,
        // $1.20 peak / $0.60 off-peak per 1M output tokens. The peak rate is used.
        pricing!.InputCostPerToken.Should().Be(3e-7);
        pricing.OutputCostPerToken.Should().Be(1.2e-6);
    }

    [Fact]
    public void Calculates_a_non_zero_cost_for_deepseek_flash()
    {
        var cost = _catalog.Calculate("deepseek", "deepseek-flash", new TokenUsage(9976, 512));

        cost.Should().BeGreaterThan(0m);
        cost.Should().Be((decimal)((9976 * 3e-7) + (512 * 1.2e-6)));
    }

    [Fact]
    public void Resolves_deepseek_flash_for_the_deepseek_provider_prefix()
    {
        _catalog.Find("deepseek", "deepseek/deepseek-flash").Should().NotBeNull();
    }
}
