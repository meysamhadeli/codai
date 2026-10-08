namespace Codai.Core.Chat;

/// <summary>
/// A fully rendered request ready to be sent to a chat provider.
/// </summary>
/// <param name="SystemPrompt">The system prompt (context + template + history).</param>
/// <param name="UserMessage">The user message (the current request).</param>
public sealed record ChatRequest(string SystemPrompt, string UserMessage);
