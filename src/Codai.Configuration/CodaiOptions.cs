using Microsoft.Extensions.Configuration;

namespace Codai.Configuration;

/// <summary>
/// Root application options.
/// </summary>
public sealed class CodaiOptions
{
    /// <summary>Configuration section that holds <see cref="AiProviderConfig"/>.</summary>
    public const string ProviderSectionName = "ai_provider_config";

    /// <summary>Configuration file name (without extension) searched in the working directory.</summary>
    public const string FileName = "codai-config";

    [ConfigurationKeyName("version")]
    public string Version { get; set; } = CodaiDefaults.Version;

    [ConfigurationKeyName("theme")]
    public string Theme { get; set; } = CodaiDefaults.Theme;

    [ConfigurationKeyName("ai_provider_config")]
    public AiProviderOptions AiProviderConfig { get; set; } = new();

    /// <summary>Absolute path of the configuration file that was loaded, if any.</summary>
    public string? LoadedFrom { get; set; }
}
