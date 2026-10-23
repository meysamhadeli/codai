namespace Codai.Application.Inspection;

/// <summary>
/// Which part of the resolved configuration an inspection reports.
/// </summary>
/// <remarks>
/// A target exists so the answer can be narrowed: a session with sixty skills should be
/// able to ask what <em>rules</em> apply without scrolling through tools and MCP servers.
/// </remarks>
public enum ConfigurationTarget
{
    /// <summary>Everything.</summary>
    All,

    /// <summary>The paths, provider settings and session policy together.</summary>
    Settings,

    /// <summary>Project, configuration and state directories.</summary>
    Paths,

    /// <summary>Provider, model and sampling settings.</summary>
    Model,

    /// <summary>Mode, compaction, approval policy and budget.</summary>
    Session,

    /// <summary>The tools the model may call, and the ones it may not.</summary>
    Tools,

    /// <summary>Project rules and the globs they are scoped to.</summary>
    Rules,

    /// <summary>Skills available to the session.</summary>
    Skills,

    /// <summary>User-defined <c>:commands</c>.</summary>
    Commands,

    /// <summary>Lifecycle hook manifests.</summary>
    Hooks,

    /// <summary>Configured MCP servers.</summary>
    Mcp,

    /// <summary>Configured plugin assemblies.</summary>
    Plugins,
}

/// <summary>
/// Parses the target name accepted by <c>codai config &lt;target&gt;</c> and <c>:config</c>.
/// </summary>
public static class ConfigurationTargets
{
    /// <summary>The accepted target names, in the order they are offered to a user.</summary>
    public const string Names = "all | settings | paths | model | session | tools | rules | skills | commands | hooks | mcp | plugins";

    /// <summary>
    /// Maps a target name onto a <see cref="ConfigurationTarget"/>. An empty name means
    /// <see cref="ConfigurationTarget.All"/>, so a bare <c>config</c> reports everything.
    /// </summary>
    public static bool TryParse(string? value, out ConfigurationTarget target)
    {
        target = ConfigurationTarget.All;

        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        switch (value.Trim().ToLowerInvariant())
        {
            case "all":
                target = ConfigurationTarget.All;
                return true;
            case "settings":
                target = ConfigurationTarget.Settings;
                return true;
            case "paths" or "directories":
                target = ConfigurationTarget.Paths;
                return true;
            case "model" or "provider":
                target = ConfigurationTarget.Model;
                return true;
            case "session" or "policy":
                target = ConfigurationTarget.Session;
                return true;
            case "tools":
                target = ConfigurationTarget.Tools;
                return true;
            case "rules":
                target = ConfigurationTarget.Rules;
                return true;
            case "skills":
                target = ConfigurationTarget.Skills;
                return true;
            case "commands" or "workflows":
                target = ConfigurationTarget.Commands;
                return true;
            case "hooks":
                target = ConfigurationTarget.Hooks;
                return true;
            case "mcp":
                target = ConfigurationTarget.Mcp;
                return true;
            case "plugins" or "plugin":
                target = ConfigurationTarget.Plugins;
                return true;
            default:
                return false;
        }
    }
}
