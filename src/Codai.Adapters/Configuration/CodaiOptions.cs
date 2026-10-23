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

    [ConfigurationKeyName("plan_provider")]
    public string? PlanProvider { get; set; }

    [ConfigurationKeyName("plan_model")]
    public string? PlanModel { get; set; }

    [ConfigurationKeyName("agent_provider")]
    public string? AgentProvider { get; set; }

    [ConfigurationKeyName("agent_model")]
    public string? AgentModel { get; set; }

    [ConfigurationKeyName("checkpoint_retention")]
    public int CheckpointRetention { get; set; } = 10;

    [ConfigurationKeyName("checkpoints_enabled")]
    public bool CheckpointsEnabled { get; set; } = true;

    [ConfigurationKeyName("mcp")]
    public McpOptions Mcp { get; set; } = new();

    [ConfigurationKeyName("budget")]
    public AgentBudgetOptions Budget { get; set; } = new();

    /// <summary>Plugin assembly paths loaded by the host before tool composition.</summary>
    [ConfigurationKeyName("plugins")]
    public List<string> PluginPaths { get; set; } = [];

    /// <summary>Plugin assembly paths withheld from the next host composition.</summary>
    [ConfigurationKeyName("disabled_plugins")]
    public List<string> DisabledPlugins { get; set; } = [];

    /// <summary>Tool names excluded from advertisement and invocation.</summary>
    [ConfigurationKeyName("disabled_tools")]
    public List<string> DisabledTools { get; set; } = [];

    /// <summary>Absolute path of the configuration file that was loaded, if any.</summary>
    public string? LoadedFrom { get; set; }
}
