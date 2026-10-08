using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Codai.Core.Abstractions;
using Codai.Providers.Internal;
using Microsoft.Extensions.AI;

namespace Codai.Providers.Anthropic;

/// <summary>
/// <see cref="IChatClient"/> for Anthropic's Messages API.
/// </summary>
/// <remarks>
/// Improvements over the original implementation: the system prompt is sent via the
/// top-level <c>system</c> field (not as a message), <c>max_tokens</c> is always
/// supplied, and input/output usage is accumulated across stream events.
/// </remarks>
internal sealed class AnthropicChatClient : HttpChatClientBase
{
    private const int DefaultMaxTokens = 4096;

    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly string _baseUrl;
    private readonly string _anthropicVersion;
    private readonly float? _temperature;
    private readonly int _maxTokens;

    public AnthropicChatClient(
        ResolvedProviderOptions options,
        HttpClient? httpClient = null)
        : base(
            ProviderNames.Anthropic,
            options.Model,
            Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri) ? uri : null)
    {
        _apiKey = options.ApiKey
            ?? throw new ProviderConfigurationException("Anthropic requires an API key.");
        _model = options.Model;
        _baseUrl = options.BaseUrl.TrimEnd('/');
        _anthropicVersion = options.ApiVersion ?? "2023-06-01";
        _temperature = options.Temperature;
        _maxTokens = options.MaxTokens > 0 ? options.MaxTokens : DefaultMaxTokens;

        _httpClient = httpClient ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var (system, user) = Split(messages);

        TextContent? text = null;
        var usage = Codai.Core.Models.TokenUsage.Empty;

        await foreach (var update in StreamCoreAsync(system, user, options, cancellationToken))
        {
            if (update.ContentDelta is { Length: > 0 } delta)
            {
                text ??= new TextContent(string.Empty);
                text.Text += delta;
            }

            if (update.Usage.HasValue)
            {
                usage = update.Usage;
            }
        }

        var message = new ChatMessage(ChatRole.Assistant, text is null ? [] : [text]);
        return new ChatResponse(message)
        {
            ModelId = _model,
            Usage = new UsageDetails
            {
                InputTokenCount = usage.InputTokens,
                OutputTokenCount = usage.OutputTokens,
                TotalTokenCount = usage.TotalTokens,
            },
        };
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var (system, user) = Split(messages);

        await foreach (var update in StreamCoreAsync(system, user, options, cancellationToken))
        {
            if (update.ContentDelta is { Length: > 0 } delta)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, delta);
            }

            if (update.IsCompleted && update.Usage.HasValue)
            {
                yield return new ChatResponseUpdate(
                    ChatRole.Assistant,
                    [new UsageContent(new UsageDetails
                    {
                        InputTokenCount = update.Usage.InputTokens,
                        OutputTokenCount = update.Usage.OutputTokens,
                        TotalTokenCount = update.Usage.TotalTokens,
                    })]);
            }
        }
    }

    private async IAsyncEnumerable<Codai.Core.Models.ChatStreamUpdate> StreamCoreAsync(
        string? system,
        string user,
        ChatOptions? options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var requestBody = new Dictionary<string, object?>
        {
            ["model"] = options?.ModelId ?? _model,
            ["max_tokens"] = options?.MaxOutputTokens ?? _maxTokens,
            ["stream"] = true,
            ["messages"] = new[] { new Dictionary<string, object?> { ["role"] = "user", ["content"] = user } },
        };

        if (!string.IsNullOrEmpty(system))
        {
            requestBody["system"] = system;
        }

        var temperature = options?.Temperature ?? _temperature;
        if (temperature is not null)
        {
            requestBody["temperature"] = temperature.Value;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/messages")
        {
            Content = JsonContent.Create(requestBody),
        };
        request.Headers.TryAddWithoutValidation("anthropic-version", _anthropicVersion);
        request.Headers.TryAddWithoutValidation("x-api-key", _apiKey);

        using var response = await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new ChatProviderException(
                $"Anthropic request failed with status {(int)response.StatusCode}: {ExtractErrorMessage(body)}");
        }

        var inputTokens = 0;
        var outputTokens = 0;

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream);

        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            if (!line.StartsWith("data: ", StringComparison.Ordinal))
            {
                continue;
            }

            using var document = JsonDocument.Parse(line[6..]);
            var root = document.RootElement;
            var type = root.TryGetProperty("type", out var typeProperty) ? typeProperty.GetString() : null;

            switch (type)
            {
                case "message_start":
                    inputTokens = ReadNested(root, ["message", "usage", "input_tokens"], inputTokens);
                    outputTokens = ReadNested(root, ["message", "usage", "output_tokens"], outputTokens);
                    break;

                case "content_block_delta":
                    if (root.TryGetProperty("delta", out var deltaElement) &&
                        deltaElement.TryGetProperty("text", out var textElement) &&
                        textElement.GetString() is { Length: > 0 } text)
                    {
                        yield return Codai.Core.Models.ChatStreamUpdate.Content(text);
                    }

                    break;

                case "message_delta":
                    outputTokens = ReadNested(root, ["usage", "output_tokens"], outputTokens);
                    break;

                case "message_stop":
                    yield return Codai.Core.Models.ChatStreamUpdate.Completed(
                        new Codai.Core.Models.TokenUsage(inputTokens, outputTokens));
                    yield break;
            }
        }

        yield return Codai.Core.Models.ChatStreamUpdate.Completed(
            new Codai.Core.Models.TokenUsage(inputTokens, outputTokens));
    }

    private static int ReadNested(JsonElement root, IReadOnlyList<string> path, int fallback)
    {
        var current = root;

        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object ||
                !current.TryGetProperty(segment, out var next))
            {
                return fallback;
            }

            current = next;
        }

        return current.ValueKind == JsonValueKind.Number && current.TryGetInt32(out var value)
            ? value
            : fallback;
    }

    internal static string ExtractErrorMessage(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error) &&
                error.TryGetProperty("message", out var message))
            {
                return message.GetString() ?? body;
            }
        }
        catch (JsonException)
        {
            // Not JSON; fall through to the raw body.
        }

        return body;
    }
}
