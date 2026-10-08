using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Codai.Core.Abstractions;
using Codai.Providers;
using FluentAssertions;

namespace Codai.Providers.Tests;

public class ProviderNamesTests
{
    [Fact]
    public void OpenAiCompatible_set_contains_expected_providers()
    {
        ProviderNames.OpenAiCompatible.Should().BeEquivalentTo(
            ["openai", "ollama", "deepseek", "openrouter", "qwen", "mistral", "grok"]);
    }

    [Fact]
    public void OpenAiCompatible_is_case_insensitive()
    {
        ProviderNames.OpenAiCompatible.Contains("DeepSeek").Should().BeTrue();
    }
}

public class ResolvedProviderOptionsTests
{
    private static Configuration.AiProviderOptions Options(
        string provider,
        string? baseUrl = null,
        string? apiKey = null,
        string model = "gpt-4o") => new()
        {
            Provider = provider,
            Model = model,
            BaseUrl = baseUrl,
            ApiKey = apiKey,
        };

    [Fact]
    public void Applies_provider_default_base_url_when_missing()
    {
        var resolved = ResolvedProviderOptions.From(Options("anthropic"));

        resolved.BaseUrl.Should().Be("https://api.anthropic.com/v1");
    }

    [Fact]
    public void Applies_default_base_url_for_providers_that_previously_ignored_it()
    {
        // Regression guard for the original bug: these providers dropped their defaults.
        ResolvedProviderOptions.From(Options("ollama")).BaseUrl.Should().Be("http://localhost:11434/api");
        ResolvedProviderOptions.From(Options("gemini")).BaseUrl.Should().Be("https://generativelanguage.googleapis.com/v1beta");
        ResolvedProviderOptions.From(Options("deepseek")).BaseUrl.Should().Be("https://api.deepseek.com");
    }

    [Fact]
    public void Honours_explicit_base_url()
    {
        var resolved = ResolvedProviderOptions.From(
            Options("openai", baseUrl: "https://custom.example/v1"));

        resolved.BaseUrl.Should().Be("https://custom.example/v1");
    }

    [Fact]
    public void Trims_provider_and_model()
    {
        var resolved = ResolvedProviderOptions.From(Options("  openai  ", model: " gpt-4o "));

        resolved.Provider.Should().Be("openai");
        resolved.Model.Should().Be("gpt-4o");
    }

    [Fact]
    public void Ollama_does_not_require_api_key()
    {
        ResolvedProviderOptions.From(Options("ollama")).RequiresApiKey.Should().BeFalse();
    }

    [Theory]
    [InlineData("openai")]
    [InlineData("deepseek")]
    [InlineData("anthropic")]
    public void Remote_providers_require_api_key(string provider)
    {
        ResolvedProviderOptions.From(Options(provider)).RequiresApiKey.Should().BeTrue();
    }

    [Fact]
    public void EnsureUsable_throws_when_remote_provider_has_no_key()
    {
        var resolved = ResolvedProviderOptions.From(Options("deepseek"));

        var act = resolved.EnsureUsable;

        act.Should().Throw<ProviderConfigurationException>()
            .WithMessage("*requires an API key*");
    }

    [Fact]
    public void EnsureUsable_allows_ollama_without_key()
    {
        var act = ResolvedProviderOptions.From(Options("ollama")).EnsureUsable;

        act.Should().NotThrow();
    }
}
