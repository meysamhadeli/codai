using System.Runtime.CompilerServices;
using Codai.Core.Models;
using Microsoft.Extensions.AI;

namespace Codai.Providers.Tests.Fakes;

/// <summary>
/// Scriptable <see cref="IChatClient"/> used to exercise the Codai adapter without
/// touching the network.
/// </summary>
internal sealed class ScriptedChatClient : IChatClient
{
    private readonly IReadOnlyList<ChatResponseUpdate> _updates;
    private readonly Exception? _failure;

    public ScriptedChatClient(IReadOnlyList<ChatResponseUpdate> updates, Exception? failure = null)
    {
        _updates = updates;
        _failure = failure;
    }

    public ChatOptions? CapturedOptions { get; private set; }

    public IReadOnlyList<ChatMessage> CapturedMessages { get; private set; } = [];

    public ChatClientMetadata Metadata { get; } = new("scripted");

    public static ScriptedChatClient Content(params string[] chunks) =>
        new(chunks.Select(chunk => new ChatResponseUpdate(ChatRole.Assistant, chunk)).ToList());

    public static ScriptedChatClient WithUsage(
        TokenUsage usage,
        params string[] chunks)
    {
        var updates = chunks.Select(chunk => new ChatResponseUpdate(ChatRole.Assistant, chunk)).ToList();
        updates.Add(new ChatResponseUpdate(
            ChatRole.Assistant,
            [new UsageContent(new UsageDetails
            {
                InputTokenCount = usage.InputTokens,
                OutputTokenCount = usage.OutputTokens,
                TotalTokenCount = usage.TotalTokens,
            })]));

        return new ScriptedChatClient(updates);
    }

    public static ScriptedChatClient Failing(Exception failure) => new([], failure);

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        CapturedMessages = messages.ToList();
        CapturedOptions = options;

        if (_failure is not null)
        {
            throw _failure;
        }

        // Re-materialise the scripted stream as a single response, mirroring how a
        // real provider answers a non-streamed request.
        var contents = new List<AIContent>();
        UsageDetails? usage = null;

        foreach (var update in _updates)
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                contents.Add(new TextContent(update.Text));
            }

            var updateUsage = update.Contents.OfType<UsageContent>().FirstOrDefault()?.Details;
            if (updateUsage is not null)
            {
                usage = updateUsage;
            }
        }

        if (usage is not null)
        {
            contents.Add(new UsageContent(usage));
        }

        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, contents)));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        CapturedMessages = messages.ToList();
        CapturedOptions = options;

        if (_failure is not null)
        {
            throw _failure;
        }

        foreach (var update in _updates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return update;
            await Task.Yield();
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}
