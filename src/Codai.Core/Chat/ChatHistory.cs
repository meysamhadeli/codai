using Codai.Core.Abstractions;

namespace Codai.Core.Chat;

/// <summary>
/// In-memory, session-scoped chat history.
/// </summary>
public sealed class ChatHistory : IChatHistory
{
    private readonly List<ChatTurn> _turns = [];

    public IReadOnlyList<ChatTurn> Turns => _turns;

    public void Add(ChatTurn turn)
    {
        ArgumentNullException.ThrowIfNull(turn);
        _turns.Add(turn);
    }

    public void Replace(int index, ChatTurn turn)
    {
        ArgumentNullException.ThrowIfNull(turn);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _turns.Count);

        _turns[index] = turn;
    }

    public void Clear() => _turns.Clear();
}
