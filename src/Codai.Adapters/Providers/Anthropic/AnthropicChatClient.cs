using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Codai.Configuration;
using Codai.Core.Abstractions;
using Codai.Core.Tokens;
using Codai.Providers.Internal;
using Microsoft.Extensions.AI;

namespace Codai.Providers.Anthropic;

/// <summary>
/// <see cref="IChatClient"/> for Anthropic's Messages API.
/// </summary>
/// <remarks>
/// <para>
/// Improvements over the original implementation: the system prompt is sent via the
/// top-level <c>system</c> field (not as a message), <c>max_tokens</c> is always
/// supplied, and input/output usage is accumulated across stream events.
/// </para>
/// <para>
/// Tool calling is implemented by hand because Anthropic is not OpenAI-compatible:
/// tools are advertised as <c>{name, description, input_schema}</c>, a model turn asks
/// for them with <c>tool_use</c> blocks, and results go back as <c>tool_result</c>
/// blocks inside a user turn. A streamed call has to be stitched together from its
/// <c>input_json_delta</c> fragments before it can be parsed.
/// </para>
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
            ProviderCatalog.Anthropic,
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
        var (system, turns) = BuildTurns(messages);

        var text = new StringBuilder();
        var contents = new List<AIContent>();
        var usage = TokenUsage.Empty;

        await foreach (var @event in StreamCoreAsync(system, turns, options, cancellationToken))
        {
            if (@event.Text is { Length: > 0 } delta)
            {
                text.Append(delta);
            }

            if (@event.ToolCall is { } toolCall)
            {
                contents.Add(toolCall);
            }

            if (@event.HasUsage)
            {
                usage = @event.Usage;
            }
        }

        if (text.Length > 0)
        {
            contents.Insert(0, new TextContent(text.ToString()));
        }

        return new ChatResponse(new ChatMessage(ChatRole.Assistant, contents))
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
        var (system, turns) = BuildTurns(messages);

        await foreach (var @event in StreamCoreAsync(system, turns, options, cancellationToken))
        {
            if (@event.Text is { Length: > 0 } delta)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, delta);
            }

            if (@event.ToolCall is { } toolCall)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, [toolCall]);
            }

            if (@event.HasUsage)
            {
                yield return new ChatResponseUpdate(
                    ChatRole.Assistant,
                    [new UsageContent(new UsageDetails
                    {
                        InputTokenCount = @event.Usage.InputTokens,
                        OutputTokenCount = @event.Usage.OutputTokens,
                        TotalTokenCount = @event.Usage.TotalTokens,
                    })]);
            }
        }
    }

    /// <summary>
    /// One step of the streamed response: a text delta, a completed tool call, an
    /// updated usage total, or nothing at all.
    /// </summary>
    private readonly record struct StreamEvent(
        string? Text,
        FunctionCallContent? ToolCall,
        TokenUsage Usage,
        bool HasUsage);

    private async IAsyncEnumerable<StreamEvent> StreamCoreAsync(
        string? system,
        IReadOnlyList<object> turns,
        ChatOptions? options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var requestBody = new Dictionary<string, object?>
        {
            ["model"] = options?.ModelId ?? _model,
            ["max_tokens"] = options?.MaxOutputTokens ?? _maxTokens,
            ["stream"] = true,
            ["messages"] = turns,
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

        var functions = ToolPayloads.Functions(options?.Tools);
        if (functions.Count > 0)
        {
            requestBody["tools"] = functions
                .Select(function => new Dictionary<string, object?>
                {
                    ["name"] = function.Name,
                    ["description"] = function.Description,
                    ["input_schema"] = function.JsonSchema,
                })
                .ToList();
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

        // A tool call arrives in pieces: a start marker names the call, a run of
        // input_json_delta chunks carries its arguments, then a stop marker closes it.
        var pending = new Dictionary<int, PendingToolCall>();

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

                case "content_block_start":
                    StartToolCall(root, pending);
                    break;

                case "content_block_delta":
                    if (TryReadTextDelta(root, out var text))
                    {
                        yield return new StreamEvent(text, null, default, false);
                    }

                    AppendToolCallArguments(root, pending);
                    break;

                case "content_block_stop":
                    if (TryCompleteToolCall(root, pending, out var toolCall))
                    {
                        yield return new StreamEvent(null, toolCall, default, false);
                    }

                    break;

                case "message_delta":
                    outputTokens = ReadNested(root, ["usage", "output_tokens"], outputTokens);
                    yield return new StreamEvent(null, null, new TokenUsage(inputTokens, outputTokens), true);
                    break;

                case "message_stop":
                    yield return new StreamEvent(null, null, new TokenUsage(inputTokens, outputTokens), true);
                    yield break;
            }
        }

        yield return new StreamEvent(null, null, new TokenUsage(inputTokens, outputTokens), true);
    }

    /// <summary>A call being assembled while its <c>input_json_delta</c> fragments arrive.</summary>
    private sealed class PendingToolCall
    {
        public string Id { get; init; } = string.Empty;

        public string Name { get; init; } = string.Empty;

        public StringBuilder Arguments { get; } = new();
    }

    private static void StartToolCall(JsonElement root, Dictionary<int, PendingToolCall> pending)
    {
        if (!TryReadIndex(root, out var index) ||
            !root.TryGetProperty("content_block", out var block) ||
            !block.TryGetProperty("type", out var blockType) ||
            blockType.GetString() != "tool_use")
        {
            return;
        }

        pending[index] = new PendingToolCall
        {
            Id = block.TryGetProperty("id", out var id) ? id.GetString() ?? string.Empty : string.Empty,
            Name = block.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty,
        };
    }

    private static bool TryReadTextDelta(JsonElement root, out string text)
    {
        text = string.Empty;

        if (!root.TryGetProperty("delta", out var delta) ||
            !delta.TryGetProperty("text", out var textElement))
        {
            return false;
        }

        text = textElement.GetString() ?? string.Empty;
        return text.Length > 0;
    }

    private static void AppendToolCallArguments(JsonElement root, Dictionary<int, PendingToolCall> pending)
    {
        if (!TryReadIndex(root, out var index) ||
            !pending.TryGetValue(index, out var toolCall) ||
            !root.TryGetProperty("delta", out var delta) ||
            !delta.TryGetProperty("partial_json", out var partial))
        {
            return;
        }

        toolCall.Arguments.Append(partial.GetString());
    }

    private static bool TryCompleteToolCall(
        JsonElement root,
        Dictionary<int, PendingToolCall> pending,
        out FunctionCallContent toolCall)
    {
        toolCall = null!;

        if (!TryReadIndex(root, out var index) || !pending.Remove(index, out var completed))
        {
            return false;
        }

        toolCall = new FunctionCallContent(
            completed.Id,
            completed.Name,
            ParseArguments(completed.Arguments));

        return true;
    }

    /// <summary>
    /// Parses accumulated argument JSON. A truncated or malformed fragment still yields a
    /// call, with no arguments, so the model gets feedback rather than an exception.
    /// </summary>
    private static Dictionary<string, object?> ParseArguments(StringBuilder json)
    {
        if (json.Length == 0)
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal);
        }

        try
        {
            using var document = JsonDocument.Parse(json.ToString());

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new Dictionary<string, object?>(StringComparer.Ordinal);
            }

            return document.RootElement
                .EnumerateObject()
                .ToDictionary(
                    property => property.Name,
                    property => ToolPayloads.ToClrValue(property.Value),
                    StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal);
        }
    }

    private static bool TryReadIndex(JsonElement root, out int index)
    {
        index = 0;

        return root.TryGetProperty("index", out var element) && element.TryGetInt32(out index);
    }

    /// <summary>
    /// Converts MEAI messages into the top-level <c>system</c> string plus the ordered
    /// Anthropic turns. Consecutive turns of the same role are merged: the API requires
    /// alternating roles, and tool results can arrive as several messages.
    /// </summary>
    private static (string? System, IReadOnlyList<object> Turns) BuildTurns(IEnumerable<ChatMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        string? system = null;
        var turns = new List<object>();

        foreach (var message in messages)
        {
            if (message.Role == ChatRole.System)
            {
                system ??= message.Text;
                continue;
            }

            if (message.Role == ChatRole.Tool)
            {
                AppendTurn(turns, "user", ToolResultBlocks(message));
                continue;
            }

            // Assistant and user turns carry the same shape: text and tool_use blocks.
            AppendTurn(
                turns,
                message.Role == ChatRole.Assistant ? "assistant" : "user",
                ContentBlocks(message));
        }

        return (system, turns);
    }

    private static List<object> ContentBlocks(ChatMessage message)
    {
        var blocks = new List<object>();

        foreach (var content in ToolPayloads.AsContents(message.Contents))
        {
            switch (content)
            {
                case TextContent text:
                    blocks.Add(new Dictionary<string, object?>
                    {
                        ["type"] = "text",
                        ["text"] = text.Text,
                    });
                    break;

                case FunctionCallContent call:
                    blocks.Add(new Dictionary<string, object?>
                    {
                        ["type"] = "tool_use",
                        ["id"] = call.CallId,
                        ["name"] = call.Name,
                        ["input"] = ToolPayloads.ArgumentsOrDefault(call),
                    });
                    break;
            }
        }

        return blocks;
    }

    private static List<object> ToolResultBlocks(ChatMessage message)
    {
        var blocks = new List<object>();

        foreach (var result in message.Contents.OfType<FunctionResultContent>())
        {
            blocks.Add(new Dictionary<string, object?>
            {
                ["type"] = "tool_result",
                ["tool_use_id"] = result.CallId,
                ["content"] = DescribeResult(result),
            });
        }

        return blocks;
    }

    /// <summary>
    /// Renders a tool result as text. A tool that threw is reported as an error, so the
    /// model can see the failure and correct itself.
    /// </summary>
    private static string DescribeResult(FunctionResultContent result) =>
        result.Exception is { } exception
            ? $"Error: {exception.Message}"
            : result.Result?.ToString() ?? string.Empty;

    private static void AppendTurn(List<object> turns, string role, List<object> blocks)
    {
        if (blocks.Count == 0)
        {
            return;
        }

        if (turns.Count > 0 &&
            turns[^1] is Dictionary<string, object?> last &&
            last.TryGetValue("role", out var lastRole) &&
            lastRole as string == role &&
            last["content"] is List<object> existing)
        {
            existing.AddRange(blocks);
            return;
        }

        turns.Add(new Dictionary<string, object?>
        {
            ["role"] = role,
            ["content"] = blocks,
        });
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
