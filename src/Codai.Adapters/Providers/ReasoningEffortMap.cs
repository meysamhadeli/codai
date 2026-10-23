using Microsoft.Extensions.AI;

namespace Codai.Providers;

/// <summary>
/// Maps the configuration-level reasoning effort onto the Microsoft.Extensions.AI model.
/// </summary>
internal static class ReasoningEffortMap
{
    /// <summary>
    /// Builds the <see cref="ChatOptions.Reasoning"/> value, or <c>null</c> when no
    /// effort was configured, so the provider applies its own default.
    /// </summary>
    public static ReasoningOptions? ToReasoningOptions(Configuration.ReasoningEffortLevel? level) =>
        level is null
            ? null
            : new ReasoningOptions { Effort = ToEffort(level.Value) };

    private static ReasoningEffort ToEffort(Configuration.ReasoningEffortLevel level) => level switch
    {
        Configuration.ReasoningEffortLevel.Low => ReasoningEffort.Low,
        Configuration.ReasoningEffortLevel.Medium => ReasoningEffort.Medium,
        Configuration.ReasoningEffortLevel.High => ReasoningEffort.High,
        _ => throw new ArgumentOutOfRangeException(
            nameof(level), level, "Unknown reasoning effort level."),
    };
}
