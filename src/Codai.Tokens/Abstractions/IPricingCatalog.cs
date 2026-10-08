using Codai.Core.Abstractions;

namespace Codai.Tokens.Abstractions;

/// <summary>
/// Looks up model pricing and converts token usage into a monetary cost.
/// </summary>
public interface IPricingCatalog : ICostCalculator
{
    /// <summary>Returns pricing for a provider/model pair, or <c>null</c> when unknown.</summary>
    ModelPricing? Find(string provider, string model);
}
