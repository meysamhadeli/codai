using System.Runtime.CompilerServices;
using Codai.Core.Abstractions;
using Codai.Core.Chat;
using Codai.Core.Tokens;
using Codai.Core.Tools;
using Codai.Providers.Tools;
using Microsoft.Extensions.AI;

namespace Codai.Providers;

/// <summary>
/// Adapts a Microsoft.Extensions.AI <see cref="IChatClient"/> to Codai's
/// <see cref="IChatCompletionService"/>, translating streaming updates and
/// surfacing token usage.
/// </summary>
/// <remarks>
/// When an <see cref="IToolExecutor"/> is supplied, the client is wrapped in
/// <see cref="FunctionInvokingChatClient"/>. That client runs the
/// call-tool/append-result/re-ask loop, so this type stays a thin translator and the
/// loop is not reimplemented per provider.
/// </remarks>
public sealed class ChatCompletionService : IChatCompletionService
{
    private readonly IChatClient _client;
    private readonly IReadOnlyList<ITool>? _tools;
    private readonly IToolExecutor? _toolExecutor;
    private readonly float? _temperature;
    private readonly ReasoningOptions? _reasoning;
    private readonly int _maxTokens;
    private readonly bool _stream;

    public ChatCompletionService(
        IChatClient client,
        ResolvedProviderOptions options,
        IToolExecutor? toolExecutor = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);

        ProviderName = options.Provider;
        ModelName = options.Model;
        _temperature = options.Temperature;
        _reasoning = ReasoningEffortMap.ToReasoningOptions(options.ReasoningEffort);
        _maxTokens = options.MaxTokens;
        _stream = options.Stream;
        _toolExecutor = toolExecutor;

        var tools = toolExecutor?.Tools ?? [];

        _tools = tools.Count == 0
            ? null
            : tools;

        _client = _tools is null
            ? client
            : new ChatClientBuilder(client)
                .UseFunctionInvocation(configure: invocation => invocation.AllowConcurrentInvocation = false)
                .Build();
    }

    public string ProviderName { get; }

    public string ModelName { get; }

    public async IAsyncEnumerable<ChatStreamUpdate> StreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var messages = new ChatMessage[]
        {
            new(ChatRole.System, request.SystemPrompt),
            new(ChatRole.User, request.UserMessage),
        };

        ToolResult? stoppedResult = null;
        var options = new ChatOptions
        {
            ModelId = ModelName,
            Temperature = _temperature,
            Reasoning = _reasoning,
            MaxOutputTokens = _maxTokens > 0 ? _maxTokens : null,
            Tools = SelectTools(request.AllowedTools, result => stoppedResult = result),
        };

        if (_stream)
        {
            var completionWritten = false;
            await foreach (var update in StreamChunksAsync(messages, options, cancellationToken).ConfigureAwait(false))
            {
                if (stoppedResult is { IsSuccess: false } failed)
                {
                    throw new ChatProviderException(failed.Content);
                }

                if (stoppedResult is { IsSuccess: true } completed)
                {
                    if (!completionWritten)
                    {
                        yield return ChatStreamUpdate.Content(completed.Content);
                        completionWritten = true;
                    }

                    if (update.ContentDelta is null)
                    {
                        yield return update;
                    }

                    continue;
                }

                yield return update;
            }

            yield break;
        }

        var response = await GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        if (stoppedResult is { IsSuccess: false } failure)
        {
            throw new ChatProviderException(failure.Content);
        }

        var usage = Internal.AiContentHelpers.ExtractUsage(response);

        var text = stoppedResult?.Content ?? response.Text;
        if (!string.IsNullOrEmpty(text))
        {
            yield return ChatStreamUpdate.Content(text);
        }

        yield return ChatStreamUpdate.Completed(usage);
    }

    /// <summary>
    /// The tools this request may call.
    /// </summary>
    /// <remarks>
    /// A request that names a subset gets only that subset, so a tool the session does
    /// not offer for this request is never advertised to the model in the first place.
    /// That matters more than politeness: a tool that is absent from the list cannot be
    /// called, so the restriction holds even for a provider that would otherwise accept a
    /// call for a function it was never given.
    /// </remarks>
    private List<AITool>? SelectTools(IReadOnlyCollection<string>? allowedTools, Action<ToolResult> onStopped)
    {
        if (_tools is null)
        {
            return null;
        }

        var review = new ToolReviewState();
        var selected = _tools
            .Where(tool => allowedTools is null || allowedTools.Contains(tool.Name, StringComparer.OrdinalIgnoreCase))
            .Select(tool => (AITool)new ToolAIFunction(tool, _toolExecutor!, onStopped, review))
            .ToList();

        // An empty selection means "no tools", which is different from "no restriction".
        return selected.Count == 0 ? null : selected;
    }

    /// <summary>Streams the response as ordered <see cref="ChatStreamUpdate"/> chunks.</summary>
    private async IAsyncEnumerable<ChatStreamUpdate> StreamChunksAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var enumerator = GetStream(messages, options, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);

        var usage = TokenUsage.Empty;

        try
        {
            while (true)
            {
                ChatResponseUpdate update;

                try
                {
                    if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                    {
                        break;
                    }

                    update = enumerator.Current;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (ChatProviderException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    throw new ChatProviderException(
                        $"{ProviderName} request failed: {ex.Message}", ex);
                }

                var updateUsage = Internal.AiContentHelpers.ExtractUsage(update);
                if (updateUsage.HasValue)
                {
                    usage = updateUsage;
                }

                if (IsReasoning(update))
                {
                    yield return ChatStreamUpdate.Reasoning();
                }
                var text = update.Text;
                if (!string.IsNullOrEmpty(text))
                {
                    yield return ChatStreamUpdate.Content(text);
                }
            }
        }
        finally
        {
            await enumerator.DisposeAsync().ConfigureAwait(false);
        }

        yield return ChatStreamUpdate.Completed(usage);
    }

    private static bool IsReasoning(ChatResponseUpdate update)
    {
        if (update.Contents.OfType<TextReasoningContent>().Any())
        {
            return true;
        }
        if (!string.IsNullOrEmpty(update.Text) || update.RawRepresentation is not OpenAI.Chat.StreamingChatCompletionUpdate raw)
        {
            return false;
        }
        using var document = System.Text.Json.JsonDocument.Parse(System.ClientModel.Primitives.ModelReaderWriter.Write(raw).ToMemory());
        return document.RootElement.TryGetProperty("choices", out var choices) && choices.ValueKind == System.Text.Json.JsonValueKind.Array &&
            choices.EnumerateArray().Any(choice => choice.TryGetProperty("delta", out var delta) &&
                delta.TryGetProperty("reasoning_content", out var reasoning) && reasoning.ValueKind == System.Text.Json.JsonValueKind.String &&
                !string.IsNullOrEmpty(reasoning.GetString()));
    }

    /// <summary>Sends a single non-streamed request.</summary>
    private async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _client
                .GetResponseAsync(messages, options, cancellationToken)
                .ConfigureAwait(false) ?? new ChatResponse([]);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ChatProviderException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ChatProviderException(
                $"{ProviderName} request failed: {ex.Message}", ex);
        }
    }

    private IAsyncEnumerable<ChatResponseUpdate> GetStream(
        IEnumerable<ChatMessage> messages,
        ChatOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            return _client.GetStreamingResponseAsync(messages, options, cancellationToken);
        }
        catch (Exception ex)
        {
            throw new ChatProviderException(
                $"{ProviderName} request could not be started: {ex.Message}", ex);
        }
    }
}
