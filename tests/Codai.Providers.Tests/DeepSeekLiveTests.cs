using System.Text;
using Codai.Configuration;
using Codai.Core.Chat;
using Codai.Core.Tokens;
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

    [Fact]
    public void Defaults_to_the_deepseek_flash_model()
    {
        ResolvedProviderOptions.From(new AiProviderOptions { Provider = "deepseek" })
            .Model.Should().Be("deepseek-flash");
    }
}
