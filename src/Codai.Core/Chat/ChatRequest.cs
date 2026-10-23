namespace Codai.Core.Chat;

/// <summary>
/// A fully rendered request ready to be sent to a chat provider.
/// </summary>
/// <param name="SystemPrompt">The system prompt (context + template + history).</param>
/// <param name="UserMessage">The user message (the current request).</param>
/// <param name="AllowedTools">
/// Names of the tools this single request may call, taken from the session's
/// <c>AgentMode</c>. <c>null</c> means every tool the service was built with, which is
/// what a caller that does not care about modes gets.
/// </param>
/// <remarks>
/// Tool availability is part of the request rather than of the client so that switching
/// mode does not have to rebuild the provider: the same client serves a read-only
/// question and an editing task.
/// </remarks>
public sealed record ChatRequest(
    string SystemPrompt,
    string UserMessage,
    IReadOnlyCollection<string>? AllowedTools = null);
