namespace Codai.Core.Abstractions;

/// <summary>
/// Raised when a chat provider fails to produce a response.
/// </summary>
public sealed class ChatProviderException : Exception
{
    public ChatProviderException(string message)
        : base(message)
    {
    }

    public ChatProviderException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// True when the provider refused the request because it was longer than the model's
    /// context window.
    /// </summary>
    /// <remarks>
    /// A caller can act on this: the conversation is not wrong, it is merely too long, so
    /// shortening it and sending the request again is worth one attempt. Every other failure
    /// is not a length problem and retrying it would only spend the same request twice.
    /// </remarks>
    public bool IsContextOverflow => ContextOverflowDetector.Matches(this);
}
