using Codai.Core.Context;
using FluentAssertions;

namespace Codai.Core.Tests;

/// <summary>
/// Covers the model-to-window table and how an unknown model is treated.
/// </summary>
public class ContextWindowTests
{
    [Theory]
    [InlineData("gpt-4.1", 1_000_000)]
    [InlineData("gpt-4o", 128_000)]
    [InlineData("gpt-4-turbo", 128_000)]
    [InlineData("gpt-4", 8_192)]
    [InlineData("gpt-3.5-turbo", 16_000)]
    [InlineData("o3-mini", 200_000)]
    [InlineData("claude-3-5-sonnet-20241022", 200_000)]
    [InlineData("gemini-1.5-pro", 2_000_000)]
    [InlineData("gemini-2.0-flash", 1_000_000)]
    [InlineData("deepseek-chat", 128_000)]
    [InlineData("mistral-large-latest", 128_000)]
    [InlineData("grok-2", 131_072)]
    [InlineData("qwen2.5-coder", 131_072)]
    [InlineData("llama3.1-70b", 131_072)]
    [InlineData("llama2", 8_192)]
    [InlineData("phi-3", 128_000)]
    [InlineData("command-r-plus", 128_000)]
    public void Knows_the_window_of_the_supported_models(string model, int expected) =>
        ContextWindow.For(model).Should().Be(expected);

    [Theory]
    [InlineData("openai/gpt-4o", 128_000)]
    [InlineData("deepseek/deepseek-chat", 128_000)]
    [InlineData("models/gemini-1.5-flash", 1_000_000)]
    [InlineData("openrouter/anthropic/claude-3-5-sonnet", 200_000)]
    public void Matches_a_model_name_qualified_by_a_provider(string model, int expected) =>
        ContextWindow.For(model).Should().Be(expected);

    [Fact]
    public void The_longest_matching_prefix_wins()
    {
        // "gpt-4-turbo" must not be answered with the "gpt-4" window, which is much smaller.
        ContextWindow.For("gpt-4-turbo-preview").Should().Be(128_000);
    }

    [Theory]
    [InlineData("some-model-nobody-has-heard-of")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Unknown_models_get_the_default_window(string? model) =>
        ContextWindow.For(model).Should().Be(ContextWindow.DefaultTokens);

    [Fact]
    public void Matching_ignores_case() =>
        ContextWindow.For("GPT-4O").Should().Be(128_000);

    [Fact]
    public void No_model_is_ever_bet_on_a_window_smaller_than_the_floor()
    {
        ContextWindow.DefaultTokens.Should().BeGreaterThan(ContextWindow.MinimumTokens);

        // "gpt-4" is the smallest window in the table and still above the floor.
        ContextWindow.For("gpt-4").Should().BeGreaterThanOrEqualTo(ContextWindow.MinimumTokens);
    }
}
