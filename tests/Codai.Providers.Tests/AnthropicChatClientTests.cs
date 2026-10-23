using System.Net;
using Codai.Configuration;
using Codai.Core.Chat;
using Codai.Core.Tokens;
using Codai.Providers.Anthropic;
using Codai.Providers.Tests.Fakes;
using FluentAssertions;

namespace Codai.Providers.Tests;

public class AnthropicChatClientTests
{
    private const string SseBody = """
        event: message_start
        data: {"type":"message_start","message":{"usage":{"input_tokens":25,"output_tokens":1}}}

        event: content_block_delta
        data: {"type":"content_block_delta","delta":{"type":"text_delta","text":"Hello"}}

        event: content_block_delta
        data: {"type":"content_block_delta","delta":{"type":"text_delta","text":" world"}}

        event: message_delta
        data: {"type":"message_delta","usage":{"output_tokens":7}}

        event: message_stop
        data: {"type":"message_stop"}

        """;

    private static ResolvedProviderOptions Options(string? apiVersion = null) =>
        ResolvedProviderOptions.From(new AiProviderOptions
        {
            Provider = "anthropic",
            Model = "claude-3-5-sonnet",
            ApiKey = "test-key",
            ApiVersion = apiVersion,
        });

    private static async Task<(List<ChatStreamUpdate> Updates, StubHttpMessageHandler Handler)> RunAsync(
        string body,
        HttpStatusCode status = HttpStatusCode.OK,
        string? apiVersion = null)
    {
        var handler = new StubHttpMessageHandler(body, status);
        var client = new AnthropicChatClient(Options(apiVersion), StubHttpMessageHandler.CreateClient(body, status, handler: handler));
        var service = new ChatCompletionService(client, Options(apiVersion));

        var updates = new List<ChatStreamUpdate>();
        await foreach (var update in service.StreamAsync(new ChatRequest("be terse", "say hello")))
        {
            updates.Add(update);
        }

        return (updates, handler);
    }

    [Fact]
    public async Task Streams_text_deltas_and_usage()
    {
        var (updates, _) = await RunAsync(SseBody);

        updates.Where(u => u.ContentDelta is not null)
            .Select(u => u.ContentDelta)
            .Should().ContainInOrder("Hello", " world");

        updates[^1].IsCompleted.Should().BeTrue();
        updates[^1].Usage.Should().Be(new TokenUsage(25, 7));
    }

    [Fact]
    public async Task Sends_api_key_and_version_headers()
    {
        var (_, handler) = await RunAsync(SseBody, apiVersion: "2023-06-01");

        handler.LastRequestHeaders.Should().ContainKey("x-api-key");
        handler.LastRequestHeaders["x-api-key"].Should().ContainSingle().Which.Should().Be("test-key");
        handler.LastRequestHeaders["anthropic-version"].Should().ContainSingle().Which.Should().Be("2023-06-01");
    }

    [Fact]
    public async Task Uses_default_anthropic_version_when_not_configured()
    {
        var (_, handler) = await RunAsync(SseBody);

        handler.LastRequestHeaders["anthropic-version"].Should().ContainSingle().Which.Should().Be("2023-06-01");
    }

    [Fact]
    public async Task Sends_system_as_top_level_field_and_requires_max_tokens()
    {
        var (_, handler) = await RunAsync(SseBody);

        handler.LastRequestBody.Should().Contain("\"system\":\"be terse\"");
        handler.LastRequestBody.Should().Contain("\"max_tokens\":4096");
        handler.LastRequestBody.Should().NotContain("\"role\":\"system\"");
    }

    [Fact]
    public async Task Fails_with_provider_message_on_error_status()
    {
        const string errorBody = """{"error":{"type":"authentication_error","message":"invalid api key"}}""";

        var act = async () => await RunAsync(errorBody, HttpStatusCode.Unauthorized);

        await act.Should().ThrowAsync<Core.Abstractions.ChatProviderException>()
            .WithMessage("*invalid api key*");
    }

    [Fact]
    public void ExtractErrorMessage_falls_back_to_raw_body_for_non_json()
    {
        AnthropicChatClient.ExtractErrorMessage("not json").Should().Be("not json");
    }
}
