using System.Text.Json;
using System.Text.Json.Serialization;

namespace Codai.Application.Inspection;

/// <summary>
/// The resolved configuration as reported by <c>codai config</c> and <c>:config</c>.
/// </summary>
/// <remarks>
/// <para>
/// A section is <c>null</c> when the requested target did not ask for it, which is what
/// makes the JSON useful for scripting: <c>codai config tools --json</c> returns the tool
/// section and nothing else.
/// </para>
/// <para>
/// Credential fields are omitted. API keys are reported as a boolean
/// (<see cref="ConfigurationModel.ApiKeyConfigured"/>), MCP environment variables by name
/// only, and MCP commands, argument values and hook commands are never included.
/// Catalog names, descriptions and source paths remain visible metadata.
/// </para>
/// </remarks>
public sealed record ConfigurationReport
{
    /// <summary>Project and configuration directories.</summary>
    public ConfigurationPaths? Paths { get; init; }

    /// <summary>Provider and model settings.</summary>
    public ConfigurationModel? Model { get; init; }

    /// <summary>Mode, compaction, approval policy and budget.</summary>
    public ConfigurationSession? Session { get; init; }

    /// <summary>Tools the model may call and tools it may not.</summary>
    public ConfigurationToolSet? Tools { get; init; }

    /// <summary>Project rules; empty when the project ships none.</summary>
    public IReadOnlyList<ConfigurationRuleEntry>? Rules { get; init; }

    /// <summary>Skills available to the session.</summary>
    public IReadOnlyList<ConfigurationSkillEntry>? Skills { get; init; }

    /// <summary>User-defined <c>:commands</c>.</summary>
    public IReadOnlyList<ConfigurationCommandEntry>? Commands { get; init; }

    /// <summary>Lifecycle hook manifests, one entry per searched directory.</summary>
    public IReadOnlyList<ConfigurationHookManifestEntry>? Hooks { get; init; }

    /// <summary>Configured MCP servers.</summary>
    public IReadOnlyList<ConfigurationMcpServerEntry>? McpServers { get; init; }

    /// <summary>Configured plugin assemblies, described without loading them.</summary>
    public IReadOnlyList<ConfigurationPluginEntry>? Plugins { get; init; }

    /// <summary>Serialises the report as indented camel-case JSON.</summary>
    public string ToJson() =>
        JsonSerializer.Serialize(this, ConfigurationReportJsonContext.Default.ConfigurationReport);
}

/// <summary>Where the session reads configuration from and writes state to.</summary>
/// <param name="ProjectRoot">Directory scanned, edited and treated as the workspace.</param>
/// <param name="ConfigurationDirectory">Directory configuration is discovered from.</param>
/// <param name="ConfigurationFile">Configuration file that was loaded, when there was one.</param>
/// <param name="DataDirectory">Isolated state directory, or <c>null</c> for the project default.</param>
public sealed record ConfigurationPaths(
    string ProjectRoot,
    string ConfigurationDirectory,
    string? ConfigurationFile,
    string? DataDirectory)
{
    /// <summary>Global customization root, or null when global discovery is disabled.</summary>
    public string? GlobalConfigurationDirectory { get; init; }
}

/// <summary>Provider and model settings, without any credential value.</summary>
/// <param name="Provider">Active provider identifier.</param>
/// <param name="Model">Resolved model identifier.</param>
/// <param name="BaseUrl">Endpoint origin only; user info, path, query and fragment are omitted.</param>
/// <param name="ReasoningEffort">Configured reasoning effort, when the model supports it.</param>
/// <param name="Temperature">Configured sampling temperature, when set.</param>
/// <param name="MaxTokens">Configured response-token limit; zero means the provider default.</param>
/// <param name="Stream">True when responses are streamed.</param>
/// <param name="Theme">Markdown render theme.</param>
/// <param name="ApiKeyConfigured">True when a key is present; the key itself is never reported.</param>
public sealed record ConfigurationModel(
    string Provider,
    string Model,
    string? BaseUrl,
    string? ReasoningEffort,
    float? Temperature,
    int MaxTokens,
    bool Stream,
    string Theme,
    bool ApiKeyConfigured);

/// <summary>Session policy: what the assistant may do and what it may spend.</summary>
/// <param name="Mode">Mode currently in force; changes with <c>:mode</c>.</param>
/// <param name="AutoCompact">True when an over-long conversation is summarised automatically.</param>
/// <param name="CompactionStrategy">How compaction shortens the conversation.</param>
/// <param name="MaxConsecutiveToolFailures">Executed-tool failures tolerated before the model loop stops.</param>
/// <param name="AllowShell">True when <c>run_command</c> may be offered.</param>
/// <param name="AllowSubAgents">True when <c>task</c> may be offered.</param>
/// <param name="MaxTokens">Token budget for the run; zero means unlimited.</param>
/// <param name="MaxCost">Cost budget for the run; zero means unlimited.</param>
/// <param name="MaxToolCalls">Tool calls allowed per request.</param>
public sealed record ConfigurationSession(
    string Mode,
    bool AutoCompact,
    string CompactionStrategy,
    int MaxConsecutiveToolFailures,
    bool AllowShell,
    bool AllowSubAgents,
    int MaxTokens,
    decimal MaxCost,
    int MaxToolCalls);

/// <summary>The tools advertised to the model, and the ones deliberately kept back.</summary>
/// <param name="Available">Tool names offered for this run, sorted.</param>
/// <param name="Unavailable">
/// Tools withheld by configuration, shell/delegation policy or the current mode.
/// </param>
/// <param name="ExternalToolsInspected">False when plugin and MCP discovery was deliberately skipped.</param>
public sealed record ConfigurationToolSet(
    IReadOnlyList<string> Available,
    IReadOnlyList<string> Unavailable,
    bool ExternalToolsInspected);

/// <summary>One project rule.</summary>
/// <param name="Name">Rule name, taken from its file name.</param>
/// <param name="SourcePath">Project-relative path, or absolute path for a global rule.</param>
/// <param name="ApplyTo">Globs the rule is scoped to; empty means it always applies.</param>
public sealed record ConfigurationRuleEntry(
    string Name,
    string SourcePath,
    IReadOnlyList<string> ApplyTo)
{
    /// <summary>True for a global rule; false for a higher-priority project rule.</summary>
    public bool IsGlobal { get; init; }
}

/// <summary>One skill.</summary>
/// <param name="Name">Skill name.</param>
/// <param name="Description">When the skill applies; the only part sent up front.</param>
/// <param name="SourcePath">Project-relative path of the skill file.</param>
public sealed record ConfigurationSkillEntry(
    string Name,
    string Description,
    string SourcePath)
{
    /// <summary>True when the skill is loaded from global customization.</summary>
    public bool IsGlobal { get; init; }
}

/// <summary>One user-defined <c>:command</c>.</summary>
/// <param name="Name">Command name without its leading colon.</param>
/// <param name="Description">What the command does.</param>
/// <param name="SourcePath">Project-relative path of the command file.</param>
public sealed record ConfigurationCommandEntry(
    string Name,
    string Description,
    string SourcePath)
{
    /// <summary>True when the workflow is loaded from global customization.</summary>
    public bool IsGlobal { get; init; }
}

/// <summary>A hook manifest in one of the searched directories.</summary>
/// <param name="Directory">Directory searched for <c>hooks.json</c>.</param>
/// <param name="Exists">True when the directory contains a manifest.</param>
/// <param name="Hooks">Hooks declared by the manifest; empty when it is absent or unreadable.</param>
/// <param name="Error">Why the manifest could not be read, when it could not be.</param>
public sealed record ConfigurationHookManifestEntry(
    string Directory,
    bool Exists,
    IReadOnlyList<ConfigurationHookEntry> Hooks,
    string? Error);

/// <summary>One declared hook.</summary>
/// <param name="Name">Hook name.</param>
/// <param name="Event">Lifecycle event that activates it.</param>
/// <param name="Enabled">False when the manifest disabled it.</param>
/// <param name="TimeoutSeconds">Time the hook is given before it is treated as failed.</param>
public sealed record ConfigurationHookEntry(
    string Name,
    string Event,
    bool Enabled,
    int TimeoutSeconds);

/// <summary>A configured MCP server, described without its arguments or environment values.</summary>
/// <param name="Name">Stable server name.</param>
/// <param name="Transport">Transport type; only <c>stdio</c> is supported today.</param>
/// <param name="Provenance">How the entry entered configuration.</param>
/// <param name="Enabled">False when configuration disabled it.</param>
/// <param name="CommandConfigured">Whether an executable is configured; its value is omitted.</param>
/// <param name="ArgumentCount">How many arguments it is launched with; the values may carry secrets.</param>
/// <param name="EnvironmentVariables">Names of the environment variables added to the process, never their values.</param>
/// <param name="ReadOnly">True when the server's tools are trusted in read-only modes.</param>
public sealed record ConfigurationMcpServerEntry(
    string Name,
    string Transport,
    string Provenance,
    bool Enabled,
    bool CommandConfigured,
    int ArgumentCount,
    IReadOnlyList<string> EnvironmentVariables,
    bool ReadOnly);

/// <summary>One configured plugin assembly.</summary>
/// <param name="Path">Resolved assembly path.</param>
/// <param name="Provenance">How the path entered configuration.</param>
/// <param name="Exists">Whether the assembly currently exists.</param>
/// <param name="AssemblyName">Assembly identity when metadata can be read.</param>
/// <param name="SizeBytes">Assembly size when the file exists.</param>
public sealed record ConfigurationPluginEntry(
    string Path,
    string Provenance,
    bool Exists,
    string? AssemblyName = null,
    long? SizeBytes = null);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ConfigurationReport))]
internal sealed partial class ConfigurationReportJsonContext : JsonSerializerContext;
