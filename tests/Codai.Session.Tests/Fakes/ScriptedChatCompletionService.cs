using System.Runtime.CompilerServices;
using Codai.Core.Abstractions;
using Codai.Core.Chat;
using Codai.Core.Tokens;

namespace Codai.Session.Tests.Fakes;

/// <summary>
/// Returns pre-scripted responses in order, one per request, and records every
/// request that was sent.
/// </summary>
public sealed class ScriptedChatCompletionService : IChatCompletionService
{
    private readonly Queue<(string Text, TokenUsage Usage)> _responses = new();
    private readonly Queue<(string Text, TokenUsage Usage, Exception Failure)> _partialRuns = new();

    public List<ChatRequest> Requests { get; } = [];

    public Queue<Exception> Failures { get; } = new();

    public string ProviderName => "fake";

    public string ModelName => "fake-model";

    public void Enqueue(string text, TokenUsage usage = default) => _responses.Enqueue((text, usage));

    /// <summary>
    /// Queues a run that answers with <paramref name="text" />, reports what it spent, and
    /// then fails. A run can spend tokens and still end badly, and the caller has to count
    /// what was spent rather than infer it from the outcome.
    /// </summary>
    public void EnqueueThenFail(string text, TokenUsage usage, Exception failure) =>
        _partialRuns.Enqueue((text, usage, failure));

    public async IAsyncEnumerable<ChatStreamUpdate> StreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();

        Requests.Add(request);

        if (Failures.Count > 0)
        {
            throw Failures.Dequeue();
        }

        if (_partialRuns.Count > 0)
        {
            var (partial, cost, failure) = _partialRuns.Dequeue();

            foreach (var chunk in partial.Chunk(16))
            {
                yield return ChatStreamUpdate.Content(new string(chunk));
            }

            yield return ChatStreamUpdate.Completed(cost);

            throw failure;
        }

        if (_responses.Count == 0)
        {
            yield return ChatStreamUpdate.Completed();
            yield break;
        }

        var (text, usage) = _responses.Dequeue();

        foreach (var chunk in text.Chunk(16))
        {
            yield return ChatStreamUpdate.Content(new string(chunk));
        }

        yield return ChatStreamUpdate.Completed(usage);
    }
}
