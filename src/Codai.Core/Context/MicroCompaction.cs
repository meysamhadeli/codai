using Codai.Core.Chat;

namespace Codai.Core.Context;

/// <summary>
/// Shortens a conversation without asking a model, by clearing the bodies of old tool
/// output that the model has almost certainly stopped reading.
/// </summary>
/// <remarks>
/// <para>
/// Summarising is the expensive tier: it is a model call, it can fail, and it costs the same
/// as an answer. It is also not needed most of the time. What makes an agent conversation
/// long is not the conversation — it is the file dumps, build logs and diffs buried in older
/// turns, whose value was in the turn that read them and whose bulk is paid for on every
/// request afterwards.
/// </para>
/// <para>
/// So this tier runs first. It keeps the newest turns exactly as they are, and for anything
/// older than that it keeps the beginning and the end of each response and drops the middle
/// with a marker saying so. Nothing is invented and nothing is summarised, which is why it
/// cannot fail and needs neither a model nor a network. Only when it is not enough does the
/// session pay for a summary.
/// </para>
/// <para>
/// The turns it clears are not lost: a caller archives the conversation before applying a
/// plan, exactly as it does before a summary.
/// </para>
/// </remarks>
public static class MicroCompaction
{
    /// <summary>
    /// Newest turns left untouched. They are the ones the next request works from, so
    /// clearing them would trade a token problem for a correctness problem.
    /// </summary>
    public const int KeptTurns = 2;

    /// <summary>
    /// Response size below which a turn is left alone. Clearing a turn that costs little
    /// buys little and loses whatever nuance it carried.
    /// </summary>
    public const int MaxTurnTokens = 2_000;

    /// <summary>Characters of the start of a response that are kept.</summary>
    public const int HeadCharacters = 1_200;

    /// <summary>
    /// Characters of the end of a response that are kept. The end is where a tool's verdict
    /// lives — the failing test, the last error line, the tail of a log.
    /// </summary>
    public const int TailCharacters = 800;

    /// <summary>What stands in for the text that was dropped.</summary>
    public const string ElisionMarker = "\n\n… [older tool output cleared to save context]\n\n";

    /// <summary>
    /// Works out which turns to clear, and how much that would save.
    /// </summary>
    /// <param name="turns">The conversation, oldest first.</param>
    /// <param name="targetTokens">
    /// Tokens the conversation has to fit into. Trimming stops as soon as this is reached,
    /// so a conversation that needs a little room does not lose everything it could.
    /// </param>
    /// <param name="currentTokens">
    /// Estimated tokens of <paramref name="turns"/>, or null to measure them here.
    /// </param>
    /// <param name="keptTurns">
    /// Newest turns left alone. A caller that keeps a different tail than the summariser
    /// should say so, or trimming would clear turns the summary was going to keep.
    /// </param>
    /// <param name="maxTurnTokens">Response size below which a turn is left alone.</param>
    public static MicroCompactionPlan Plan(
        IReadOnlyList<ChatTurn> turns,
        int targetTokens,
        int? currentTokens = null,
        int keptTurns = KeptTurns,
        int maxTurnTokens = MaxTurnTokens)
    {
        ArgumentNullException.ThrowIfNull(turns);

        var measured = currentTokens ?? Estimate(turns);
        var keep = Math.Max(0, keptTurns);

        if (turns.Count <= keep || measured <= targetTokens)
        {
            return MicroCompactionPlan.Empty;
        }

        var edits = new List<MicroCompactionEdit>();
        var saved = 0;

        for (var index = 0; index < turns.Count - keep; index++)
        {
            var turn = turns[index];

            // A summary is already the compressed form of what came before it, so there is
            // nothing in it to clear, and clearing it would remove what the compaction was
            // written to keep.
            if (turn.IsCompactionSummary)
            {
                continue;
            }

            var cost = TokenEstimator.EstimateRequest(turn.UserRequest, turn.AiResponse);

            if (cost <= maxTurnTokens)
            {
                continue;
            }

            var cleared = Clear(turn.AiResponse);
            var clearedCost = TokenEstimator.EstimateRequest(turn.UserRequest, cleared);
            var gain = cost - clearedCost;

            if (gain <= 0)
            {
                continue;
            }

            edits.Add(new MicroCompactionEdit(index, turn with { AiResponse = cleared }));
            saved += gain;

            if (measured - saved <= targetTokens)
            {
                break;
            }
        }

        return edits.Count == 0
            ? MicroCompactionPlan.Empty
            : new MicroCompactionPlan(edits, saved, measured - saved);
    }

    /// <summary>Estimates the tokens of a whole conversation.</summary>
    /// <param name="turns">The conversation, oldest first.</param>
    public static int Estimate(IReadOnlyList<ChatTurn> turns)
    {
        ArgumentNullException.ThrowIfNull(turns);

        var tokens = 0;

        foreach (var turn in turns)
        {
            tokens += TokenEstimator.EstimateRequest(turn.UserRequest, turn.AiResponse);
        }

        return tokens;
    }

    /// <summary>Keeps the start and the end of <paramref name="text"/> and drops the middle.</summary>
    /// <param name="text">The response to shorten.</param>
    public static string Clear(string? text)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= HeadCharacters + TailCharacters)
        {
            return text ?? string.Empty;
        }

        return string.Concat(
            text.AsSpan(0, HeadCharacters),
            ElisionMarker,
            text.AsSpan(text.Length - TailCharacters));
    }
}

/// <summary>One turn a <see cref="MicroCompactionPlan"/> replaces, and what replaces it.</summary>
/// <param name="Index">Position of the turn in the conversation it was measured against.</param>
/// <param name="Replacement">The turn with the middle of its response cleared.</param>
public readonly record struct MicroCompactionEdit(int Index, ChatTurn Replacement);

/// <summary>What <see cref="MicroCompaction"/> decided to clear, before anything changed.</summary>
/// <param name="Edits">Turns to replace, oldest first.</param>
/// <param name="SavedTokens">Estimated tokens the conversation is shorter by once applied.</param>
/// <param name="ProjectedTokens">Estimated tokens of the conversation afterwards.</param>
public sealed record MicroCompactionPlan(
    IReadOnlyList<MicroCompactionEdit> Edits,
    int SavedTokens,
    int ProjectedTokens)
{
    /// <summary>A plan that changes nothing.</summary>
    public static MicroCompactionPlan Empty { get; } = new([], 0, 0);

    /// <summary>True when there is nothing to apply.</summary>
    public bool IsEmpty => Edits.Count == 0;
}
