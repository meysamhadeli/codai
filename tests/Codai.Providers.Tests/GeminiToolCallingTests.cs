using System.Text.Json;
using Codai.Configuration;
using Codai.Core.Chat;
using Codai.Core.Tools;
using Codai.Providers.Gemini;
using Codai.Providers.Tests.Fakes;
using FluentAssertions;

namespace Codai.Providers.Tests;

/// <summary>
/// Gemini's wire format is not OpenAI-compatible either, and it correlates a function
/// result by name rather than by call id. These tests cover the whole exchange.
/// </summary>
public class GeminiToolCallingTests
{
    private const string ToolCallBody = """
        {
          "candidates": [
            {
              "content": {
                "role": "model",
                "parts": [ { "functionCall": { "name": "grep", "args": { "pattern": "TODO" } } } ]
              }
            }
          ],
          "usageMetadata": { "promptTokenCount": 20, "candidatesTokenCount": 5 }
        }
        """;

    private const string TextBody = """
        {
          "candidates": [ { "content": { "parts": [ { "text": "Found it." } ] } } ],
          "usageMetadata": { "promptTokenCount": 35, "candidatesTokenCount": 4 }
        }
        """;

    private static ResolvedProviderOptions Options() =>
        ResolvedProviderOptions.From(new AiProviderOptions
        {
            Provider = "gemini",
            Model = "gemini-2.0-flash",
            ApiKey = "test-key",
            Stream = true,
        });

    private static async Task<(List<ChatStreamUpdate> Updates, RecordingToolExecutor Executor, SequencedHttpMessageHandler Handler)>
        RunAsync(ITool? tool = null)
    {
        var handler = SequencedHttpMessageHandler.Json(ToolCallBody, TextBody);
        var client = new GeminiChatClient(Options(), handler.CreateClient());
        var executor = new RecordingToolExecutor(tool ?? new StubTool("grep"))
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
    public async Task Advertises_the_tool_as_a_function_declaration()
    {
        var (_, _, handler) = await RunAsync();

        using var request = JsonDocument.Parse(handler.RequestBodies[0]);
        var declaration = request.RootElement.GetProperty("tools")[0].GetProperty("functionDeclarations")[0];

        declaration.GetProperty("name").GetString().Should().Be("grep");
        declaration.GetProperty("description").GetString().Should().Be("Stub grep.");
        declaration.GetProperty("parameters").GetProperty("required")[0].GetString().Should().Be("pattern");
    }

    [Fact]
    public async Task Strips_schema_keywords_gemini_does_not_model()
    {
        var tool = new StubTool("grep", schema: StubTool.RichSchema);

        var (_, _, handler) = await RunAsync(tool);

        using var request = JsonDocument.Parse(handler.RequestBodies[0]);
        var parameters = request.RootElement
            .GetProperty("tools")[0]
            .GetProperty("functionDeclarations")[0]
            .GetProperty("parameters");

        parameters.TryGetProperty("additionalProperties", out _).Should().BeFalse();
        parameters.TryGetProperty("$schema", out _).Should().BeFalse();
        parameters.TryGetProperty("title", out _).Should().BeFalse();

        parameters.GetProperty("properties").TryGetProperty("case_sensitive", out var caseSensitive)
            .Should().BeTrue();
        caseSensitive.TryGetProperty("default", out _).Should().BeFalse();

        // The parts Gemini does understand survive untouched.
        parameters.GetProperty("type").GetString().Should().Be("object");
        parameters.GetProperty("required")[0].GetString().Should().Be("pattern");
        parameters.GetProperty("properties").GetProperty("pattern").GetProperty("description")
            .GetString().Should().Be("Regular expression to search for.");
    }

    [Fact]
    public async Task Runs_the_function_the_model_asked_for()
    {
        var (_, executor, _) = await RunAsync();

        var call = executor.Calls.Should().ContainSingle().Subject;
        call.Name.Should().Be("grep");
        call.Arguments.GetProperty("pattern").GetString().Should().Be("TODO");
    }

    [Fact]
    public async Task Sends_the_call_back_as_a_model_function_call_part()
    {
        var (_, _, handler) = await RunAsync();

        using var request = JsonDocument.Parse(handler.RequestBodies[1]);
        var contents = request.RootElement.GetProperty("contents");

        contents.GetArrayLength().Should().Be(3);

        var call = contents[1].GetProperty("parts")[0].GetProperty("functionCall");
        contents[1].GetProperty("role").GetString().Should().Be("model");
        call.GetProperty("name").GetString().Should().Be("grep");
        call.GetProperty("args").GetProperty("pattern").GetString().Should().Be("TODO");
    }

    [Fact]
    public async Task Sends_the_result_back_as_a_function_response_part_named_after_the_function()
    {
        var (_, _, handler) = await RunAsync();

        using var request = JsonDocument.Parse(handler.RequestBodies[1]);
        var turn = request.RootElement.GetProperty("contents")[2];

        turn.GetProperty("role").GetString().Should().Be("user");

        var response = turn.GetProperty("parts")[0].GetProperty("functionResponse");
        response.GetProperty("name").GetString().Should().Be("grep");
        response.GetProperty("response").GetProperty("result").GetString()
            .Should().Be("src/a.cs:2: // TODO: fix");
    }

    [Fact]
    public async Task Passes_the_system_prompt_through_system_instruction()
    {
        var (_, _, handler) = await RunAsync();

        using var request = JsonDocument.Parse(handler.RequestBodies[0]);

        request.RootElement.GetProperty("systemInstruction").GetProperty("parts")[0]
            .GetProperty("text").GetString().Should().Be("be terse");

        request.RootElement.GetProperty("contents")[0].GetProperty("role").GetString().Should().Be("user");
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
    public async Task A_request_without_tools_carries_no_tools_field()
    {
        var handler = SequencedHttpMessageHandler.Json(TextBody);
        var client = new GeminiChatClient(Options(), handler.CreateClient());
        var service = new ChatCompletionService(client, Options());

        await foreach (var _ in service.StreamAsync(new ChatRequest("be terse", "say hello")))
        {
        }

        using var request = JsonDocument.Parse(handler.RequestBodies[0]);
        request.RootElement.TryGetProperty("tools", out _).Should().BeFalse();
    }

    [Fact]
    public async Task The_whole_exchange_ends_after_the_model_stops_asking()
    {
        var (_, executor, handler) = await RunAsync();

        handler.RequestBodies.Should().HaveCount(2);
        executor.Calls.Should().HaveCount(1);
    }
}
