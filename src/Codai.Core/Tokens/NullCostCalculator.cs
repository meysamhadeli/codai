using Codai.Core.Abstractions;

namespace Codai.Core.Tokens;

/// <summary>
/// Cost calculator used when no pricing catalog is registered: every request costs 0.
/// </summary>
/// <remarks>
/// Registered by <c>AddCodaiCore</c> so that <see cref="ICostCalculator"/> is always
/// resolvable. A host that wants real costs registers a priced implementation
/// afterwards, which replaces this one.
/// </remarks>
public sealed class NullCostCalculator : ICostCalculator
{
    /// <summary>The shared instance; this type is stateless.</summary>
    public static NullCostCalculator Instance { get; } = new();

    /// <inheritdoc />
    public decimal Calculate(string provider, string model, TokenUsage usage) => 0m;
}
