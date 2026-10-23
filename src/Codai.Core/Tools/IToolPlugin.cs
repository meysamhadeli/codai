using Microsoft.Extensions.DependencyInjection;

namespace Codai.Core.Tools;

/// <summary>
/// Extension point for a package or assembly that contributes Codai tools.
/// </summary>
/// <remarks>
/// Plugins register tools and their dependencies into the host service collection. The
/// session still applies its normal mode filtering, approval policy, and tool-call limits,
/// so loading a plugin does not bypass agent safety rules.
/// </remarks>
public interface IToolPlugin
{
    /// <summary>Stable display name used in diagnostics.</summary>
    string Name { get; }

    /// <summary>Registers the plugin's services and tools.</summary>
    void Register(IServiceCollection services);
}