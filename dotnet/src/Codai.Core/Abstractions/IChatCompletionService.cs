using Codai.Core.Models;

namespace Codai.Core.Abstractions;

/// <summary>
/// A streaming chat completion backend (OpenAI, Anthropic, Ollama, ...).
/// </summary>
public interface IChatCompletionService
{
    /// <summary>Human readable provider name, e.g. <c>openai</c>.</summary>
    string ProviderName { get; }

    /// <summary>Model identifier, e.g. <c>gpt-4o</c>.</summary>
    string ModelName { get; }

    /// <summary>
    /// Streams a chat completion. Implementations must throw
    /// <see cref="ChatProviderException"/> for provider/transport failures.
    /// </summary>
    IAsyncEnumerable<ChatStreamUpdate> StreamAsync(
        ChatRequest request,
        CancellationToken cancellationToken = default);
}
