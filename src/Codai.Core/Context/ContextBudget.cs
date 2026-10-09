namespace Codai.Core.Context;

/// <summary>
/// How much of the model's context window a request is about to use.
/// </summary>
/// <param name="UsedTokens">Estimated tokens of the request that is about to be sent.</param>
/// <param name="ContextWindow">The window of the model that will receive it.</param>
/// <param name="ReserveTokens">
/// Headroom kept free for the response, the tool definitions and the next turn.
/// </param>
/// <remarks>
/// This is a measurement, not a decision: <see cref="CompactionPolicy"/> decides what to
/// do with it. Keeping the two apart makes the threshold testable without running a
/// session.
/// </remarks>
public readonly record struct ContextBudget(int UsedTokens, int ContextWindow, int ReserveTokens)
{
    /// <summary>Tokens that may be used before the reserve is touched.</summary>
    public int ThresholdTokens => Math.Max(0, ContextWindow - ReserveTokens);

    /// <summary>True when the request would eat into the reserved headroom.</summary>
    public bool IsOverThreshold => UsedTokens > ThresholdTokens;

    /// <summary>Fraction of the usable budget consumed, from 0 upwards.</summary>
    public double Utilisation =>
        ThresholdTokens <= 0 ? 1d : (double)UsedTokens / ThresholdTokens;

    /// <summary>Tokens still free before the threshold is reached.</summary>
    public int RemainingTokens => Math.Max(0, ThresholdTokens - UsedTokens);
}
