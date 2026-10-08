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
}
