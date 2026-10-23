namespace Codai.Core.Context;

/// <summary>
/// Decides when a conversation has to be compacted, and stops trying when compacting
/// keeps failing.
/// </summary>
/// <remarks>
/// <para>
/// The decision rests on a <see cref="ContextPressure"/> reading rather than a yes or no
/// answer, so the conversation can be compacted at four fifths of its budget instead of
/// at the point of failure. Compacting late is what a session cannot afford: the request
/// that overflows is the request that was about to do the work.
/// </para>
/// <para>
/// The failure counter is a circuit breaker. Summarisation is itself a model call, so it
/// can fail, and a session that retried it on every turn would spend its whole budget
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

    /// <summary>Fraction of the usable budget a conversation may consume.</summary>
    public double SafetyMargin { get; init; } = ContextPressure.DefaultSafetyMargin;

    /// <summary>Failures since the last success.</summary>
    public int ConsecutiveFailures { get; private set; }

    /// <summary>True when automatic compaction has been given up on.</summary>
    public bool IsTripped => ConsecutiveFailures >= MaxFailures;

    /// <summary>Measures a request against the window of the model that will receive it.</summary>
    /// <param name="estimatedTokens">Estimated tokens of the request and its history.</param>
    /// <param name="model">
    /// Model that will receive the request. An unknown model is measured against the
    /// default window, which is the only assumption that is safe to make about it.
    /// </param>
    /// <param name="toolTokens">Estimated tokens of the tool definitions sent with it.</param>
    public ContextPressure Measure(int estimatedTokens, string? model, int toolTokens = 0) =>
        Measure(estimatedTokens, ContextWindow.For(model), toolTokens);

    /// <summary>Measures a request against a window that is already known.</summary>
    /// <param name="estimatedTokens">Estimated tokens of the request and its history.</param>
    /// <param name="contextWindow">Tokens the model can take in total.</param>
    /// <param name="toolTokens">Estimated tokens of the tool definitions sent with it.</param>
    public ContextPressure Measure(int estimatedTokens, int contextWindow, int toolTokens = 0) =>
        ContextPressure.Measure(
            estimatedTokens,
            Math.Max(contextWindow, ContextWindow.MinimumTokens),
            ReserveTokens,
            toolTokens,
            SafetyMargin);

    /// <summary>
    /// True when <paramref name="pressure"/> calls for compaction now and the breaker has
    /// not tripped.
    /// </summary>
    public bool ShouldCompact(ContextPressure pressure) =>
        !IsTripped && pressure.IsWorthCompacting;

    /// <summary>Records that a compaction worked, clearing the breaker.</summary>
    public void RecordSuccess() => ConsecutiveFailures = 0;

    /// <summary>Records that a compaction failed.</summary>
    public void RecordFailure() => ConsecutiveFailures++;
}
