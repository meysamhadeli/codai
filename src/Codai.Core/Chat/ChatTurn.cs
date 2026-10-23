namespace Codai.Core.Chat;

/// <summary>
/// One user request and the model response that answered it.
/// </summary>
/// <param name="Timestamp">When the turn was recorded.</param>
/// <param name="UserRequest">What the user asked, or the label describing a synthetic turn.</param>
/// <param name="AiResponse">What the model answered.</param>
/// <param name="Kind">
/// What the turn is. A conversation turn is what the user and the model said; a summary
/// turn is one a compaction wrote to stand in for turns that were removed. Keeping the two
/// apart is what lets a later compaction fold only what has grown since the last summary
/// instead of summarising a summary of a summary.
/// </param>
public sealed record ChatTurn(
    DateTimeOffset Timestamp,
    string UserRequest,
    string AiResponse,
    ChatTurnKind Kind = ChatTurnKind.Conversation)
{
    /// <summary>True when a compaction wrote this turn rather than the user and the model.</summary>
    public bool IsCompactionSummary => Kind == ChatTurnKind.CompactionSummary;
}

/// <summary>What a <see cref="ChatTurn"/> is.</summary>
public enum ChatTurnKind
{
    /// <summary>A request the user sent and the answer the model gave.</summary>
    Conversation,

    /// <summary>
    /// A summary a compaction wrote in place of earlier turns. It is carried forward in the
    /// prompt like any other turn, but a later compaction folds it rather than keeping it.
    /// </summary>
    CompactionSummary,
}

