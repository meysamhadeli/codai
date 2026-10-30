using System.Reflection;

namespace Codai.Configuration;

/// <summary>
/// Defaults matching the original Go <c>DefaultConfig</c>.
/// </summary>
public static class CodaiDefaults
{
    /// <summary>
    /// Version reported by <c>--version</c>.
    /// </summary>
    /// <remarks>
    /// Read from the assembly so the value always equals the <c>Version</c>
    /// property in <c>Directory.Build.props</c> and the published package.
    /// </remarks>
    public static string Version { get; } = ReadAssemblyVersion();

    /// <summary>Terminal colour theme applied to selections and accents.</summary>
    public const string Theme = "graphite";

    /// <summary>Provider used when none is configured.</summary>
    public const string Provider = ProviderCatalog.OpenAi;

    /// <summary>Files larger than this are excluded from the project context.</summary>
    public const long MaxFileSizeBytes = 100 * 1024;

    private static string ReadAssemblyVersion()
    {
        var assembly = typeof(CodaiDefaults).Assembly;

        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            // SourceLink appends "+<commit sha>" to the informational version.
            var metadata = informational.IndexOf('+', StringComparison.Ordinal);
            return metadata < 0 ? informational : informational[..metadata];
        }

        return assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }
}
