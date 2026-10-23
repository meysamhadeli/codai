namespace Codai.Configuration;

/// <summary>
/// How much internal reasoning a reasoning-capable model should spend on a request.
/// </summary>
/// <remarks>
/// Only providers and models that support reasoning honour this. Leaving it unset lets
/// the provider apply its own default.
/// </remarks>
public enum ReasoningEffortLevel
{
    /// <summary>Minimal or no reasoning.</summary>
    Low,

    /// <summary>Balanced reasoning. The usual choice.</summary>
    Medium,

    /// <summary>Maximum reasoning. Slowest and most expensive.</summary>
    High,
}
