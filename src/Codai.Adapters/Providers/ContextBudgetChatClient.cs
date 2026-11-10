using System.Text.Json;
using Codai.Core.Abstractions;
using Codai.Core.Context;
using Microsoft.Extensions.AI;

namespace Codai.Providers;

internal sealed class ContextBudgetChatClient(IChatClient innerClient, ResolvedProviderOptions provider, int windowCap)
    : DelegatingChatClient(innerClient)
{
    private const string Omitted = "[Earlier tool output omitted to fit model context. Re-read relevant files or repeat a targeted search if needed.]";
    private const string Truncated = "\n[Tool output compacted to fit model context. Re-read targeted ranges if needed.]\n";
    private readonly Dictionary<string, string> _replacements = new(StringComparer.Ordinal);

    public override Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        CancellationToken cancellationToken = default) => base.GetResponseAsync(Prepare(messages, options, cancellationToken), options, cancellationToken);

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        base.GetStreamingResponseAsync(Prepare(messages, options, cancellationToken), options, cancellationToken);

    private List<ChatMessage> Prepare(IEnumerable<ChatMessage> messages, ChatOptions? options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sent = messages.ToList();
        var protectedCalls = sent.SelectMany(message => message.Contents).OfType<FunctionCallContent>()
            .Where(call => string.Equals(call.Name, "read_skill", StringComparison.OrdinalIgnoreCase))
            .Select(call => call.CallId).ToHashSet(StringComparer.Ordinal);
        bool IsProtected(FunctionResultContent result) => protectedCalls.Contains(result.CallId) ||
            result.Result is string text && text.StartsWith("Tool was not executed. Review", StringComparison.Ordinal);
        for (var index = 0; index < sent.Count; index++)
        {
            foreach (var result in sent[index].Contents.OfType<FunctionResultContent>().ToArray())
            {
                if (_replacements.TryGetValue(result.CallId, out var replacement))
                {
                    Replace(sent, index, result, replacement);
                }
            }
        }
        var window = windowCap > 0 ? Math.Min(windowCap, provider.ContextWindowTokens) : provider.ContextWindowTokens;
        var schemas = options?.Tools?.OfType<AIFunction>().Sum(tool => TokenEstimator.EstimateRequest(tool.Name,
            tool.Description, tool.JsonSchema.GetRawText()) + TokenEstimator.PerToolOverhead) ?? 0;
        var reserve = Math.Min(CompactionPolicy.DefaultReserveTokens, window / 4) + (long)provider.OutputReserveTokens;
        var budget = Math.Max(1, (long)((window - reserve - schemas) * ContextPressure.DefaultSafetyMargin));
        var used = TokenEstimator.PerRequestOverhead + sent.Sum(message => (long)Measure(message));
        var latestCall = sent.FindLastIndex(message => message.Contents.OfType<FunctionCallContent>().Any());
        for (var index = 0; index < sent.Count && used > budget; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var result in sent[index].Contents.OfType<FunctionResultContent>().ToArray())
            {
                if (index >= latestCall || IsProtected(result) || result.Result is not string text || text.Length <= Omitted.Length)
                {
                    continue;
                }
                used -= TokenEstimator.Estimate(text) - TokenEstimator.Estimate(Omitted);
                Replace(sent, index, result, Omitted);
                if (used <= budget)
                {
                    break;
                }
            }
        }
        for (var index = 0; index < sent.Count && used > budget; index++)
        {
            foreach (var result in sent[index].Contents.OfType<FunctionResultContent>().ToArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsProtected(result) || result.Result is not string text || text.Length <= Truncated.Length)
                {
                    continue;
                }
                var room = Math.Max(0, budget - used + TokenEstimator.Estimate(text));
                var characters = (int)Math.Min(text.Length, Math.Max(0, room * 4 - Truncated.Length - 64));
                var head = characters / 2;
                if (head > 0 && char.IsHighSurrogate(text[head - 1]))
                {
                    head--;
                }
                var tail = text.Length - (characters - head);
                if (tail < text.Length && char.IsLowSurrogate(text[tail]))
                {
                    tail++;
                }
                var replacement = text[..head] + Truncated + text[tail..];
                used -= TokenEstimator.Estimate(text) - TokenEstimator.Estimate(replacement);
                Replace(sent, index, result, replacement);
                if (used <= budget)
                {
                    break;
                }
            }
        }
        if (used > budget)
        {
            throw new ChatProviderException($"Model context window exceeded for {provider.Model}. Reduce attached context or compact the conversation; protected instructions and tool calls were retained.");
        }
        return sent;
    }

    private void Replace(List<ChatMessage> messages, int index, FunctionResultContent result, string replacement)
    {
        var original = messages[index];
        var contents = original.Contents.ToList();
        contents[contents.IndexOf(result)] = new FunctionResultContent(result.CallId, replacement);
        messages[index] = new ChatMessage(original.Role, contents)
        {
            AuthorName = original.AuthorName,
            AdditionalProperties = original.AdditionalProperties,
        };
        _replacements[result.CallId] = replacement;
    }

    private static int Measure(ChatMessage message) => TokenEstimator.PerMessageOverhead + message.Contents.Sum(content => content switch
    {
        TextContent text => TokenEstimator.Estimate(text.Text),
        FunctionResultContent result => TokenEstimator.Estimate(result.Result as string ?? JsonSerializer.Serialize(result.Result, AIJsonUtilities.DefaultOptions)),
        FunctionCallContent call => TokenEstimator.EstimateRequest(call.Name, JsonSerializer.Serialize(call.Arguments, AIJsonUtilities.DefaultOptions)),
        _ => 1_024,
    });
}