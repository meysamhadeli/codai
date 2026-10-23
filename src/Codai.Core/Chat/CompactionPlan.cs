namespace Codai.Core.Chat;

/// <summary>
/// What a compaction decided to do, before anything has been changed.
/// </summary>
/// <remarks>
/// <para>
/// Producing a compaction and applying one are separated on purpose. Summarising is a model
/// call, and a model call takes long enough that doing it while the user waits is the
/// difference between a session that feels instant and one that stalls. Producing the plan
/// can therefore happen whenever there is spare time — including while an answer the user
/// is reading is still on screen — and applying it happens only where it is safe.
/// </para>
/// <para>
/// Applying is only safe between turns. A conversation that is rewritten underneath a
/// request that has already been sent is a request built from a history that no longer
/// exists, so the plan describes which turns it consumed and the caller applies it at the
/// boundary it chooses.
/// </para>
/// </remarks>
/// <param name="ConsumedTurns">
/// How many turns, counted from the start of the conversation, this plan replaces. Turns
/// added after the plan was made are left where they are: they were not part of what was
/// summarised, and appending them to the replacement keeps the order right.
/// </param>
/// <param name="Replacement">
/// The turns that stand in for the ones that were consumed, oldest first.
/// </param>
/// <param name="Method">How the replacement was produced.</param>
/// <param name="ReplacedTokens">Estimated tokens of the turns that were consumed.</param>
/// <param name="ReplacementTokens">Estimated tokens of <paramref name="Replacement"/>.</param>
/// <param name="ArchivePath">
/// Where the whole conversation was archived, relative to the project root, or null when it
/// could not be written.
/// </param>
public sealed record CompactionPlan(
    int ConsumedTurns,
    IReadOnlyList<ChatTurn> Replacement,
    CompactionMethod Method,
    int ReplacedTokens,
    int ReplacementTokens,
    string? ArchivePath)
{
    /// <summary>Tokens the conversation is shorter by once this is applied.</summary>
    public int SavedTokens => ReplacedTokens - ReplacementTokens;

    /// <summary>Turns that are no longer in the conversation once this is applied.</summary>
    public int DroppedTurns => ConsumedTurns - Replacement.Count;

    /// <summary>
    /// Turns at the end of the conversation that this plan leaves alone. They are the turns
    /// the next request is built on, so a plan that says how many it kept can be reported
    /// without the caller having to work it out from the replacement.
    /// </summary>
    /// <remarks>
    /// They are already among the <see cref="Replacement"/>: a plan describes the whole of
    /// the conversation it saw, which is why <see cref="ConsumedTurns"/> counts them too, and
    /// why this is only here to report what survived rather than to be subtracted by a caller
    /// that appends the replacement.
    /// </remarks>
    public int KeptTurns { get; init; }

    /// <summary>
    /// Original turns whose place <see cref="Replacement"/> takes. A summary stands in for the
    /// turns it folded plus the turn that holds the summary itself.
    /// </summary>
    public int ReplacedTurns => Math.Max(0, ConsumedTurns - KeptTurns);

    /// <summary>True when a model wrote the replacement rather than it being packed from the turns.</summary>
    public bool IsSummary => Method == CompactionMethod.Summary;

    /// <summary>True when the replacement is still larger than the budget it was asked to fit.</summary>
    /// <param name="maxSummaryTokens">The cap the summariser was given.</param>
    public bool IsOverCap(int maxSummaryTokens) =>
        IsSummary && ReplacementTokens > maxSummaryTokens;
}

/// <summary>How a <see cref="CompactionPlan"/> produced its replacement.</summary>
public enum CompactionMethod
{
    /// <summary>A model read the turns and wrote a summary of them.</summary>
    Summary,

    /// <summary>
    /// The model could not produce a usable summary, so the ends of the conversation were
    /// kept, each capped, and the middle was dropped with a notice in its place.
    /// </summary>
    Digest,
}
