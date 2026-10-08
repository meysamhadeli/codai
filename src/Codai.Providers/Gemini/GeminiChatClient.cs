using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Codai.Configuration;
using Codai.Core.Abstractions;
using Codai.Providers.Internal;
using Microsoft.Extensions.AI;

namespace Codai.Providers.Gemini;

/// <summary>
/// <see cref="IChatClient"/> for Google's Gemini generative language API.
/// </summary>
/// <remarks>
/// The original implementation incorrectly sent a bearer token; the API expects the
/// key in <c>x-goog-api-key</c>. The system prompt is now sent via
/// <c>systemInstruction</c> rather than being concatenated into the user turn.
/// </remarks>
internal sealed class GeminiChatClient : HttpChatClientBase
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly string _baseUrl;
    private readonly float? _temperature;
    private readonly int _maxTokens;

    public GeminiChatClient(
        ResolvedProviderOptions options,
        HttpClient? httpClient = null)
        : base(
            ProviderCatalog.Gemini,
            options.Model,
            Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri) ? uri : null)
    {
        _apiKey = options.ApiKey
            ?? throw new ProviderConfigurationException("Gemini requires an API key.");
        _model = options.Model;
        _baseUrl = options.BaseUrl.TrimEnd('/');
        _temperature = options.Temperature;
        _maxTokens = options.MaxTokens;

        _httpClient = httpClient ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var (system, user) = Split(messages);
        var result = await GenerateAsync(system, user, options, cancellationToken).ConfigureAwait(false);

        var message = new ChatMessage(ChatRole.Assistant, result.Text);
        return new ChatResponse(message)
        {
            ModelId = _model,
            Usage = new UsageDetails
            {
                InputTokenCount = result.Usage.InputTokens,
                OutputTokenCount = result.Usage.OutputTokens,
                TotalTokenCount = result.Usage.TotalTokens,
            },
        };
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var (system, user) = Split(messages);
        var result = await GenerateAsync(system, user, options, cancellationToken).ConfigureAwait(false);

        if (result.Text.Length > 0)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, result.Text);
        }

        yield return new ChatResponseUpdate(
            ChatRole.Assistant,
            [new UsageContent(new UsageDetails
            {
                InputTokenCount = result.Usage.InputTokens,
                OutputTokenCount = result.Usage.OutputTokens,
                TotalTokenCount = result.Usage.TotalTokens,
            })]);
    }

    private async Task<(string Text, Codai.Core.Tokens.TokenUsage Usage)> GenerateAsync(
        string? system,
        string user,
        ChatOptions? options,
        CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, object?>
        {
            ["contents"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["role"] = "user",
                    ["parts"] = new[] { new Dictionary<string, object?> { ["text"] = user } },
                },
            },
        };

        if (!string.IsNullOrEmpty(system))
        {
            body["systemInstruction"] = new Dictionary<string, object?>
            {
                ["parts"] = new[] { new Dictionary<string, object?> { ["text"] = system } },
            };
        }

        var generationConfig = new Dictionary<string, object?>();
        var temperature = options?.Temperature ?? _temperature;
        if (temperature is not null)
        {
            generationConfig["temperature"] = temperature.Value;
        }

        var maxTokens = options?.MaxOutputTokens ?? _maxTokens;
        if (maxTokens > 0)
        {
            generationConfig["maxOutputTokens"] = maxTokens;
        }

        if (generationConfig.Count > 0)
        {
            body["generationConfig"] = generationConfig;
        }

        var model = options?.ModelId ?? _model;
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{_baseUrl}/models/{model}:generateContent")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.TryAddWithoutValidation("x-goog-api-key", _apiKey);

        using var response = await _httpClient
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);

        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new ChatProviderException(
                $"Gemini request failed with status {(int)response.StatusCode}: {payload}");
        }

        return ParseGenerateContent(payload);
    }

    internal static (string Text, Codai.Core.Tokens.TokenUsage Usage) ParseGenerateContent(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        var text = string.Empty;

        if (root.TryGetProperty("candidates", out var candidates) &&
            candidates.ValueKind == JsonValueKind.Array &&
            candidates.GetArrayLength() > 0)
        {
            var candidate = candidates[0];

            if (candidate.TryGetProperty("content", out var content) &&
                content.TryGetProperty("parts", out var parts) &&
                parts.ValueKind == JsonValueKind.Array)
            {
                var builder = new System.Text.StringBuilder();
                foreach (var part in parts.EnumerateArray())
                {
                    if (part.TryGetProperty("text", out var textElement))
                    {
                        builder.Append(textElement.GetString());
                    }
                }

                text = builder.ToString();
            }
        }

        var usage = Codai.Core.Tokens.TokenUsage.Empty;
        if (root.TryGetProperty("usageMetadata", out var usageMetadata))
        {
            usage = new Codai.Core.Tokens.TokenUsage(
                ReadInt(usageMetadata, "promptTokenCount"),
                ReadInt(usageMetadata, "candidatesTokenCount"));
        }

        return (text, usage);
    }

    private static int ReadInt(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.TryGetInt32(out var result) ? result : 0;
}
