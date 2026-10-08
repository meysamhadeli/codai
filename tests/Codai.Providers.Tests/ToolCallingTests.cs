using Codai.Configuration;
using Codai.Core.Chat;
using Codai.Core.Tools;
using Codai.Providers.Tests.Fakes;
using FluentAssertions;
using Microsoft.Extensions.AI;

namespace Codai.Providers.Tests;

/// <summary>
/// Verifies that advertising tools drives the whole call loop: the model asks for a
/// tool, the executor runs it, and the result is fed back so the model can answer.
/// </summary>
public class ToolCallingTests
{
    private static ResolvedProviderOptions Options(bool stream = true) =>
        ResolvedProviderOptions.From(new AiProviderOptions
        {
            Provider = "openai",
            Model = "gpt-4o",
            ApiKey = "test-key",
            Stream = stream,
        });

    private static async Task<List<ChatStreamUpdate>> DrainAsync(ChatCompletionService service)
    {
        var updates = new List<ChatStreamUpdate>();
        await foreach (var update in service.StreamAsync(new ChatRequest("system", "user")))
        {
            updates.Add(update);
        }

        return updates;
    }

    [Fact]
    public async Task Advertises_the_executor_tools_to_the_provider()
    {
        var client = new ToolCallingChatClient(ToolCallingChatClient.Says("hi"));
        var executor = new RecordingToolExecutor(new StubTool("grep"), new StubTool("glob"));
        var service = new ChatCompletionService(client, Options(), executor);

        await DrainAsync(service);

        client.LastOptions!.Tools.Should().NotBeNull();
        client.LastOptions.Tools!.Select(tool => tool.Name).Should().Equal("grep", "glob");
    }

    [Fact]
    public async Task Offers_no_tools_when_there_is_no_executor()
    {
        var client = new ToolCallingChatClient(ToolCallingChatClient.Says("hi"));
        var service = new ChatCompletionService(client, Options());

        await DrainAsync(service);

        // No executor means no tools are advertised, which is observable on the request.
        client.LastOptions!.Tools.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task Advertised_tool_carries_its_description_and_schema()
    {
        var client = new ToolCallingChatClient(ToolCallingChatClient.Says("hi"));
        var service = new ChatCompletionService(
            client,
            Options(),
            new RecordingToolExecutor(new StubTool("grep")));

        await DrainAsync(service);

        var tool = client.LastOptions!.Tools!.Single().Should().BeAssignableTo<AIFunction>().Subject;
        tool.Description.Should().Be("Stub grep.");
        tool.JsonSchema.GetProperty("required")[0].GetString().Should().Be("pattern");
    }

    [Fact]
    public async Task Runs_the_requested_tool_and_answers_with_its_result()
    {
        var client = new ToolCallingChatClient(
            ToolCallingChatClient.CallsTool("call-1", "grep", new Dictionary<string, object?> { ["pattern"] = "TODO" }),
            ToolCallingChatClient.Says("Found nothing."));

        var executor = new RecordingToolExecutor(new StubTool("grep"));
        var service = new ChatCompletionService(client, Options(), executor);

        var updates = await DrainAsync(service);

        var call = executor.Calls.Should().ContainSingle().Subject;
        call.Name.Should().Be("grep");
        call.Arguments.GetProperty("pattern").GetString().Should().Be("TODO");

        updates.Select(u => u.ContentDelta).Where(d => d is not null)
            .Should().Equal("Found nothing.");
        updates[^1].IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task Feeds_the_tool_output_back_as_a_tool_message()
    {
        var client = new ToolCallingChatClient(
            ToolCallingChatClient.CallsTool("call-1", "grep", new Dictionary<string, object?> { ["pattern"] = "TODO" }),
            ToolCallingChatClient.Says("done"));

        var executor = new RecordingToolExecutor(new StubTool("grep"))
        {
            Outcome = ToolResult.Success("src/a.cs:2: // TODO"),
        };

        var service = new ChatCompletionService(client, Options(), executor);

        await DrainAsync(service);

        client.CallCount.Should().Be(2);

        var result = client.SeenMessages[1]
            .Where(message => message.Role == ChatRole.Tool)
            .SelectMany(message => message.Contents)
            .OfType<FunctionResultContent>()
            .Should().ContainSingle().Subject;

        result.CallId.Should().Be("call-1");
        result.Result.Should().Be("src/a.cs:2: // TODO");
    }

    [Fact]
    public async Task A_failed_tool_is_reported_to_the_model_as_text()
    {
        var client = new ToolCallingChatClient(
            ToolCallingChatClient.CallsTool("call-1", "read_file", new Dictionary<string, object?> { ["path"] = "nope" }),
            ToolCallingChatClient.Says("The file is missing."));

        var executor = new RecordingToolExecutor(new StubTool("read_file"))
        {
            Outcome = ToolResult.Failure("File 'nope' does not exist."),
        };

        var service = new ChatCompletionService(client, Options(), executor);

        await DrainAsync(service);

        var result = client.SeenMessages[1]
            .SelectMany(message => message.Contents)
            .OfType<FunctionResultContent>()
            .Single();

        result.Result.Should().Be("File 'nope' does not exist.");
        client.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task The_non_streaming_path_also_runs_the_loop()
    {
        var client = new ToolCallingChatClient(
            ToolCallingChatClient.CallsTool("call-1", "grep", new Dictionary<string, object?> { ["pattern"] = "TODO" }),
            ToolCallingChatClient.Says("Found it."));

        var executor = new RecordingToolExecutor(new StubTool("grep"));
        var service = new ChatCompletionService(client, Options(stream: false), executor);

        var updates = await DrainAsync(service);

        executor.Calls.Should().HaveCount(1);
        updates.Select(u => u.ContentDelta).Where(d => d is not null).Should().Equal("Found it.");
    }

    [Fact]
    public async Task Several_tool_calls_in_one_turn_all_run()
    {
        var client = new ToolCallingChatClient(
            [
                new ChatResponseUpdate(ChatRole.Assistant, [new FunctionCallContent("c1", "grep", new Dictionary<string, object?> { ["pattern"] = "a" })]),
                new ChatResponseUpdate(ChatRole.Assistant, [new FunctionCallContent("c2", "glob", new Dictionary<string, object?> { ["pattern"] = "*.cs" })]),
            ],
            ToolCallingChatClient.Says("done"));

        var executor = new RecordingToolExecutor(new StubTool("grep"), new StubTool("glob"));
        var service = new ChatCompletionService(client, Options(), executor);

        await DrainAsync(service);

        executor.Calls.Should().HaveCount(2);
    }

    [Fact]
    public async Task The_loop_stops_when_the_model_stops_asking_for_tools()
    {
        var client = new ToolCallingChatClient(ToolCallingChatClient.Says("nothing to do"));
        var executor = new RecordingToolExecutor(new StubTool("grep"));
        var service = new ChatCompletionService(client, Options(), executor);

        await DrainAsync(service);

        client.CallCount.Should().Be(1);
        executor.Calls.Should().BeEmpty();
    }
}
