using Codai.Configuration;
using Codai.Core.Chat;
using Codai.Core.Tools;
using Codai.Providers.Anthropic;
using Codai.Providers.Tests.Fakes;
using FluentAssertions;
using System.Text.Json;

namespace Codai.Providers.Tests;

/// <summary>
/// The Anthropic wire format is not OpenAI-compatible, so tool calling there is written
/// by hand. These tests cover the whole exchange against a stubbed HTTP transport.
/// </summary>
public class AnthropicToolCallingTests
{
    /// <summary>A turn that asks for <c>grep(pattern=TODO)</c>, streamed the way the API streams it.</summary>
    private const string ToolCallBody = """
        event: message_start
        data: {"type":"message_start","message":{"usage":{"input_tokens":30,"output_tokens":1}}}

        event: content_block_start
        data: {"type":"content_block_start","index":0,"content_block":{"type":"tool_use","id":"toolu_1","name":"grep","input":{}}}

        event: content_block_delta
        data: {"type":"content_block_delta","index":0,"delta":{"type":"input_json_delta","partial_json":"{\"pattern\":"}}

        event: content_block_delta
        data: {"type":"content_block_delta","index":0,"delta":{"type":"input_json_delta","partial_json":"\"TODO\"}"}}

        event: content_block_stop
        data: {"type":"content_block_stop","index":0}

        event: message_delta
        data: {"type":"message_delta","usage":{"output_tokens":8}}

        event: message_stop
        data: {"type":"message_stop"}

        """;

    private const string TextBody = """
        event: message_start
        data: {"type":"message_start","message":{"usage":{"input_tokens":40,"output_tokens":1}}}

        event: content_block_delta
        data: {"type":"content_block_delta","delta":{"type":"text_delta","text":"Found it."}}

        event: message_delta
        data: {"type":"message_delta","usage":{"output_tokens":4}}

        event: message_stop
        data: {"type":"message_stop"}

        """;

    private static ResolvedProviderOptions Options() =>
        ResolvedProviderOptions.From(new AiProviderOptions
        {
            Provider = "anthropic",
            Model = "claude-3-5-sonnet",
            ApiKey = "test-key",
            Stream = true,
        });

    private static async Task<(List<ChatStreamUpdate> Updates, RecordingToolExecutor Executor, SequencedHttpMessageHandler Handler)>
        RunAsync()
    {
        var handler = SequencedHttpMessageHandler.Streaming(ToolCallBody, TextBody);
        var client = new AnthropicChatClient(Options(), handler.CreateClient());
        var executor = new RecordingToolExecutor(new StubTool("grep"))
        {
            Outcome = ToolResult.Success("src/a.cs:2: // TODO: fix"),
        };

        var service = new ChatCompletionService(client, Options(), executor);

        var updates = new List<ChatStreamUpdate>();
        await foreach (var update in service.StreamAsync(new ChatRequest("be terse", "find the TODOs")))
        {
            updates.Add(update);
        }

        return (updates, executor, handler);
    }

    [Fact]
    public async Task Advertises_the_tool_with_an_input_schema()
    {
        var (_, _, handler) = await RunAsync();

        using var request = JsonDocument.Parse(handler.RequestBodies[0]);
        var tool = request.RootElement.GetProperty("tools")[0];

        tool.GetProperty("name").GetString().Should().Be("grep");
        tool.GetProperty("description").GetString().Should().Be("Stub grep.");
        tool.GetProperty("input_schema").GetProperty("required")[0].GetString().Should().Be("pattern");
    }

    [Fact]
    public async Task Assembles_a_streamed_tool_call_from_its_json_fragments()
    {
        var (_, executor, _) = await RunAsync();

        var call = executor.Calls.Should().ContainSingle().Subject;
        call.Name.Should().Be("grep");
        call.Arguments.GetProperty("pattern").GetString().Should().Be("TODO");
    }

    [Fact]
    public async Task Sends_the_call_back_as_an_assistant_tool_use_turn()
    {
        var (_, _, handler) = await RunAsync();

        using var request = JsonDocument.Parse(handler.RequestBodies[1]);
        var turns = request.RootElement.GetProperty("messages");

        turns.GetArrayLength().Should().Be(3);

        var assistant = turns[1];
        assistant.GetProperty("role").GetString().Should().Be("assistant");

        var toolUse = assistant.GetProperty("content")[0];
        toolUse.GetProperty("type").GetString().Should().Be("tool_use");
        toolUse.GetProperty("id").GetString().Should().Be("toolu_1");
        toolUse.GetProperty("name").GetString().Should().Be("grep");
        toolUse.GetProperty("input").GetProperty("pattern").GetString().Should().Be("TODO");
    }

    [Fact]
    public async Task Sends_the_result_back_as_a_user_tool_result_turn()
    {
        var (_, _, handler) = await RunAsync();

        using var request = JsonDocument.Parse(handler.RequestBodies[1]);
        var result = request.RootElement.GetProperty("messages")[2];

        result.GetProperty("role").GetString().Should().Be("user");

        var block = result.GetProperty("content")[0];
        block.GetProperty("type").GetString().Should().Be("tool_result");
        block.GetProperty("tool_use_id").GetString().Should().Be("toolu_1");
        block.GetProperty("content").GetString().Should().Be("src/a.cs:2: // TODO: fix");
    }

    [Fact]
    public async Task Answers_with_the_text_from_the_follow_up_turn()
    {
        var (updates, _, handler) = await RunAsync();

        handler.RequestBodies.Should().HaveCount(2);
        updates.Select(update => update.ContentDelta).Where(delta => delta is not null)
            .Should().Equal("Found it.");
        updates[^1].IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task Usage_from_both_turns_is_reported()
    {
        var (updates, _, _) = await RunAsync();

        updates[^1].Usage.InputTokens.Should().Be(40);
        updates[^1].Usage.OutputTokens.Should().Be(4);
    }

    [Fact]
    public async Task A_request_without_tools_carries_no_tools_field()
    {
        var handler = SequencedHttpMessageHandler.Streaming(TextBody);
        var client = new AnthropicChatClient(Options(), handler.CreateClient());
        var service = new ChatCompletionService(client, Options());

        await foreach (var _ in service.StreamAsync(new ChatRequest("be terse", "say hello")))
        {
        }

        using var request = JsonDocument.Parse(handler.RequestBodies[0]);
        request.RootElement.TryGetProperty("tools", out _).Should().BeFalse();
    }

    [Fact]
    public async Task A_tool_call_with_no_arguments_is_sent_with_an_empty_object()
    {
        const string noArgsBody = """
            data: {"type":"content_block_start","index":0,"content_block":{"type":"tool_use","id":"toolu_9","name":"list_files","input":{}}}

            data: {"type":"content_block_stop","index":0}

            data: {"type":"message_stop"}

            """;

        var handler = SequencedHttpMessageHandler.Streaming(noArgsBody, TextBody);
        var client = new AnthropicChatClient(Options(), handler.CreateClient());
        var executor = new RecordingToolExecutor(new StubTool("list_files"));
        var service = new ChatCompletionService(client, Options(), executor);

        await foreach (var _ in service.StreamAsync(new ChatRequest("be terse", "list")))
        {
        }

        executor.Calls.Should().ContainSingle().Which.Name.Should().Be("list_files");

        using var request = JsonDocument.Parse(handler.RequestBodies[1]);
        var toolUse = request.RootElement.GetProperty("messages")[1].GetProperty("content")[0];

        toolUse.GetProperty("input").ValueKind.Should().Be(JsonValueKind.Object);
    }
}
