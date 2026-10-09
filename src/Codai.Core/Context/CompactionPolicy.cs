namespace Codai.Core.Context;

/// <summary>
/// Decides when a conversation has to be compacted, and stops trying when compacting
/// keeps failing.
/// </summary>
/// <remarks>
/// <para>
/// The threshold is the model's context window minus a reserve. The reserve covers what
/// is added after the estimate is taken: the tool definitions, the response itself, and
/// the tool results the model may ask for next. Compacting at the window size instead of
/// below it would trade one failure for another.
/// </para>
/// <para>
/// The failure counter is a circuit breaker. Summarisation is a model call, so it can
/// fail, and a session that retries it on every turn would spend its whole budget
/// compacting and never answer. After <see cref="MaxFailures"/> consecutive failures the
/// conversation is left alone: a long conversation that still works beats a broken one
/// that keeps trying to shorten itself.
/// </para>
/// </remarks>
public sealed class CompactionPolicy
{
    /// <summary>
    /// Headroom left for the response, the tool definitions and the next turn. Chosen to
    /// match the buffer the agent CLIs reserve for the same reason.
    /// </summary>
    public const int DefaultReserveTokens = 13_000;

    /// <summary>Consecutive failures after which automatic compaction gives up.</summary>
    public const int DefaultMaxFailures = 3;

    /// <summary>Headroom kept free at the end of the window.</summary>
    public int ReserveTokens { get; init; } = DefaultReserveTokens;

    /// <summary>Consecutive failures allowed before the breaker trips.</summary>
    public int MaxFailures { get; init; } = DefaultMaxFailures;

    /// <summary>Failures since the last success.</summary>
    public int ConsecutiveFailures { get; private set; }

    /// <summary>True when automatic compaction has been given up on.</summary>
    public bool IsTripped => ConsecutiveFailures >= MaxFailures;

    /// <summary>Measures a request against the window of the model that will receive it.</summary>
    public ContextBudget Measure(int estimatedTokens, int contextWindow) =>
        new(estimatedTokens, Math.Max(contextWindow, ContextWindow.MinimumTokens), ReserveTokens);

    /// <summary>
    /// True when <paramref name="budget"/> is over the threshold and the breaker has not
    /// tripped.
    /// </summary>
    public bool ShouldCompact(ContextBudget budget) => !IsTripped && budget.IsOverThreshold;

    /// <summary>Records that a compaction worked, clearing the breaker.</summary>
    public void RecordSuccess() => ConsecutiveFailures = 0;

    /// <summary>Records that a compaction failed.</summary>
    public void RecordFailure() => ConsecutiveFailures++;
}
