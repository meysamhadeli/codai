using System.Globalization;

namespace Codai.Application.Inspection;

/// <summary>
/// Renders a <see cref="ConfigurationReport"/> as plain lines, so any host can show an
/// inspection without depending on the CLI's renderer.
/// </summary>
public static class ConfigurationReportRenderer
{
    /// <summary>Renders the sections the report actually carries.</summary>
    public static IReadOnlyList<string> Render(ConfigurationReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var lines = new List<string>();

        if (report.Paths is { } paths)
        {
            lines.Add("Project");
            lines.Add($"  root: {paths.ProjectRoot}");
            lines.Add($"  configuration directory: {paths.ConfigurationDirectory}");
            lines.Add($"  global customization directory: {paths.GlobalConfigurationDirectory ?? "(not configured)"}");
            lines.Add($"  config file: {paths.ConfigurationFile ?? "none"}");
            lines.Add($"  data directory: {paths.DataDirectory ?? "(project .codai directory)"}");
        }

        if (report.Model is { } model)
        {
            lines.Add("Model");
            lines.Add($"  provider: {model.Provider}");
            lines.Add($"  model: {model.Model}");
            lines.Add($"  endpoint origin: {model.BaseUrl ?? "(not available)"}");
            lines.Add($"  reasoning effort: {model.ReasoningEffort ?? "(provider default)"}");
            lines.Add($"  temperature: {(model.Temperature is { } temperature ? temperature.ToString(CultureInfo.InvariantCulture) : "(provider default)")}");
            lines.Add($"  max tokens: {(model.MaxTokens > 0 ? model.MaxTokens.ToString(CultureInfo.InvariantCulture) : "(provider default)")}");
            lines.Add($"  stream: {Format(model.Stream)}");
            lines.Add($"  theme: {model.Theme}");
            lines.Add($"  api key: {(model.ApiKeyConfigured ? "configured" : "not configured")}");
        }

        if (report.Session is { } session)
        {
            lines.Add("Session");
            lines.Add($"  mode: {session.Mode} (current; change it with :mode)");
            lines.Add($"  auto compact: {Format(session.AutoCompact)}");
            lines.Add($"  compaction: {session.CompactionStrategy}");
            lines.Add($"  consecutive tool failures allowed: {session.MaxConsecutiveToolFailures}");
            lines.Add($"  shell commands: {(session.AllowShell ? "enabled (mode and approval policy still apply)" : "not offered")}");
            lines.Add($"  sub-agents: {(session.AllowSubAgents ? "offered" : "not offered")}");
            lines.Add(
                "  budget: " +
                $"max tokens {(session.MaxTokens > 0 ? session.MaxTokens.ToString(CultureInfo.InvariantCulture) : "unlimited")}, " +
                $"max cost {(session.MaxCost > 0 ? session.MaxCost.ToString(CultureInfo.InvariantCulture) : "unlimited")}, " +
                $"max tool calls {(session.MaxToolCalls > 0 ? session.MaxToolCalls.ToString(CultureInfo.InvariantCulture) : "unlimited")}");
        }

        if (report.Tools is { } tools)
        {
            lines.Add("Tools");
            if (!tools.ExternalToolsInspected)
            {
                lines.Add("  built-in tools only; plugin and MCP discovery skipped");
            }
            lines.Add(tools.Available.Count == 0
                ? "  available: none"
                : $"  available ({tools.Available.Count}): {string.Join(", ", tools.Available)}");
            lines.Add(tools.Unavailable.Count == 0
                ? "  withheld: none"
                : $"  withheld ({tools.Unavailable.Count}): {string.Join(", ", tools.Unavailable)}");
        }

        if (report.Rules is { } rules)
        {
            lines.Add("Rules");
            if (rules.Count == 0)
            {
                lines.Add("  none");
            }
            else
            {
                foreach (var rule in rules)
                {
                    var scope = rule.ApplyTo.Count == 0
                        ? "always"
                        : $"applyTo[{string.Join(' ', rule.ApplyTo)}]";
                    var origin = rule.IsGlobal ? "global" : "project";
                    lines.Add($"  {origin} {scope}: {rule.Name} <- {rule.SourcePath}");
                }
            }
        }

        if (report.Skills is { } skills)
        {
            lines.Add("Skills");
            if (skills.Count == 0)
            {
                lines.Add("  none");
            }
            else
            {
                foreach (var skill in skills)
                {
                    lines.Add($"  {(skill.IsGlobal ? "global" : "project")} {skill.Name}: {skill.Description} <- {skill.SourcePath}");
                }
            }
        }

        if (report.Commands is { } commands)
        {
            lines.Add("Commands");
            if (commands.Count == 0)
            {
                lines.Add("  none");
            }
            else
            {
                foreach (var command in commands)
                {
                    lines.Add($"  {(command.IsGlobal ? "global" : "project")} :{command.Name}: {command.Description} <- {command.SourcePath}");
                }
            }
        }

        if (report.Hooks is { } hooks)
        {
            lines.Add("Hooks");
            if (hooks.Count == 0)
            {
                lines.Add("  discovery disabled");
            }
            else
            {
                foreach (var manifest in hooks)
                {
                    lines.Add($"  {manifest.Directory}");
                    if (manifest.Error is not null)
                    {
                        lines.Add($"    unreadable: {manifest.Error}");
                    }

                    if (!manifest.Exists)
                    {
                        lines.Add("    no hooks.json");
                        continue;
                    }

                    foreach (var hook in manifest.Hooks)
                    {
                        var state = hook.Enabled ? "enabled" : "disabled";
                        lines.Add($"    {hook.Name} [{hook.Event}] {state}, timeout {hook.TimeoutSeconds}s");
                    }

                    if (manifest.Hooks.Count == 0 && manifest.Error is null)
                    {
                        lines.Add("    no hooks declared");
                    }
                }
            }
        }

        if (report.McpServers is { } servers)
        {
            lines.Add("MCP servers");
            if (servers.Count == 0)
            {
                lines.Add("  none");
            }
            else
            {
                foreach (var server in servers)
                {
                    var environment = server.EnvironmentVariables.Count == 0
                        ? "no environment variables"
                        : $"environment variables: {string.Join(", ", server.EnvironmentVariables)}";
                    lines.Add(
                        $"  {server.Name} [{server.Transport}]: command configured {Format(server.CommandConfigured)}, " +
                        $"{server.ArgumentCount} argument(s), {environment}, read-only {Format(server.ReadOnly)}, " +
                        $"enabled {Format(server.Enabled)}, provenance {server.Provenance}");
                }
            }
        }

        if (report.Plugins is { } plugins)
        {
            lines.Add("Plugins");
            if (plugins.Count == 0)
            {
                lines.Add("  none");
            }
            else
            {
                foreach (var plugin in plugins)
                {
                    var identity = plugin.AssemblyName is null ? "identity unknown" : plugin.AssemblyName;
                    var size = plugin.SizeBytes is null ? "size unknown" : $"{plugin.SizeBytes} bytes";
                    lines.Add($"  {plugin.Path} [{(plugin.Exists ? "available" : "missing")}, {identity}, {size}, provenance {plugin.Provenance}]");
                }
            }
        }

        return lines;
    }

    private static string Format(bool value) => value ? "true" : "false";
}
