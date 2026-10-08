using Codai.Core.Tokens;
namespace Codai.Core.Chat;

/// <summary>
/// A single chunk produced while streaming a chat response.
/// </summary>
/// <param name="ContentDelta">The text produced in this chunk, if any.</param>
/// <param name="Usage">Token usage, populated on the final chunk when available.</param>
/// <param name="IsCompleted">True for the final chunk of the stream.</param>
public readonly record struct ChatStreamUpdate(string? ContentDelta, TokenUsage Usage, bool IsCompleted)
{
    public static ChatStreamUpdate Content(string delta) => new(delta, TokenUsage.Empty, false);

    public static ChatStreamUpdate Completed(TokenUsage usage = default) => new(null, usage, true);
}
