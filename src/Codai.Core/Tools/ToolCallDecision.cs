namespace Codai.Core.Tools;

/// <summary>
/// The verdict on one tool call, produced by <see cref="ToolCallGovernor"/>.
/// </summary>
/// <param name="Allowed">True when the call may run.</param>
/// <param name="Reason">Why it may not, phrased for the model to act on.</param>
public readonly record struct ToolCallDecision(bool Allowed, string Reason)
{
    /// <summary>The call may run.</summary>
    public static ToolCallDecision Allow { get; } = new(true, string.Empty);

    /// <summary>The call must not run; <paramref name="reason"/> says what to do instead.</summary>
    public static ToolCallDecision Deny(string reason) => new(false, reason);
}
