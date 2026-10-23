namespace Codai.Configuration;

/// <summary>
/// Convenience facade over <see cref="ProviderCatalog"/> for resolving the effective
/// base URL and model.
/// </summary>
/// <remarks>
/// Centralising these fixes the original bug where several providers silently ignored
/// their default base URL.
/// </remarks>
public static class ProviderDefaults
{
    /// <summary>All provider identifiers understood by the CLI.</summary>
    public static IReadOnlyCollection<string> SupportedProviders => ProviderCatalog.SupportedNames;

    /// <summary>True when the provider identifier is supported (case-insensitive).</summary>
    public static bool IsSupported(string? provider) => ProviderCatalog.TryGet(provider, out _);

    /// <summary>The default base URL for a provider.</summary>
    public static string GetDefaultBaseUrl(string provider) => ProviderCatalog.Get(provider).DefaultBaseUrl;

    /// <summary>The default model for a provider.</summary>
    public static string GetDefaultModel(string provider) => ProviderCatalog.Get(provider).DefaultModel;

    /// <summary>
    /// Resolves the effective base URL: an explicit value wins, otherwise the provider
    /// default is used.
    /// </summary>
    public static string ResolveBaseUrl(string provider, string? configured) =>
        string.IsNullOrWhiteSpace(configured) ? GetDefaultBaseUrl(provider) : configured.Trim();

    /// <summary>
    /// Resolves the effective model: an explicit value wins, otherwise the provider
    /// default applies (DeepSeek defaults to <c>deepseek-flash</c>).
    /// </summary>
    public static string ResolveModel(string provider, string? configured) =>
        string.IsNullOrWhiteSpace(configured) ? GetDefaultModel(provider) : configured.Trim();
}
