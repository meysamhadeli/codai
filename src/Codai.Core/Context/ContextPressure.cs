namespace Codai.Core.Context;

/// <summary>
/// How close a conversation is to the point where the model will refuse to answer it.
/// </summary>
/// <remarks>
/// <para>
/// "Does it fit" is only useful at the moment it stops being useful: by the time the
/// answer is no, the request has already failed. A ratio answers the useful question
/// instead — how much of the room is gone — and a ratio can be acted on while there is
/// still room left. A conversation at four fifths of its budget is one long file away
/// from not fitting at all.
/// </para>
/// <para>
/// Three amounts come out of the window before the conversation is measured against it,
/// and each covers something the others cannot:
/// </para>
/// <list type="bullet">
/// <item>
/// <description>
/// The <see cref="ReserveTokens"/>: what is added to the request after it is measured,
/// plus what the next turn will need. Compacting at the window size rather than below it
/// would trade one overflow for another.
/// </description>
/// </item>
/// <item>
/// <description>
/// The <see cref="ToolTokens"/>: the tool definitions are sent with every request and
/// never shrink as the conversation grows. A session in agent mode with nine tools pays
/// for their schemas on every turn, so measuring the history against the whole window
/// would overstate the room the history actually has.
/// </description>
/// </item>
/// <item>
/// <description>
/// The <see cref="SafetyMargin"/>: <see cref="TokenEstimator"/> counts characters, and
/// the number that decides the outcome is the provider's own token count. The two
/// disagree, and they disagree in a direction that cannot be known in advance. Holding
/// back a tenth of the budget costs almost nothing and keeps the disagreement from
/// turning into a rejected request.
/// </description>
/// </item>
/// </list>
/// </remarks>
public readonly record struct ContextPressure
{
    /// <summary>Fraction of the usable budget a conversation may consume.</summary>
    public const double DefaultSafetyMargin = 0.9;

    /// <summary>
    /// Ratio at which compacting is worth starting. There is still room to finish, so a
    /// summary can be produced while the request would still have fitted without one.
    /// </summary>
    public const double ProactiveRatio = 0.80;

    /// <summary>
    /// Ratio at which the next request may be rejected outright. Past this point the
    /// request has to wait for the summary, because there is no longer room to send it.
    /// </summary>
    public const double CriticalRatio = 0.95;

    private ContextPressure(
        int usedTokens,
        int windowTokens,
        int reserveTokens,
        int toolTokens,
        double safetyMargin)
    {
        // Locals, not the properties above: a struct constructor may not read its own
        // members until every one of them has been assigned.
        var window = Math.Max(1, windowTokens);
        var reserve = Math.Clamp(reserveTokens, 0, window - 1);
        var available = Math.Max(1, window - reserve);

        UsedTokens = Math.Max(0, usedTokens);
        WindowTokens = window;
        ReserveTokens = reserve;
        ToolTokens = Math.Clamp(toolTokens, 0, available - 1);
        SafetyMargin = double.IsFinite(safetyMargin)
            ? Math.Clamp(safetyMargin, 0.1, 1d)
            : DefaultSafetyMargin;
    }

    /// <summary>Estimated tokens of the conversation that is about to be sent.</summary>
    public int UsedTokens { get; }

    /// <summary>Tokens the model can take in total.</summary>
    public int WindowTokens { get; }

    /// <summary>
    /// Headroom kept free at the end of the window, for the response, for whatever the
    /// model asks for next, and for the estimator being wrong.
    /// </summary>
    public int ReserveTokens { get; }

    /// <summary>Tokens of the tool definitions sent alongside every request.</summary>
    public int ToolTokens { get; }

    /// <summary>Fraction of the usable budget a conversation may consume.</summary>
    public double SafetyMargin { get; }

    /// <summary>Window left once the reserve is held back.</summary>
    public int BaseBudget => Math.Max(1, WindowTokens - ReserveTokens);

    /// <summary>Room left once the tool definitions are paid for.</summary>
    public int AvailableTokens => Math.Max(1, BaseBudget - ToolTokens);

    /// <summary>Tokens the conversation may use before compaction is due.</summary>
    public int MessageBudget => Math.Max(1, (int)Math.Floor(AvailableTokens * SafetyMargin));

    /// <summary>Tokens the conversation may still use before compaction is due.</summary>
    public int RemainingTokens => Math.Max(0, MessageBudget - UsedTokens);

    /// <summary>
    /// Measured tokens as a fraction of <see cref="MessageBudget"/>. One means the
    /// conversation has filled the budget exactly; above one means it no longer fits.
    /// </summary>
    public double Ratio => (double)UsedTokens / MessageBudget;

    /// <summary>What should be done about <see cref="Ratio"/>.</summary>
    public ContextPressureLevel Level => Ratio switch
    {
        >= 1d => ContextPressureLevel.Exceeded,
        >= CriticalRatio => ContextPressureLevel.Critical,
        >= ProactiveRatio => ContextPressureLevel.Proactive,
        _ => ContextPressureLevel.Healthy,
    };

    /// <summary>True when compacting now is better than waiting for the next turn.</summary>
    public bool IsWorthCompacting => Level != ContextPressureLevel.Healthy;

    /// <summary>
    /// True when a request could be rejected before a summary finishes, so compacting is
    /// the only way the next request fits.
    /// </summary>
    public bool IsUrgent => Level is ContextPressureLevel.Critical or ContextPressureLevel.Exceeded;

    /// <summary>Measures a conversation against the window of the model that will receive it.</summary>
    /// <param name="usedTokens">Estimated tokens of the conversation about to be sent.</param>
    /// <param name="windowTokens">Tokens the model can take in total.</param>
    /// <param name="reserveTokens">Headroom kept free at the end of the window.</param>
    /// <param name="toolTokens">
    /// Estimated tokens of the tool definitions sent with the request. Pass zero for a
    /// request that offers the model no tools.
    /// </param>
    /// <param name="safetyMargin">
    /// Fraction of the usable budget a conversation may consume. Values outside
    /// <c>[0.1, 1]</c> are clamped, so a misconfigured margin cannot produce a budget of
    /// zero.
    /// </param>
    public static ContextPressure Measure(
        int usedTokens,
        int windowTokens,
        int reserveTokens = 0,
        int toolTokens = 0,
        double safetyMargin = DefaultSafetyMargin) =>
        new(usedTokens, windowTokens, reserveTokens, toolTokens, safetyMargin);
}

/// <summary>What a <see cref="ContextPressure"/> reading calls for.</summary>
public enum ContextPressureLevel
{
    /// <summary>There is room; send the conversation as it is.</summary>
    Healthy,

    /// <summary>Still fits, but one long tool result away from not fitting.</summary>
    Proactive,

    /// <summary>About to stop fitting; compact or the request may be rejected.</summary>
    Critical,

    /// <summary>Already over the budget; only compaction can make the next request fit.</summary>
    Exceeded,
}
