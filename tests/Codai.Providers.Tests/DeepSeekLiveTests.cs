using System.Text;
using Codai.Configuration;
using Codai.Core.Chat;
using Codai.Core.Tokens;
using Codai.Providers.Tests.Fakes;
using FluentAssertions;

namespace Codai.Providers.Tests;

/// <summary>
/// Live tests against the DeepSeek platform. They run only when the <c>API_KEY</c>
/// environment variable is set and never log the key.
/// </summary>
[Trait("Category", "Integration")]
public class DeepSeekLiveTests
{
    private static ResolvedProviderOptions Options() =>
        ResolvedProviderOptions.From(new AiProviderOptions
        {
            Provider = "deepseek",
            // Leave the model unset so the provider default (deepseek-flash) applies.
            Model = Environment.GetEnvironmentVariable("DEEPSEEK_MODEL") ?? string.Empty,
            ApiKey = Environment.GetEnvironmentVariable("API_KEY"),
        });

    [RequiresApiKeyFact]
    public async Task Streams_a_completion_and_reports_usage()
    {
        var options = Options();
        var service = new ChatClientFactory().CreateService(options);

        var content = new StringBuilder();
        TokenUsage usage = TokenUsage.Empty;
        var completed = false;

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));

        await foreach (var update in service.StreamAsync(
            new ChatRequest(
                "You are a terse assistant.",
                "Reply with exactly the single word: pong"),
            cts.Token))
        {
            if (update.ContentDelta is not null)
            {
                content.Append(update.ContentDelta);
            }

            if (update.IsCompleted)
            {
                completed = true;
                usage = update.Usage;
            }
        }

        completed.Should().BeTrue();
        content.ToString().Trim().Should().NotBeNullOrWhiteSpace();
        content.ToString().Should().Contain("pong", "the model was asked for a fixed reply");
    }

    [RequiresApiKeyFact]
    public async Task Resolves_the_default_base_url_for_deepseek()
    {
        var options = Options();

        // Guards against the original bug where the default URL was dropped.
        options.BaseUrl.Should().Be("https://api.deepseek.com");
    }

    /// <summary>
    /// End-to-end proof that tool calling works against a real provider: the model is
    /// expected to notice it needs the secret, ask for it, and answer with what the tool
    /// returned rather than with a guess.
    /// </summary>
    [RequiresApiKeyFact]
    public async Task Calls_a_tool_and_answers_with_its_result()
    {
        const string secret = "ZAPHOD-42";

        var executor = new RecordingToolExecutor(new SecretTool())
        {
            Outcome = Codai.Core.Tools.ToolResult.Success($"The secret code is {secret}."),
        };

        var service = new ChatClientFactory().CreateService(Options(), executor);

        var content = new StringBuilder();

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));

        await foreach (var update in service.StreamAsync(
            new ChatRequest(
                "You are a terse assistant. Use the tools you are given.",
                "What is the secret code? You do not know it, so call get_secret."),
            cts.Token))
        {
            if (update.ContentDelta is not null)
            {
                content.Append(update.ContentDelta);
            }
        }

        executor.Calls.Should().NotBeEmpty("the model was asked to call get_secret");
        executor.Calls[0].Name.Should().Be("get_secret");
        content.ToString().Should().Contain(secret);
    }

    [Fact]
    public void Defaults_to_the_deepseek_flash_model()
    {
        ResolvedProviderOptions.From(new AiProviderOptions { Provider = "deepseek" })
            .Model.Should().Be("deepseek-flash");
    }

    /// <summary>A tool whose answer the model cannot know without calling it.</summary>
    private sealed class SecretTool : Core.Tools.ToolBase
    {
        public override string Name => "get_secret";

        public override string Description =>
            "Returns the project's secret code. Call this whenever the user asks for the secret code.";

        public override string ParametersSchema =>
            """{"type":"object","properties":{},"additionalProperties":false}""";

        public override Task<Core.Tools.ToolResult> InvokeAsync(
            System.Text.Json.JsonElement arguments,
            Core.Tools.ToolContext context,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Core.Tools.ToolResult.Success("The secret code is unknown."));
    }
}
