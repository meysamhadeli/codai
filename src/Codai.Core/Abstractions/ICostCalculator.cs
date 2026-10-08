using Codai.Core.Models;

namespace Codai.Core.Abstractions;

/// <summary>
/// Converts token usage into a monetary cost. Implemented by the pricing catalog in
/// <c>Codai.Tokens</c>; optional in the session.
/// </summary>
public interface ICostCalculator
{
    decimal Calculate(string provider, string model, TokenUsage usage);
}
