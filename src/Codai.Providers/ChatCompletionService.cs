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
    private readonly IList<AITool>? _tools;
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

        var tools = toolExecutor?.Tools ?? [];

        _tools = tools.Count == 0
            ? null
            : [.. tools.Select(tool => (AITool)new ToolAIFunction(tool, toolExecutor!))];

        _client = _tools is null
            ? client
            : new ChatClientBuilder(client).UseFunctionInvocation().Build();
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

        var options = new ChatOptions
        {
            ModelId = ModelName,
            Temperature = _temperature,
            Reasoning = _reasoning,
            MaxOutputTokens = _maxTokens > 0 ? _maxTokens : null,
            Tools = _tools,
        };

        if (_stream)
        {
            await foreach (var update in StreamChunksAsync(messages, options, cancellationToken).ConfigureAwait(false))
            {
                yield return update;
            }

            yield break;
        }

        var response = await GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        var usage = Internal.AiContentHelpers.ExtractUsage(response);

        var text = response.Text;
        if (!string.IsNullOrEmpty(text))
        {
            yield return ChatStreamUpdate.Content(text);
        }

        yield return ChatStreamUpdate.Completed(usage);
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
