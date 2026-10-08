namespace Codai.Configuration;

/// <summary>
/// Everything Codai needs to know about a supported provider: its identifier, its
/// defaults and how to reach it.
/// </summary>
/// <param name="Name">Stable provider identifier used in configuration and on the command line.</param>
/// <param name="DefaultBaseUrl">Endpoint used when the configuration supplies none.</param>
/// <param name="DefaultModel">Model used when the configuration supplies none.</param>
/// <param name="WireFormat">Protocol the provider speaks.</param>
/// <param name="RequiresApiKey">False for local runtimes that accept any credential.</param>
public sealed record ProviderDescriptor(
    string Name,
    string DefaultBaseUrl,
    string DefaultModel,
    ProviderWireFormat WireFormat,
    bool RequiresApiKey = true);
