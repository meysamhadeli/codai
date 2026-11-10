using System.Text.Json;
using Codai.Configuration;
using Codai.Core.Tokens;
using Codai.Pricing;
using Codai.Providers;
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

    [Theory]
    [InlineData("ollama")]
    [InlineData("deepseek")]
    [InlineData("openrouter")]
    [InlineData("custom-provider")]
    public void Does_not_reuse_another_providers_unqualified_model_price(string provider)
    {
        _catalog.Find(provider, "gpt-4o").Should().BeNull();
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

    [Theory]
    [InlineData("openai", "gpt-5.4-mini", 0.75, 4.50)]
    [InlineData("azure-openai", "gpt-5.4-mini", 0.75, 4.50)]
    [InlineData("anthropic", "claude-sonnet-4-6", 3, 15)]
    [InlineData("anthropic", "claude-fable-5-2", 8, 40)]
    [InlineData("gemini", "gemini-3.8-flash", 0.75, 3.75)]
    [InlineData("gemini", "gemini-4-carbon", 2, 10)]
    [InlineData("ollama", "qwen3-coder", 0, 0)]
    [InlineData("deepseek", "deepseek-flash", 0.30, 1.20)]
    [InlineData("openrouter", "openai/gpt-5.4-mini", 0.75, 4.50)]
    [InlineData("qwen", "qwen3.7-plus", 0.40, 1.60)]
    [InlineData("mistral", "mistral-medium-3-5", 1.50, 7.50)]
    [InlineData("grok", "grok-4.7", 2, 6)]
    [InlineData("openai", "o3", 2, 8)]
    [InlineData("azure-openai", "o3", 2, 8)]
    [InlineData("mistral", "mistral-large-latest", 0.50, 1.50)]
    [InlineData("mistral", "mistral-large-4", 0.70, 2.10)]
    public void Uses_reviewed_default_model_rates(string provider, string model, double inputPerMillion, double outputPerMillion)
    {
        var pricing = _catalog.Find(provider, model);
        pricing.Should().NotBeNull();
        ((decimal)pricing!.InputCostPerToken * 1_000_000m).Should().Be((decimal)inputPerMillion);
        ((decimal)pricing.OutputCostPerToken * 1_000_000m).Should().Be((decimal)outputPerMillion);
    }

    [Fact]
    public void Every_bundled_provider_model_has_reviewed_pricing()
    {
        using var stream = typeof(EmbeddedPricingCatalog).Assembly.GetManifestResourceStream("Codai.Pricing.Data.model_pricing.json")!;
        using var document = JsonDocument.Parse(stream);
        var current = document.RootElement.GetProperty("current_models");
        foreach (var provider in ProviderCatalog.All)
        {
            foreach (var model in provider.SuggestedModels.Prepend(provider.DefaultModel))
            {
                var prefix = provider.Name == ProviderCatalog.AzureOpenAi ? "azure" : provider.Name;
                current.TryGetProperty(prefix + "/" + model, out _).Should().BeTrue($"{provider.Name}/{model} needs a reviewed rate");
                _catalog.Find(provider.Name, model).Should().NotBeNull();
            }
        }
    }

    [Fact]
    public void Every_bundled_hosted_model_has_explicit_context_limits_used_by_compaction()
    {
        foreach (var provider in ProviderCatalog.All.Where(provider => provider.Name != ProviderCatalog.Ollama))
        {
            foreach (var model in provider.SuggestedModels.Prepend(provider.DefaultModel))
            {
                var pricing = _catalog.Find(provider.Name, model);
                pricing.Should().NotBeNull($"{provider.Name}/{model} needs model metadata");
                pricing!.MaxContextTokens.Should().BePositive($"{provider.Name}/{model} needs an explicit context window");
                pricing.MaxInputTokens.Should().BeGreaterThanOrEqualTo(0);
                pricing.MaxOutputTokens.Should().BeGreaterThanOrEqualTo(0);
                var resolved = ResolvedProviderOptions.From(new AiProviderOptions { Provider = provider.Name, Model = model });
                resolved.ContextWindowTokens.Should().Be(pricing.MaxInputTokens > 0
                    ? Math.Min(pricing.MaxInputTokens, pricing.MaxContextTokens) : pricing.MaxContextTokens);
            }
        }
    }

    [Theory]
    [InlineData("openai")]
    [InlineData("azure-openai")]
    public void Compaction_uses_input_ceiling_not_larger_combined_context(string provider)
    {
        var pricing = _catalog.Find(provider, "gpt-5.4-mini")!;
        pricing.MaxContextTokens.Should().Be(400_000);
        pricing.MaxInputTokens.Should().Be(272_000);
        pricing.MaxOutputTokens.Should().Be(128_000);
        var resolved = ResolvedProviderOptions.From(new AiProviderOptions { Provider = provider, Model = "gpt-5.4-mini" });
        resolved.ContextWindowTokens.Should().Be(272_000);
        resolved.OutputReserveTokens.Should().Be(20_000);
    }

    [Fact]
    public void Reviewed_context_limits_have_separate_provenance_and_do_not_change_rate_review_date()
    {
        using var stream = typeof(EmbeddedPricingCatalog).Assembly.GetManifestResourceStream("Codai.Pricing.Data.model_pricing.json")!;
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        root.GetProperty("context_reviewed_at").GetString().Should().Be("2026-11-10");
        root.GetProperty("reviewed_at").GetString().Should().Be("2026-10-30");
        var entries = root.GetProperty("context_limits").EnumerateObject().ToArray();
        entries.Select(entry => entry.Name).Should().OnlyHaveUniqueItems();
        foreach (var entry in entries)
        {
            root.GetProperty("current_models").TryGetProperty(entry.Name, out _).Should().BeTrue();
            var separator = entry.Name.IndexOf('/', StringComparison.Ordinal);
            var source = root.GetProperty("context_sources").GetProperty(entry.Name[..separator]).GetString();
            Uri.TryCreate(source, UriKind.Absolute, out var address).Should().BeTrue();
            address!.Scheme.Should().Be("https");
            var context = entry.Value.GetProperty("max_context_tokens").GetInt32();
            context.Should().BePositive();
            entry.Value.GetProperty("max_input_tokens").GetInt32().Should().BeInRange(0, context);
            entry.Value.GetProperty("max_output_tokens").GetInt32().Should().BeInRange(0, context);
        }
    }

    [Theory]
    [InlineData("anthropic", "claude-sonnet-4-6", 1000000, 20000)]
    [InlineData("gemini", "gemini-4-carbon", 1048576, 20000)]
    [InlineData("deepseek", "deepseek-flash", 1000000, 20000)]
    [InlineData("qwen", "qwen3.7-plus", 1000000, 20000)]
    [InlineData("mistral", "mistral-large-4", 1000000, 4096)]
    [InlineData("grok", "grok-4.7", 500000, 4096)]
    [InlineData("openrouter", "openai/gpt-5.4-mini", 400000, 20000)]
    [InlineData("openrouter", "x-ai/grok-4.7", 500000, 20000)]
    public void Active_model_metadata_controls_runtime_window_and_bounded_output_headroom(string provider, string model, int window, int reserve)
    {
        var resolved = ResolvedProviderOptions.From(new AiProviderOptions { Provider = provider, Model = model });
        resolved.ContextWindowTokens.Should().Be(window);
        resolved.OutputReserveTokens.Should().Be(reserve);
    }

    [Fact]
    public void Native_and_routed_output_limits_are_not_interchangeable()
    {
        _catalog.Find("gemini", "gemini-4-argon")!.MaxOutputTokens.Should().Be(65_536);
        _catalog.Find("openrouter", "google/gemini-4-argon")!.MaxOutputTokens.Should().Be(943_718);
        _catalog.Find("grok", "grok-4.7")!.MaxOutputTokens.Should().Be(0);
        _catalog.Find("openrouter", "x-ai/grok-4.7")!.MaxOutputTokens.Should().Be(450_000);
    }

    [Fact]
    public void Current_suggestions_keep_saved_legacy_models_usable_without_recommending_them()
    {
        ProviderCatalog.Get("anthropic").SuggestedModels.Should().Contain("claude-fable-5-2");
        ProviderCatalog.Get("gemini").SuggestedModels.Should().Contain("gemini-4-carbon").And.NotContain("gemini-2.5-pro");
        ProviderCatalog.Get("mistral").DefaultModel.Should().Be("mistral-large-4");
        var saved = ResolvedProviderOptions.From(new AiProviderOptions { Provider = "mistral", Model = "mistral-medium-3-5" });
        saved.Model.Should().Be("mistral-medium-3-5");
        _catalog.Find("gemini", "gemini-2.5-pro").Should().NotBeNull();
        _catalog.Find("qwen", "qwen3-coder-plus").Should().NotBeNull();
    }

    [Fact]
    public void Reviewed_rates_have_provenance_and_valid_unique_tiers()
    {
        using var stream = typeof(EmbeddedPricingCatalog).Assembly.GetManifestResourceStream("Codai.Pricing.Data.model_pricing.json")!;
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        root.GetProperty("reviewed_at").GetString().Should().Be("2026-10-30");
        root.GetProperty("currency").GetString().Should().Be("USD");
        var entries = root.GetProperty("current_models").EnumerateObject().ToArray();
        entries.Select(entry => entry.Name).Should().OnlyHaveUniqueItems();
        foreach (var entry in entries)
        {
            var separator = entry.Name.IndexOf('/', StringComparison.Ordinal);
            separator.Should().BeGreaterThan(0);
            var provider = entry.Name[..separator];
            var source = root.GetProperty("sources").GetProperty(provider).GetString();
            Uri.TryCreate(source, UriKind.Absolute, out var address).Should().BeTrue();
            address!.Scheme.Should().Be("https");
            var pricing = _catalog.Find(provider, entry.Name[(separator + 1)..])!;
            pricing.InputCostPerToken.Should().BeGreaterThanOrEqualTo(0);
            pricing.OutputCostPerToken.Should().BeGreaterThanOrEqualTo(0);
            pricing.InputTokenTiers.Select(tier => tier.MinInputTokens).Should().OnlyHaveUniqueItems().And.BeInAscendingOrder();
            foreach (var tier in pricing.InputTokenTiers)
            {
                tier.MinInputTokens.Should().BePositive();
                tier.InputCostPerToken.Should().BeGreaterThan(0);
                tier.OutputCostPerToken.Should().BeGreaterThan(0);
            }
        }
    }

    [Theory]
    [InlineData("openai", "gpt-5.4", 272000, 2.50, 15)]
    [InlineData("openai", "gpt-5.4", 272001, 5, 22.50)]
    [InlineData("anthropic", "claude-haiku-5-5", 100000, 0.10, 0.50)]
    [InlineData("anthropic", "claude-haiku-5-5", 100001, 0.50, 2.50)]
    [InlineData("gemini", "gemini-2.5-pro", 200000, 1.25, 10)]
    [InlineData("gemini", "gemini-2.5-pro", 200001, 2.50, 15)]
    [InlineData("qwen", "qwen3-coder-plus", 32000, 1, 5)]
    [InlineData("qwen", "qwen3-coder-plus", 32001, 1.80, 9)]
    [InlineData("qwen", "qwen3-coder-plus", 128000, 1.80, 9)]
    [InlineData("qwen", "qwen3-coder-plus", 128001, 3, 15)]
    [InlineData("qwen", "qwen3-coder-plus", 256000, 3, 15)]
    [InlineData("qwen", "qwen3-coder-plus", 256001, 6, 60)]
    [InlineData("openrouter", "anthropic/claude-haiku-5.5", 100000, 0.50, 2.50)]
    [InlineData("openrouter", "openai/gpt-6.1-sol", 272000, 4, 15)]
    [InlineData("openrouter", "google/gemini-3.8-flash", 1000, 0.825, 4.125)]
    public void Prices_entire_requests_using_provider_specific_tiers(string provider, string model, int input, double inputPerMillion, double outputPerMillion)
    {
        var expected = (input * (decimal)inputPerMillion / 1_000_000m) + (1000 * (decimal)outputPerMillion / 1_000_000m);
        _catalog.Calculate(provider, model, new TokenUsage(input, 1000)).Should().BeApproximately(expected, 0.000000000001m);
    }

    [Fact]
    public void Azure_custom_deployment_and_cloud_ollama_prices_remain_unknown()
    {
        _catalog.Find("azure-openai", "production-chat-deployment").Should().BeNull();
        _catalog.Find("ollama", "qwen3-coder:cloud").Should().BeNull();
    }

    [Fact]
    public void Rate_overrides_preserve_historical_context_and_capability_metadata()
    {
        using var stream = typeof(EmbeddedPricingCatalog).Assembly.GetManifestResourceStream("Codai.Pricing.Data.model_pricing.json")!;
        using var document = JsonDocument.Parse(stream);
        var historical = document.RootElement.GetProperty("models").GetProperty("gpt-4o");
        var pricing = _catalog.Find("openai", "gpt-4o")!;
        pricing.MaxInputTokens.Should().Be(historical.GetProperty("max_input_tokens").GetInt32());
        pricing.MaxOutputTokens.Should().Be(historical.GetProperty("max_output_tokens").GetInt32());
        pricing.MaxTokens.Should().Be(historical.GetProperty("max_tokens").GetInt32());
        pricing.SupportsFunctionCalling.Should().Be(historical.GetProperty("supports_function_calling").GetBoolean());
        pricing.Mode.Should().Be(historical.GetProperty("mode").GetString());
        _catalog.Find("deepseek", "deepseek-flash")!.MaxInputTokens.Should().Be(1_000_000);
    }

    [Theory]
    [InlineData(199999, 2, 6)]
    [InlineData(200000, 4, 12)]
    [InlineData(200001, 4, 12)]
    public void Grok_long_context_prices_all_tokens_at_the_threshold(int input, double inputPerMillion, double outputPerMillion)
    {
        var expected = (decimal)((input * (inputPerMillion / 1_000_000)) + (1000 * (outputPerMillion / 1_000_000)));
        _catalog.Calculate("grok", "grok-4.7", new TokenUsage(input, 1000)).Should().Be(expected);
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
