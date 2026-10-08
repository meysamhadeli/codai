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

    /// <summary>Splits the message list into a system instruction and a user payload.</summary>
    protected static (string? SystemPrompt, string UserContent) Split(IEnumerable<ChatMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        string? system = null;
        var user = new List<string>();

        foreach (var message in messages)
        {
            if (message.Role == ChatRole.System && system is null)
            {
                system = message.Text;
            }
            else if (!string.IsNullOrEmpty(message.Text))
            {
                user.Add(message.Text);
            }
        }

        return (system, string.Join("\n\n", user));
    }
}
