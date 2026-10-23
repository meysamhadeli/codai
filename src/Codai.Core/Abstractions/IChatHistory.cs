
using Codai.Core.Chat;
namespace Codai.Core.Abstractions;

/// <summary>
/// Session-scoped conversation memory.
/// </summary>
public interface IChatHistory
{
    IReadOnlyList<ChatTurn> Turns { get; }

    void Add(ChatTurn turn);

    /// <summary>
    /// Puts <paramref name="turn"/> in the place of the turn at <paramref name="index"/>,
    /// without changing how many turns the conversation has.
    /// </summary>
    /// <remarks>
    /// This is what shortens a conversation without summarising it: an old turn keeps its
    /// place and its request, and only the bulk of its response is cleared. Adding the turn
    /// again would move it to the end, where it would read as the most recent thing the model
    /// said.
    /// </remarks>
    /// <param name="index">Position of the turn to replace.</param>
    /// <param name="turn">What replaces it.</param>
    void Replace(int index, ChatTurn turn);

    void Clear();
}
