
using Codai.Core.Chat;
namespace Codai.Core.Abstractions;

/// <summary>
/// Session-scoped conversation memory.
/// </summary>
public interface IChatHistory
{
    IReadOnlyList<ChatTurn> Turns { get; }

    void Add(ChatTurn turn);

    void Clear();
}
