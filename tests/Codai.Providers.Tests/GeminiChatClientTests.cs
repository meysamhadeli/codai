using Codai.Providers.Gemini;
using FluentAssertions;

namespace Codai.Providers.Tests;

public class GeminiChatClientTests
{
    [Fact]
    public void Parses_text_and_usage_metadata()
    {
        const string payload = """
            {
              "candidates": [
                { "content": { "parts": [ { "text": "Hello " }, { "text": "world" } ] } }
              ],
              "usageMetadata": { "promptTokenCount": 11, "candidatesTokenCount": 4, "totalTokenCount": 15 }
            }
            """;

        var (text, usage) = GeminiChatClient.ParseGenerateContent(payload);

        text.Should().Be("Hello world");
        usage.InputTokens.Should().Be(11);
        usage.OutputTokens.Should().Be(4);
        usage.TotalTokens.Should().Be(15);
    }

    [Fact]
    public void Returns_empty_text_when_no_candidates()
    {
        const string payload = """{ "candidates": [] }""";

        var (text, usage) = GeminiChatClient.ParseGenerateContent(payload);

        text.Should().BeEmpty();
        usage.Should().Be(Codai.Core.Tokens.TokenUsage.Empty);
    }

    [Fact]
    public void Handles_missing_usage_metadata()
    {
        const string payload = """{ "candidates": [ { "content": { "parts": [ { "text": "hi" } ] } } ] }""";

        var (text, usage) = GeminiChatClient.ParseGenerateContent(payload);

        text.Should().Be("hi");
        usage.Should().Be(Codai.Core.Tokens.TokenUsage.Empty);
    }
}
