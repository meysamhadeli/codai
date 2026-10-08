using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Codai.Providers.Tests.Fakes;

/// <summary>
/// An <see cref="IChatClient"/> whose replies are scripted turn by turn, so a
/// multi-round conversation (ask for a tool, then answer using its result) can be
/// exercised without a network.
/// </summary>
internal sealed class ToolCallingChatClient : IChatClient
{
    private readonly Queue<IReadOnlyList<ChatResponseUpdate>> _turns;

    public ToolCallingChatClient(params IReadOnlyList<ChatResponseUpdate>[] turns) => _turns = new(turns);

    /// <summary>Number of requests the client was asked to serve.</summary>
    public int CallCount { get; private set; }

    /// <summary>The messages seen on each request, in order.</summary>
    public List<IReadOnlyList<ChatMessage>> SeenMessages { get; } = [];

    public ChatOptions? LastOptions { get; private set; }

    public ChatClientMetadata Metadata { get; } = new("tool-calling");

    /// <summary>A turn that asks for a tool call, with the arguments a model would send.</summary>
    public static ChatResponseUpdate[] CallsTool(
        string callId,
        string name,
        IDictionary<string, object?> arguments) =>
        [new ChatResponseUpdate(ChatRole.Assistant, [new FunctionCallContent(callId, name, arguments)])];

    /// <summary>A turn that answers with text.</summary>
    public static ChatResponseUpdate[] Says(string text) =>
        [new ChatResponseUpdate(ChatRole.Assistant, text)];

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var contents = new List<AIContent>();

        foreach (var update in Next(messages, options))
        {
            contents.AddRange(update.Contents);
        }

        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, contents)));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var update in Next(messages, options))
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

    private IReadOnlyList<ChatResponseUpdate> Next(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options)
    {
        CallCount++;
        SeenMessages.Add(messages.ToList());
        LastOptions = options;

        return _turns.Count > 0 ? _turns.Dequeue() : Says("done");
    }
}
