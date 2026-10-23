using Microsoft.Extensions.AI;

namespace Codai.Providers.Internal;

/// <summary>
/// Base class for hand-written <see cref="IChatClient"/> implementations
/// (Anthropic, Gemini) that speak non-OpenAI wire formats.
/// </summary>
internal abstract class HttpChatClientBase : IChatClient
{
    protected HttpChatClientBase(string providerName, string defaultModelId, Uri? providerUri = null)
    {
        Metadata = new ChatClientMetadata(providerName, providerUri, defaultModelId);
    }

    public ChatClientMetadata Metadata { get; }

    public abstract Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default);

    public abstract IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default);

    public virtual object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        return serviceKey is null && serviceType.IsInstanceOfType(this)
            ? this
            : null;
    }

    public virtual void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
