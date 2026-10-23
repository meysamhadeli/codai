using Codai.Configuration;
using Codai.Core.Chat;
using Codai.Core.Tokens;
using Codai.Providers.Tests.Fakes;
using FluentAssertions;
using Microsoft.Extensions.AI;

namespace Codai.Providers.Tests;

public class ChatCompletionServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reasoning_chunks_report_phase_without_exposing_private_text(bool compatible)
    {
        var reasoning = new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent("private reasoning")]);
        if (compatible)
        {
            reasoning = new ChatResponseUpdate(ChatRole.Assistant, Array.Empty<AIContent>())
            {
                RawRepresentation = System.ClientModel.Primitives.ModelReaderWriter.Read<OpenAI.Chat.StreamingChatCompletionUpdate>(
                    BinaryData.FromString("""{"id":"codai-response","object":"chat.completion.chunk","created":1760000000,"model":"codai-model","choices":[{"index":0,"delta":{"reasoning_content":"private reasoning"}}]}""")),
            };
        }
        var service = new ChatCompletionService(new ScriptedChatClient([reasoning, new ChatResponseUpdate(ChatRole.Assistant, "Visible answer")]), Options());
        var updates = await DrainAsync(service);
        updates[0].IsReasoning.Should().BeTrue();
        updates[0].ContentDelta.Should().BeNull();
        string.Concat(updates.Select(update => update.ContentDelta)).Should().Be("Visible answer");
        updates[^1].IsCompleted.Should().BeTrue();
    }

    private static ResolvedProviderOptions Options(
        float? temperature = null,
        int maxTokens = 0,
        string provider = "openai",
        string model = "gpt-4o",
        bool stream = true,
        ReasoningEffortLevel? reasoningEffort = null) =>
        ResolvedProviderOptions.From(new AiProviderOptions
        {
            Provider = provider,
            Model = model,
            ApiKey = "test-key",
            Temperature = temperature,
            MaxTokens = maxTokens,
            Stream = stream,
            ReasoningEffort = reasoningEffort,
        });

    private static async Task<List<ChatStreamUpdate>> DrainAsync(
        ChatCompletionService service,
        ChatRequest? request = null)
    {
        var updates = new List<ChatStreamUpdate>();
        await foreach (var update in service.StreamAsync(request ?? new ChatRequest("system", "user")))
        {
            updates.Add(update);
        }

        return updates;
    }

    private static RecordingToolExecutor TwoTools() => new(
        new StubTool("read_file"),
        new StubTool("write_file", isReadOnly: false));

    [Fact]
    public async Task Advertises_only_the_tools_the_request_allows()
    {
        var client = ScriptedChatClient.Content("done");
        var service = new ChatCompletionService(client, Options(), TwoTools());

        await DrainAsync(service, new ChatRequest("system", "user", ["read_file"]));

        client.CapturedOptions!.Tools!.Select(tool => tool.Name).Should().Equal("read_file");
    }

    [Fact]
    public async Task A_request_that_names_no_tools_is_offered_every_tool()
    {
        var client = ScriptedChatClient.Content("done");
        var service = new ChatCompletionService(client, Options(), TwoTools());

        await DrainAsync(service, new ChatRequest("system", "user"));

        client.CapturedOptions!.Tools!.Select(tool => tool.Name)
            .Should().Equal("read_file", "write_file");
    }

    [Fact]
    public async Task A_request_that_allows_nothing_is_offered_no_tools_at_all()
    {
        var client = ScriptedChatClient.Content("done");
        var service = new ChatCompletionService(client, Options(), TwoTools());

        // This is how compaction asks: a summariser that can call read_file would spend
        // the turn reading instead of summarising.
        await DrainAsync(service, new ChatRequest("system", "user", []));

        client.CapturedOptions!.Tools.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task Streams_content_chunks_then_completes()
    {
        var client = ScriptedChatClient.Content("Hello ", "world");
        var service = new ChatCompletionService(client, Options());

        var updates = await DrainAsync(service);

        updates.Take(2).Select(u => u.ContentDelta).Should().ContainInOrder("Hello ", "world");
        updates.Should().OnlyContain(u => !u.IsCompleted || u.ContentDelta == null);
        updates[^1].IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task Reports_provider_and_model_names()
    {
        var service = new ChatCompletionService(ScriptedChatClient.Content("x"), Options(provider: "deepseek", model: "deepseek-chat"));

        service.ProviderName.Should().Be("deepseek");
        service.ModelName.Should().Be("deepseek-chat");
    }

    [Fact]
    public async Task Surfaces_token_usage_on_the_completed_update()
    {
        var client = ScriptedChatClient.WithUsage(new TokenUsage(120, 30), "hi");
        var service = new ChatCompletionService(client, Options());

        var updates = await DrainAsync(service);

        updates[^1].IsCompleted.Should().BeTrue();
        updates[^1].Usage.Should().Be(new TokenUsage(120, 30));
    }

    [Fact]
    public async Task Sends_system_and_user_messages()
    {
        var client = ScriptedChatClient.Content("ok");
        var service = new ChatCompletionService(client, Options());

        await DrainAsync(service);

        client.CapturedMessages.Should().HaveCount(2);
        client.CapturedMessages[0].Role.Should().Be(Microsoft.Extensions.AI.ChatRole.System);
        client.CapturedMessages[0].Text.Should().Be("system");
        client.CapturedMessages[1].Role.Should().Be(Microsoft.Extensions.AI.ChatRole.User);
        client.CapturedMessages[1].Text.Should().Be("user");
    }

    [Fact]
    public async Task Forwards_temperature_and_max_tokens()
    {
        var client = ScriptedChatClient.Content("ok");
        var service = new ChatCompletionService(client, Options(temperature: 0.8f, maxTokens: 512));

        await DrainAsync(service);

        client.CapturedOptions!.Temperature.Should().Be(0.8f);
        client.CapturedOptions.MaxOutputTokens.Should().Be(512);
    }

    [Fact]
    public async Task Leaves_max_tokens_null_when_not_configured()
    {
        var client = ScriptedChatClient.Content("ok");
        var service = new ChatCompletionService(client, Options(maxTokens: 0));

        await DrainAsync(service);

        client.CapturedOptions!.MaxOutputTokens.Should().BeNull();
    }

    [Fact]
    public async Task Wraps_provider_failures_in_chat_provider_exception()
    {
        var client = ScriptedChatClient.Failing(new InvalidOperationException("boom"));
        var service = new ChatCompletionService(client, Options());

        var act = async () => await DrainAsync(service);

        await act.Should().ThrowAsync<Core.Abstractions.ChatProviderException>()
            .WithMessage("*boom*");
    }

    [Fact]
    public async Task Wraps_failures_that_occur_when_starting_the_stream()
    {
        var client = ScriptedChatClient.Failing(new InvalidOperationException("no connection"));
        var service = new ChatCompletionService(client, Options());

        var act = async () => await DrainAsync(service);

        (await act.Should().ThrowAsync<Core.Abstractions.ChatProviderException>())
            .Which.Message.Should().Contain("openai");
    }

    [Fact]
    public async Task Streams_by_default()
    {
        var client = ScriptedChatClient.Content("a", "b");
        var service = new ChatCompletionService(client, Options());

        var updates = await DrainAsync(service);

        updates.Count(u => !u.IsCompleted).Should().Be(2);
    }

    [Fact]
    public async Task Non_streaming_mode_returns_the_response_as_one_chunk()
    {
        var client = ScriptedChatClient.Content("Hello ", "world");
        var service = new ChatCompletionService(client, Options(stream: false));

        var updates = await DrainAsync(service);

        updates.Should().HaveCount(2);
        updates[0].ContentDelta.Should().Be("Hello world");
        updates[^1].IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task Non_streaming_mode_reports_token_usage()
    {
        var client = ScriptedChatClient.WithUsage(new TokenUsage(200, 40), "answer");
        var service = new ChatCompletionService(client, Options(stream: false));

        var updates = await DrainAsync(service);

        updates[^1].Usage.Should().Be(new TokenUsage(200, 40));
    }

    [Fact]
    public async Task Non_streaming_mode_wraps_provider_failures()
    {
        var client = ScriptedChatClient.Failing(new HttpRequestException("connection refused"));
        var service = new ChatCompletionService(client, Options(stream: false));

        var act = async () => await DrainAsync(service);

        (await act.Should().ThrowAsync<Core.Abstractions.ChatProviderException>())
            .Which.Message.Should().Contain("connection refused");
    }

    [Fact]
    public async Task Non_streaming_mode_forwards_the_same_options()
    {
        var client = ScriptedChatClient.Content("ok");
        var service = new ChatCompletionService(client, Options(stream: false, temperature: 0.3f, maxTokens: 64));

        await DrainAsync(service);

        client.CapturedOptions!.Temperature.Should().Be(0.3f);
        client.CapturedOptions.MaxOutputTokens.Should().Be(64);
    }

    [Theory]
    [InlineData(ReasoningEffortLevel.Low, ReasoningEffort.Low)]
    [InlineData(ReasoningEffortLevel.Medium, ReasoningEffort.Medium)]
    [InlineData(ReasoningEffortLevel.High, ReasoningEffort.High)]
    public async Task Forwards_the_configured_reasoning_effort(
        ReasoningEffortLevel configured,
        ReasoningEffort expected)
    {
        var client = ScriptedChatClient.Content("ok");
        var service = new ChatCompletionService(client, Options(reasoningEffort: configured));

        await DrainAsync(service);

        client.CapturedOptions!.Reasoning.Should().NotBeNull();
        client.CapturedOptions.Reasoning!.Effort.Should().Be(expected);
    }

    [Fact]
    public async Task Leaves_reasoning_unset_when_not_configured()
    {
        var client = ScriptedChatClient.Content("ok");
        var service = new ChatCompletionService(client, Options());

        await DrainAsync(service);

        client.CapturedOptions!.Reasoning.Should().BeNull(
            "an unconfigured effort must let the provider choose its own default");
    }

    [Fact]
    public async Task Propagates_cancellation()
    {
        var client = ScriptedChatClient.Content("a", "b", "c");
        var service = new ChatCompletionService(client, Options());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () =>
        {
            await foreach (var _ in service.StreamAsync(new ChatRequest("s", "u"), cts.Token))
            {
            }
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
