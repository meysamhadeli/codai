using Codai.Configuration;
using FluentAssertions;

namespace Codai.Core.Tests;

public class ProviderDefaultsTests
{
    [Theory]
    [InlineData("openai")]
    [InlineData("azure-openai")]
    [InlineData("anthropic")]
    [InlineData("gemini")]
    [InlineData("ollama")]
    [InlineData("deepseek")]
    [InlineData("openrouter")]
    [InlineData("qwen")]
    [InlineData("mistral")]
    [InlineData("grok")]
    public void Every_supported_provider_has_a_base_url_and_model(string provider)
    {
        ProviderDefaults.GetDefaultBaseUrl(provider).Should().NotBeNullOrWhiteSpace();
        ProviderDefaults.GetDefaultModel(provider).Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void DeepSeek_defaults_to_the_flash_model()
    {
        ProviderDefaults.GetDefaultModel("deepseek").Should().Be("deepseek-flash");
        ProviderDefaults.ResolveModel("deepseek", null).Should().Be("deepseek-flash");
        ProviderDefaults.ResolveModel("deepseek", "   ").Should().Be("deepseek-flash");
    }

    [Fact]
    public void Explicit_model_wins_over_the_provider_default()
    {
        ProviderDefaults.ResolveModel("deepseek", "deepseek-reasoner").Should().Be("deepseek-reasoner");
        ProviderDefaults.ResolveModel("deepseek", "  deepseek-chat  ").Should().Be("deepseek-chat");
    }

    [Fact]
    public void OpenAi_defaults_to_gpt_4o()
    {
        ProviderDefaults.ResolveModel("openai", null).Should().Be("gpt-4o");
    }

    [Fact]
    public void Explicit_base_url_wins_over_the_default()
    {
        ProviderDefaults.ResolveBaseUrl("openai", "https://custom/v1")
            .Should().Be("https://custom/v1");
    }

    [Fact]
    public void IsSupported_is_case_insensitive()
    {
        ProviderDefaults.IsSupported("DeepSeek").Should().BeTrue();
        ProviderDefaults.IsSupported("nope").Should().BeFalse();
        ProviderDefaults.IsSupported(null).Should().BeFalse();
    }

    [Fact]
    public void Unknown_provider_throws()
    {
        var act = () => ProviderDefaults.GetDefaultModel("nope");

        act.Should().Throw<NotSupportedException>();
    }
}
