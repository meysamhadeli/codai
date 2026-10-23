using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Codai.Configuration;
using Codai.Core.Abstractions;
using Codai.Core.Tokens;
using Codai.Providers.Internal;
using Microsoft.Extensions.AI;

namespace Codai.Providers.Gemini;

/// <summary>
/// <see cref="IChatClient"/> for Google's Gemini generative language API.
/// </summary>
/// <remarks>
/// <para>
/// The original implementation incorrectly sent a bearer token; the API expects the key
/// in <c>x-goog-api-key</c>. The system prompt is sent via <c>systemInstruction</c>
/// rather than being concatenated into the user turn.
/// </para>
/// <para>
/// Tool calling is implemented by hand: functions are advertised under
/// <c>tools[].functionDeclarations</c>, the model answers with <c>functionCall</c> parts,
/// and results go back as <c>functionResponse</c> parts. Gemini correlates a result by
/// function <em>name</em> rather than by call id, so the ids used internally are mapped
/// back to names while the request is built.
/// </para>
/// <para>
/// Responses are not streamed: the API is called with <c>generateContent</c> and the
/// complete answer is surfaced as a single delta, which the session renders the same way.
/// </para>
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
        var result = await GenerateAsync(messages, options, cancellationToken).ConfigureAwait(false);

        return new ChatResponse(new ChatMessage(ChatRole.Assistant, result.ToContents()))
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
        var result = await GenerateAsync(messages, options, cancellationToken).ConfigureAwait(false);

        if (result.Text.Length > 0)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, result.Text);
        }

        foreach (var toolCall in result.ToolCalls)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, [toolCall]);
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

    private async Task<GeneratedContent> GenerateAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options,
        CancellationToken cancellationToken)
    {
        // Materialised because the message list is walked twice: once for the system
        // instruction and once for the contents.
        var conversation = messages as IReadOnlyList<ChatMessage> ?? [.. messages];

        var body = new Dictionary<string, object?>
        {
            ["contents"] = BuildContents(conversation),
        };

        var system = SystemPrompt(conversation);
        if (!string.IsNullOrEmpty(system))
        {
            body["systemInstruction"] = new Dictionary<string, object?>
            {
                ["parts"] = new[] { Part("text", system) },
            };
        }

        var functions = ToolPayloads.Functions(options?.Tools);
        if (functions.Count > 0)
        {
            body["tools"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["functionDeclarations"] = functions
                        .Select(function => new Dictionary<string, object?>
                        {
                            ["name"] = function.Name,
                            ["description"] = function.Description,
                            ["parameters"] = GeminiSchema.Sanitize(function.JsonSchema),
                        })
                        .ToList(),
                },
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

    /// <summary>What one Gemini response contained.</summary>
    internal readonly record struct GeneratedContent(
        string Text,
        IReadOnlyList<FunctionCallContent> ToolCalls,
        TokenUsage Usage)
    {
        /// <summary>The assistant contents, text before tool calls, as MEAI expects.</summary>
        public AIContent[] ToContents()
        {
            var contents = new List<AIContent>(ToolCalls.Count + 1);

            if (Text.Length > 0)
            {
                contents.Add(new TextContent(Text));
            }

            contents.AddRange(ToolCalls);

            return [.. contents];
        }
    }

    /// <summary>
    /// Builds the ordered <c>contents</c> array. Consecutive turns of the same role are
    /// merged, because Gemini expects alternating roles and tool results can arrive as
    /// several messages.
    /// </summary>
    private static List<object> BuildContents(IEnumerable<ChatMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var contents = new List<object>();

        // Gemini matches a function result by name, so names seen on the model's calls
        // are remembered here and looked up when the results come back.
        var namesByCallId = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var message in messages)
        {
            if (message.Role == ChatRole.System)
            {
                continue;
            }

            var parts = message.Role == ChatRole.Tool
                ? FunctionResultParts(message, namesByCallId)
                : ModelOrUserParts(message, namesByCallId);

            AppendTurn(contents, RoleOf(message), parts);
        }

        return contents;
    }

    private static string RoleOf(ChatMessage message) =>
        message.Role == ChatRole.Assistant ? "model" : "user";

    private static string? SystemPrompt(IEnumerable<ChatMessage> messages) =>
        messages.FirstOrDefault(message => message.Role == ChatRole.System)?.Text;

    private static List<object> ModelOrUserParts(
        ChatMessage message,
        Dictionary<string, string> namesByCallId)
    {
        var parts = new List<object>();

        foreach (var content in ToolPayloads.AsContents(message.Contents))
        {
            switch (content)
            {
                case TextContent text:
                    parts.Add(Part("text", text.Text));
                    break;

                case FunctionCallContent call:
                    namesByCallId[call.CallId] = call.Name;
                    parts.Add(new Dictionary<string, object?>
                    {
                        ["functionCall"] = new Dictionary<string, object?>
                        {
                            ["name"] = call.Name,
                            ["args"] = ToolPayloads.ArgumentsOrDefault(call),
                        },
                    });
                    break;
            }
        }

        return parts;
    }

    private static List<object> FunctionResultParts(
        ChatMessage message,
        Dictionary<string, string> namesByCallId)
    {
        var parts = new List<object>();

        foreach (var result in message.Contents.OfType<FunctionResultContent>())
        {
            // Without the original name the API cannot match the response to the call, so
            // fall back to the id rather than dropping the result silently.
            var name = namesByCallId.TryGetValue(result.CallId, out var found) ? found : result.CallId;

            parts.Add(new Dictionary<string, object?>
            {
                ["functionResponse"] = new Dictionary<string, object?>
                {
                    ["name"] = name,
                    ["response"] = new Dictionary<string, object?>
                    {
                        ["result"] = DescribeResult(result),
                    },
                },
            });
        }

        return parts;
    }

    private static string DescribeResult(FunctionResultContent result) =>
        result.Exception is { } exception
            ? $"Error: {exception.Message}"
            : result.Result?.ToString() ?? string.Empty;

    private static Dictionary<string, object?> Part(string kind, string value) =>
        new(StringComparer.Ordinal) { [kind] = value };

    private static void AppendTurn(List<object> contents, string role, List<object> parts)
    {
        if (parts.Count == 0)
        {
            return;
        }

        if (contents.Count > 0 &&
            contents[^1] is Dictionary<string, object?> last &&
            last.TryGetValue("role", out var lastRole) &&
            lastRole as string == role &&
            last["parts"] is List<object> existing)
        {
            existing.AddRange(parts);
            return;
        }

        contents.Add(new Dictionary<string, object?>
        {
            ["role"] = role,
            ["parts"] = parts,
        });
    }

    internal static GeneratedContent ParseGenerateContent(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        var text = new StringBuilder();
        var toolCalls = new List<FunctionCallContent>();

        if (root.TryGetProperty("candidates", out var candidates) &&
            candidates.ValueKind == JsonValueKind.Array &&
            candidates.GetArrayLength() > 0 &&
            candidates[0].TryGetProperty("content", out var content) &&
            content.TryGetProperty("parts", out var parts) &&
            parts.ValueKind == JsonValueKind.Array)
        {
            var index = 0;

            foreach (var part in parts.EnumerateArray())
            {
                if (part.TryGetProperty("text", out var textElement))
                {
                    text.Append(textElement.GetString());
                    continue;
                }

                if (TryReadFunctionCall(part, index, out var toolCall))
                {
                    toolCalls.Add(toolCall);
                    index++;
                }
            }
        }

        var usage = TokenUsage.Empty;
        if (root.TryGetProperty("usageMetadata", out var usageMetadata))
        {
            usage = new TokenUsage(
                ReadInt(usageMetadata, "promptTokenCount"),
                ReadInt(usageMetadata, "candidatesTokenCount"));
        }

        return new GeneratedContent(text.ToString(), toolCalls, usage);
    }

    /// <summary>
    /// Reads a <c>functionCall</c> part. Gemini does not supply call ids, so a stable one
    /// is generated — the API is not asked for it back, and the session only needs the
    /// id to pair a result with its call.
    /// </summary>
    private static bool TryReadFunctionCall(JsonElement part, int index, out FunctionCallContent toolCall)
    {
        toolCall = null!;

        if (!part.TryGetProperty("functionCall", out var call))
        {
            return false;
        }

        var name = call.TryGetProperty("name", out var nameElement)
            ? nameElement.GetString() ?? string.Empty
            : string.Empty;

        var arguments = new Dictionary<string, object?>(StringComparer.Ordinal);

        if (call.TryGetProperty("args", out var args) && args.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in args.EnumerateObject())
            {
                arguments[property.Name] = ToolPayloads.ToClrValue(property.Value);
            }
        }

        toolCall = new FunctionCallContent($"gemini-call-{index}", name, arguments);
        return true;
    }

    private static int ReadInt(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.TryGetInt32(out var result) ? result : 0;
}
