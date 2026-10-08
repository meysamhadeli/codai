using Codai.Core.Tokens;
using Codai.Providers.Gemini;
using FluentAssertions;
using Microsoft.Extensions.AI;

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

        var result = GeminiChatClient.ParseGenerateContent(payload);

        result.Text.Should().Be("Hello world");
        result.Usage.InputTokens.Should().Be(11);
        result.Usage.OutputTokens.Should().Be(4);
        result.Usage.TotalTokens.Should().Be(15);
        result.ToolCalls.Should().BeEmpty();
    }

    [Fact]
    public void Returns_empty_text_when_no_candidates()
    {
        const string payload = """{ "candidates": [] }""";

        var result = GeminiChatClient.ParseGenerateContent(payload);

        result.Text.Should().BeEmpty();
        result.ToolCalls.Should().BeEmpty();
        result.Usage.Should().Be(TokenUsage.Empty);
    }

    [Fact]
    public void Handles_missing_usage_metadata()
    {
        const string payload = """{ "candidates": [ { "content": { "parts": [ { "text": "hi" } ] } } ] }""";

        var result = GeminiChatClient.ParseGenerateContent(payload);

        result.Text.Should().Be("hi");
        result.Usage.Should().Be(TokenUsage.Empty);
    }

    [Fact]
    public void Parses_a_function_call_part()
    {
        const string payload = """
            {
              "candidates": [
                {
                  "content": {
                    "role": "model",
                    "parts": [
                      { "functionCall": { "name": "grep", "args": { "pattern": "TODO", "case_sensitive": true } } }
                    ]
                  }
                }
              ],
              "usageMetadata": { "promptTokenCount": 7, "candidatesTokenCount": 3 }
            }
            """;

        var result = GeminiChatClient.ParseGenerateContent(payload);

        var call = result.ToolCalls.Should().ContainSingle().Subject;
        call.Name.Should().Be("grep");
        call.CallId.Should().NotBeNullOrEmpty();
        call.Arguments!["pattern"].Should().Be("TODO");
        call.Arguments["case_sensitive"].Should().Be(true);
    }

    [Fact]
    public void Parses_text_and_a_function_call_from_the_same_response()
    {
        const string payload = """
            {
              "candidates": [
                {
                  "content": {
                    "parts": [
                      { "text": "Let me look." },
                      { "functionCall": { "name": "grep", "args": { "pattern": "TODO" } } }
                    ]
                  }
                }
              ]
            }
            """;

        var result = GeminiChatClient.ParseGenerateContent(payload);

        result.Text.Should().Be("Let me look.");
        result.ToolCalls.Should().ContainSingle().Which.Name.Should().Be("grep");
    }

    [Fact]
    public void Parses_several_function_calls_and_gives_each_a_distinct_id()
    {
        const string payload = """
            {
              "candidates": [
                {
                  "content": {
                    "parts": [
                      { "functionCall": { "name": "grep", "args": { "pattern": "a" } } },
                      { "functionCall": { "name": "glob", "args": { "pattern": "*.cs" } } }
                    ]
                  }
                }
              ]
            }
            """;

        var result = GeminiChatClient.ParseGenerateContent(payload);

        result.ToolCalls.Should().HaveCount(2);
        result.ToolCalls.Select(call => call.CallId).Should().OnlyHaveUniqueItems();
        result.ToolCalls.Select(call => call.Name).Should().Equal("grep", "glob");
    }

    [Fact]
    public void A_function_call_without_args_is_still_reported()
    {
        const string payload = """
            { "candidates": [ { "content": { "parts": [ { "functionCall": { "name": "list_files" } } ] } } ] }
            """;

        var result = GeminiChatClient.ParseGenerateContent(payload);

        var call = result.ToolCalls.Should().ContainSingle().Subject;
        call.Name.Should().Be("list_files");
        call.Arguments.Should().BeEmpty();
    }

    [Fact]
    public void ToContents_puts_text_before_tool_calls()
    {
        const string payload = """
            {
              "candidates": [
                {
                  "content": {
                    "parts": [
                      { "functionCall": { "name": "grep", "args": {} } },
                      { "text": "and then" }
                    ]
                  }
                }
              ]
            }
            """;

        var contents = GeminiChatClient.ParseGenerateContent(payload).ToContents();

        contents.OfType<TextContent>().Select(text => text.Text).Should().Equal("and then");
        contents.OfType<FunctionCallContent>().Should().ContainSingle();
    }
}
