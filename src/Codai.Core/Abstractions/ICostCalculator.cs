
using Codai.Core.Tokens;
namespace Codai.Core.Abstractions;

/// <summary>
/// Converts token usage into a monetary cost. Implemented by the pricing catalog in
/// <c>Codai.Pricing</c>; optional in the application runtime.
/// </summary>
public interface ICostCalculator
{
    decimal Calculate(string provider, string model, TokenUsage usage);
}
