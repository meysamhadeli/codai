using Codai.Configuration;

namespace Codai.Application.Inspection;

/// <summary>
/// The configuration values an inspection needs that are not already singletons in the
/// container: what the host resolved before the session was built.
/// </summary>
/// <remarks>
/// This is deliberately a plain record rather than a second copy of <c>CodaiOptions</c>:
/// the composition root fills it from the already-resolved values, so an inspection can
/// never disagree with what the run is actually using.
/// </remarks>
public sealed record ConfigurationInspectionContext
{
    /// <summary>Directory that is scanned, edited and treated as the workspace.</summary>
    public required string ProjectRoot { get; init; }

    /// <summary>Directory configuration was discovered from.</summary>
    public required string ConfigurationDirectory { get; init; }

    /// <summary>Explicit global customization root, when the host opted into global rules.</summary>
    public string? GlobalConfigurationDirectory { get; init; }

    /// <summary>Configuration file that was loaded, when there was one.</summary>
    public string? ConfigurationFile { get; init; }

    /// <summary>Isolated state directory, or <c>null</c> when the project default applies.</summary>
    public string? DataDirectory { get; init; }

    /// <summary>Active provider identifier.</summary>
    public required string Provider { get; init; }

    /// <summary>Resolved model identifier.</summary>
    public required string Model { get; init; }

    /// <summary>Terminal colour theme.</summary>
    public string Theme { get; init; } = "graphite";

    /// <summary>True when a conversation that outgrows the context window is summarised.</summary>
    public bool AutoCompact { get; init; } = true;

    /// <summary>How a compaction shortens the conversation.</summary>
    public string CompactionStrategy { get; init; } = "agentic";

    /// <summary>Resolved provider endpoint.</summary>
    public string? BaseUrl { get; init; }

    /// <summary>Configured reasoning effort.</summary>
    public ReasoningEffortLevel? ReasoningEffort { get; init; }

    /// <summary>Configured sampling temperature.</summary>
    public float? Temperature { get; init; }

    /// <summary>Configured response-token limit; zero means the provider default.</summary>
    public int MaxTokens { get; init; }

    /// <summary>True when responses are streamed.</summary>
    public bool Stream { get; init; } = true;

    /// <summary>True when a credential is present. The credential itself is never stored here.</summary>
    public bool ApiKeyConfigured { get; init; }

    /// <summary>True when <c>run_command</c> may be offered to the model.</summary>
    public bool AllowShell { get; init; }

    /// <summary>True when <c>task</c> may be offered to the model.</summary>
    public bool AllowSubAgents { get; init; }

    /// <summary>Tool names excluded by configuration.</summary>
    public IReadOnlyList<string> DisabledTools { get; init; } = [];

    /// <summary>Configured MCP servers.</summary>
    public IReadOnlyList<McpServerOptions> McpServers { get; init; } = [];

    /// <summary>Configured plugin assembly paths. Inspection never loads these assemblies.</summary>
    public IReadOnlyList<string> PluginPaths { get; init; } = [];

    /// <summary>Directories searched for a <c>hooks.json</c> manifest, in precedence order.</summary>
    public IReadOnlyList<string> HookDirectories { get; init; } = [];

    /// <summary>False for offline inspection, which never discovers plugin or MCP tools.</summary>
    public bool ExternalToolsInspected { get; init; }
}
