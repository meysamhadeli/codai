using Codai.Core.Context;

namespace Codai.Core.Chat;

/// <summary>
/// Shortens a conversation without asking a model to, by keeping the turns that carry the
/// intent and dropping the ones in the middle.
/// </summary>
/// <remarks>
/// <para>
/// Summarisation is a model call, and a model call can return nothing, return something
/// longer than what it was given, or fail outright. When that happens the choice is
/// between a conversation that is too long and a conversation that is shorter but missing
/// its middle. This is the second, which is the lesser of the two: a session over its
/// window cannot answer at all, and the archive still holds what was dropped.
/// </para>
/// <para>
/// The ends are what survives and the middle is what goes. The newest turns are what the
/// next request is built on, and the first turn says what the work is for — it is the one
/// turn a later request cannot reconstruct from anything else. Those two are paid for
/// before anything else is considered, so there is always a beginning and an end even when
/// there is nothing in between.
/// </para>
/// <para>
/// The contract is narrow on purpose: either the result is strictly smaller than the
/// conversation it was given, or the conversation is returned unchanged. A caller can then
/// treat "same list" as "there was nothing to save" and leave the history alone, which is
/// the only safe thing to do when shortening it would not have shortened it.
/// </para>
/// </remarks>
public static class ConversationDigest
{
    /// <summary>
    /// Longest a single turn is kept, whatever the budget. One pasted file must not be able
    /// to consume the whole of it and push every other turn out.
    /// </summary>
    public const int MaxTurnCharacters = 4_000;

    /// <summary>Smallest budget a pack is given, so a small history is not reduced to nothing.</summary>
    public const int MinimumBudgetTokens = 2_000;

    /// <summary>
    /// Text used as the user request of the turn that stands in for the turns that were
    /// dropped, so the conversation says what happened where the conversation used to be.
    /// </summary>
    public const string DroppedTurnsLabel =
        "Earlier turns, dropped without a summary when the conversation was compacted";

    private const string TruncationMarker = "\n… (truncated)";

    /// <summary>Packs <paramref name="turns"/> down to a token budget.</summary>
    /// <param name="turns">Conversation to shorten, oldest first.</param>
    /// <param name="budgetTokens">
    /// Tokens the result should aim for. Raised to <see cref="MinimumBudgetTokens"/>, because
    /// a budget too small to say anything is worse than no budget at all. The two ends are
    /// kept whatever this says: a pack without them is not a shorter conversation, it is a
    /// lost one.
    /// </param>
    /// <param name="archivePath">
    /// Where the full conversation was archived, or null when it was not written. Named in
    /// the notice so the model knows where to look for what is no longer in the
    /// conversation.
    /// </param>
    /// <returns>
    /// The turns to keep, oldest first, with one notice turn standing in for the ones that
    /// were dropped — or <paramref name="turns"/> itself, unchanged, when there was nothing
    /// to save.
    /// </returns>
    public static IReadOnlyList<ChatTurn> Pack(
        IReadOnlyList<ChatTurn> turns,
        int budgetTokens,
        string? archivePath = null)
    {
        ArgumentNullException.ThrowIfNull(turns);

        if (turns.Count <= 1)
        {
            return turns;
        }

        var budget = Math.Max(MinimumBudgetTokens, budgetTokens);
        var first = Truncate(turns[0]);

        // The notice is costed before the turns are chosen because it comes out of the same
        // budget: a pack that cannot afford to say what it dropped is a trap, not a
        // compaction. Costed against the largest count it could report — every turn after
        // the first — so the count it does report is never larger than what was reserved.
        var reserved = Cost(Notice(turns[0].Timestamp, turns.Count - 1, archivePath)) + Cost(first);

        // What is left for the newest turns. Never negative: the ends are kept even when
        // they do not fit, because a pack that drops them has nothing left worth keeping.
        var room = Math.Max(0, budget - reserved);
        var kept = new List<ChatTurn>();
        var used = 0;

        // Newest first: they are the turns the next request is built on.
        for (var index = turns.Count - 1; index >= 1; index--)
        {
            var candidate = Truncate(turns[index]);
            var cost = Cost(candidate);

            if (used + cost > room)
            {
                break;
            }

            kept.Insert(0, candidate);
            used += cost;
        }

        if (kept.Count == 0 && turns.Count > 2)
        {
            // Not one turn fitted on its own, so the newest is kept in its shortened form
            // regardless. Being a little over budget is a cheaper mistake than leaving the
            // model with no idea what it was in the middle of doing.
            kept.Add(Truncate(turns[^1]));
        }

        var dropped = turns.Count - 1 - kept.Count;
        var packed = new List<ChatTurn>(kept.Count + 2) { first };

        if (dropped > 0)
        {
            // The notice is only written when something is missing. A pack that dropped
            // nothing has nothing to stand in for, and the turns that were shortened say so
            // where they were shortened.
            packed.Insert(0, Notice(turns[0].Timestamp, dropped, archivePath));
        }

        packed.AddRange(kept);

        // The contract: strictly smaller, or the conversation goes back untouched. Checked
        // against the real notice rather than the reserved one, so the count it reports is
        // the count that happened.
        return Cost(packed) < Cost(turns) ? packed : turns;
    }

    /// <summary>The turn that stands in for the turns that were dropped.</summary>
    private static ChatTurn Notice(DateTimeOffset timestamp, int dropped, string? archivePath) =>
        new(
            timestamp,
            DroppedTurnsLabel,
            $"{dropped} earlier turn(s) were dropped when this conversation was compacted "
            + "without a usable summary. "
            + (archivePath is null
                ? "Their content is no longer in the conversation."
                : $"The full conversation is archived at `{archivePath}`; read it before assuming anything is settled."));

    /// <summary>Cuts a turn down to <see cref="MaxTurnCharacters"/>, marker and all.</summary>
    private static ChatTurn Truncate(ChatTurn turn) =>
        turn with
        {
            UserRequest = Cut(turn.UserRequest),
            AiResponse = Cut(turn.AiResponse),
        };

    /// <summary>
    /// Cuts text to the cap with the marker counted inside it. Counting the marker outside
    /// the cap would make a text that is barely over the cap come back longer than it went
    /// in, and the whole point of the cut is that it only ever shortens.
    /// </summary>
    private static string Cut(string text) =>
        text.Length <= MaxTurnCharacters
            ? text
            : string.Concat(
                text.AsSpan(0, MaxTurnCharacters - TruncationMarker.Length),
                TruncationMarker);

    /// <summary>Estimated tokens of a turn, framed the way the request that sends it will be.</summary>
    private static int Cost(ChatTurn turn) =>
        TokenEstimator.EstimateRequest(turn.UserRequest, turn.AiResponse);

    /// <summary>Estimated tokens of a whole conversation.</summary>
    private static int Cost(IEnumerable<ChatTurn> turns) =>
        turns.Sum(Cost);
}
