using System.Globalization;
using System.Text.Json;
using Codai.Application.Inspection;
using Codai.Configuration;
using Codai.Core;
using Codai.Core.Abstractions;
using Codai.Core.Customization;
using Codai.Mcp.Client;

namespace Codai.Application;

public sealed class SessionSettingsWorkflow(
    IUserInterface ui,
    ConfigurationInspector inspector,
    ConfigurationInspectionContext context,
    IFileSystem fileSystem,
    ISkillCatalog? skillCatalog = null,
    McpManifestCatalog? mcpCatalog = null)
{
    private static readonly SessionMenuItem[] Fields =
    [
        new("Provider identifier", "Saved for next startup", "ai_provider_config.provider"),
        new("Default model", "Saved for next startup", "ai_provider_config.model"),
        new("Provider endpoint", "HTTP or HTTPS URL; empty uses provider default", "ai_provider_config.base_url"),
        new("Provider credential", "Masked input; saved locally as plaintext", "ai_provider_config.api_key"),
        new("Sampling temperature", "Number from 0 to 2", "ai_provider_config.temperature"),
        new("Response token limit", "Nonnegative integer; 0 uses provider default", "ai_provider_config.max_tokens"),
        new("Stream responses", "true or false", "ai_provider_config.stream"),
        new("Workspace checkpoints", "true or false; applies at next startup", "checkpoints_enabled"),
        new("Checkpoint retention", "Positive number of retained checkpoints", "checkpoint_retention"),
        new("Token budget", "Nonnegative integer; 0 is unlimited", "budget.max_tokens"),
        new("Cost budget", "Nonnegative amount; 0 is unlimited", "budget.max_cost"),
        new("Tool-call budget", "Positive number of calls per request", "budget.max_tool_calls"),
    ];
    private readonly CodaiConfigurationManager _manager = new(fileSystem);
    private readonly Dictionary<string, string> _saved = new(StringComparer.Ordinal);
    private string ConfigurationFile => context.ConfigurationFile ?? Path.Combine(context.ConfigurationDirectory, CodaiOptions.FileName + ".json");

    internal Task SaveAutoCompactWindowAsync(int tokens, CancellationToken cancellationToken) =>
        _manager.SetValueAsync(ConfigurationFile, "auto_compact_window", tokens.ToString(CultureInfo.InvariantCulture),
            context.ConfigurationDirectory, cancellationToken);

    public async Task RunAsync(string section, CancellationToken cancellationToken = default)
    {
        try
        {
            switch (section.Trim().ToLowerInvariant())
            {
                case "connect":
                    if (await new ConnectionSetupWorkflow(ui, _manager, ConfigurationFile, context.ConfigurationDirectory,
                        new AiProviderOptions { Provider = context.Provider, Model = context.Model, BaseUrl = context.BaseUrl })
                        .RunAsync(cancellationToken).ConfigureAwait(false))
                    {
                        ui.WriteSuccess("Connection saved. Restart Codai to apply the new provider connection.");
                    }
                    break;
                case "theme":
                    await PickThemeAsync(cancellationToken).ConfigureAwait(false);
                    break;
                case "mcp":
                    await ManageMcpsAsync(cancellationToken).ConfigureAwait(false);
                    break;
                case "plugins":
                    await ManageIntegrationsAsync(ConfigurationMutationTarget.Plugin, cancellationToken).ConfigureAwait(false);
                    break;
                case "skills":
                    await BrowseSkillsAsync(cancellationToken).ConfigureAwait(false);
                    break;
                case "provider":
                    await EditAsync(providerOnly: true, cancellationToken).ConfigureAwait(false);
                    break;
                case "":
                case "config":
                    await EditAsync(providerOnly: false, cancellationToken).ConfigureAwait(false);
                    break;
                case "status":
                case "usage":
                case "stats":
                    await EditAsync(providerOnly: false, cancellationToken, section.Trim().ToLowerInvariant()).ConfigureAwait(false);
                    break;
                default:
                    ui.WriteError("Use /settings [config|status|usage|stats] or :preferences [config|provider|theme|mcp|plugins|skills].");
                    break;
            }
        }
        catch (Exception exception) when (exception is CodaiConfigurationException or IOException or UnauthorizedAccessException)
        {
            ui.WriteError(exception is CodaiConfigurationException ? exception.Message : "Could not update local configuration. Check file access.");
        }
    }

    private async Task EditAsync(bool providerOnly, CancellationToken cancellationToken, string section = "config")
    {
        var fields = providerOnly ? Fields.Where(field => field.Value.StartsWith("ai_provider_config.", StringComparison.Ordinal)).ToArray() : Fields;
        while (true)
        {
            var items = section == "config" ? fields.Select(field => new SessionMenuItem(field.Label, field.Description, field.Value)
            {
                RightLabel = field.Value == "ai_provider_config.api_key" ? "Hidden" :
                    InitialValue(field.Value) is { Length: > 0 } value ? value : "Edit...",
                IsCurrent = field.Value != "ai_provider_config.api_key" && InitialValue(field.Value).Length > 0,
            }).ToArray() : await InspectSettingsAsync(section, cancellationToken).ConfigureAwait(false);
            var title = section == "config" ? "Codai / Edit configuration" : "Codai / Settings / " + CultureInfo.InvariantCulture.TextInfo.ToTitleCase(section);
            var key = await ui.ChooseAsync(title, items, cancellationToken).ConfigureAwait(false);
            if (key?.StartsWith("@settings:", StringComparison.Ordinal) == true)
            {
                if (key[10..] is "status" or "config" or "usage" or "stats")
                {
                    section = key[10..];
                }
                continue;
            }
            var field = fields.FirstOrDefault(field => field.Value == key);
            if (field is null)
            {
                return;
            }
            var secret = key == "ai_provider_config.api_key";
            var initial = InitialValue(key!);
            var value = await ui.ReadValueAsync("Codai / " + field.Label, secret ? string.Empty : initial, secret, cancellationToken).ConfigureAwait(false);
            if (value is null)
            {
                continue;
            }
            if (!await ConfirmAsync(secret ? "Save credential locally as plaintext?" : "Save setting for next startup?", cancellationToken).ConfigureAwait(false))
            {
                continue;
            }
            try
            {
                await _manager.SetValueAsync(ConfigurationFile, key!, value.Trim(), context.ConfigurationDirectory, cancellationToken).ConfigureAwait(false);
                if (!secret)
                {
                    _saved[key!] = value.Trim();
                }
                ui.WriteSuccess("Configuration saved. Restart Codai to apply this setting; command-line and environment overrides still take precedence.");
            }
            catch (CodaiConfigurationException exception)
            {
                ui.WriteError(exception.Message);
            }
        }
    }

    private async Task<SessionMenuItem[]> InspectSettingsAsync(string section, CancellationToken cancellationToken)
    {
        var report = await inspector.InspectAsync(section == "stats" ? ConfigurationTarget.Tools : ConfigurationTarget.Session,
            cancellationToken).ConfigureAwait(false);
        var values = new List<(string Label, string Value)>();
        if (section == "status" && report.Session is { } status)
        {
            values.AddRange([
                ("Provider", context.Provider),
                ("Model", context.Model),
                ("Mode", status.Mode),
                ("Auto-compact", status.AutoCompact.ToString().ToLowerInvariant()),
                ("Compaction strategy", status.CompactionStrategy),
                ("Shell commands", status.AllowShell.ToString().ToLowerInvariant()),
                ("Sub-agents", status.AllowSubAgents.ToString().ToLowerInvariant()),
            ]);
        }
        else if (section == "usage" && report.Session is { } limits)
        {
            values.AddRange([
                ("Token budget", limits.MaxTokens == 0 ? "Unlimited" : limits.MaxTokens.ToString(CultureInfo.InvariantCulture)),
                ("Cost budget", limits.MaxCost == 0 ? "Unlimited" : limits.MaxCost.ToString("0.00", CultureInfo.InvariantCulture)),
                ("Tool calls per request", limits.MaxToolCalls.ToString(CultureInfo.InvariantCulture)),
                ("Consecutive tool failures", limits.MaxConsecutiveToolFailures.ToString(CultureInfo.InvariantCulture)),
            ]);
        }
        else if (section == "stats" && report.Tools is { } tools)
        {
            values.AddRange([
                ("Available tools", tools.Available.Count.ToString(CultureInfo.InvariantCulture)),
                ("Unavailable tools", tools.Unavailable.Count.ToString(CultureInfo.InvariantCulture)),
                ("External tools inspected", tools.ExternalToolsInspected.ToString().ToLowerInvariant()),
            ]);
        }
        return values.Select(value => new SessionMenuItem(value.Label, "Read-only runtime value", "@settings:readonly")
        {
            RightLabel = value.Value,
        }).ToArray();
    }

    private string InitialValue(string key) => _saved.GetValueOrDefault(key) ?? key switch
    {
        "ai_provider_config.provider" => context.Provider,
        "ai_provider_config.model" => context.Model,
        "ai_provider_config.base_url" => context.BaseUrl ?? string.Empty,
        "ai_provider_config.temperature" => context.Temperature?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        "ai_provider_config.max_tokens" => context.MaxTokens.ToString(CultureInfo.InvariantCulture),
        "ai_provider_config.stream" => context.Stream.ToString().ToLowerInvariant(),
        _ => string.Empty,
    };

    private async Task PickThemeAsync(CancellationToken cancellationToken)
    {
        var currentTheme = TerminalThemes.Find(_saved.GetValueOrDefault("theme") ?? context.Theme).Id;
        var name = await ui.ChooseAsync("Codai / Theme",
            TerminalThemes.All
                .Select(theme => new SessionMenuItem(theme.Title, theme.Description, theme.Id) { IsCurrent = theme.Id == currentTheme })
                .ToArray(),
            cancellationToken).ConfigureAwait(false);
        if (!TerminalThemes.IsKnown(name))
        {
            return;
        }
        if (await ConfirmAsync("Save theme for current and future sessions?", cancellationToken).ConfigureAwait(false))
        {
            await _manager.SetValueAsync(ConfigurationFile, "theme", name!, context.ConfigurationDirectory, cancellationToken).ConfigureAwait(false);
            _saved["theme"] = name!;
            ui.SetTheme(name!);
        }
    }

    private async Task ManageMcpsAsync(CancellationToken cancellationToken)
    {
        var entries = fileSystem.FileExists(ConfigurationFile)
            ? await _manager.ListAsync(ConfigurationFile, ConfigurationMutationTarget.Mcp, context.ConfigurationDirectory, cancellationToken).ConfigureAwait(false)
            : [];
        var catalog = mcpCatalog ?? new McpManifestCatalog(fileSystem, context.GlobalConfigurationDirectory);
        var imports = (await catalog.LoadAsync(context.ProjectRoot, context.McpServers.Where(server => server.DiscoverySourcePath is null).ToArray(), cancellationToken)
            .ConfigureAwait(false)).Where(server => server.DiscoverySourcePath is not null && !entries.Any(entry =>
                string.Equals(entry.Identifier, server.Name, StringComparison.OrdinalIgnoreCase))).ToArray();
        var items = entries.Select(entry => new SessionMenuItem(entry.Identifier, "Configured MCP server", entry.Identifier)
        {
            Group = "MCPs",
            RightLabel = "Configuration",
            IsCurrent = entry.Enabled,
        }).Concat(imports.Select(server => new SessionMenuItem(server.Name,
            $"{server.Transport} · {(server.Transport == "stdio" ? server.Command : new Uri(server.Url!).GetLeftPart(UriPartial.Authority))} · {server.WorkingDirectory} · {server.DiscoverySourcePath}", server.Name)
        {
            Group = "MCPs",
            RightLabel = server.DiscoveryScope == "user" ? "Global" : "Repository",
            IsCurrent = server.Enabled,
        })).ToArray();
        var selected = await ui.ChooseAsync("MCPs", items,
            new SessionMenuOptions { Placeholder = "Search MCPs...", Width = 104, MultiSelect = true }, cancellationToken).ConfigureAwait(false);
        if (selected is null)
        {
            return;
        }
        string[]? names;
        try
        {
            names = JsonSerializer.Deserialize<string[]>(selected);
        }
        catch (JsonException)
        {
            ui.WriteError("Could not read MCP selection; no changes were saved.");
            return;
        }
        if (names is null || names.Any(name => !items.Any(item => item.Value == name)))
        {
            ui.WriteError("MCP selection contained an unknown server; no changes were saved.");
            return;
        }
        var enabled = names.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var changed = false;
        foreach (var imported in imports)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var next = enabled.Contains(imported.Name);
            if (next == imported.Enabled)
            {
                continue;
            }
            await catalog.SetEnabledAsync(context.ProjectRoot, imported, next, cancellationToken).ConfigureAwait(false);
            changed = true;
        }
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var next = enabled.Contains(entry.Identifier);
            if (next == entry.Enabled)
            {
                continue;
            }
            await _manager.MutateAsync(ConfigurationFile, new(ConfigurationMutationTarget.Mcp,
                next ? ConfigurationMutationAction.Enable : ConfigurationMutationAction.Disable),
                entry.Identifier, context.ConfigurationDirectory, cancellationToken).ConfigureAwait(false);
            changed = true;
        }
        if (changed)
        {
            ui.WriteInfo("MCP selection saved. Restart Codai to rebuild tool connections.");
        }
    }

    private async Task ManageIntegrationsAsync(ConfigurationMutationTarget target, CancellationToken cancellationToken)
    {
        if (!fileSystem.FileExists(ConfigurationFile))
        {
            ui.WriteInfo("No integration configuration file exists. Add connection definitions to Codai configuration first.");
            return;
        }
        while (true)
        {
            var entries = await _manager.ListAsync(ConfigurationFile, target, context.ConfigurationDirectory, cancellationToken).ConfigureAwait(false);
            if (entries.Count == 0)
            {
                ui.WriteInfo("No configured integrations in this collection.");
                return;
            }
            var identifier = await ui.ChooseAsync("Codai / " + (target == ConfigurationMutationTarget.Mcp ? "MCP servers" : "Plugins"),
                entries.Select(entry => new SessionMenuItem(entry.Identifier, entry.Enabled ? "Enabled on next startup" : "Disabled on next startup", entry.Identifier) { IsCurrent = entry.Enabled }).ToArray(), cancellationToken).ConfigureAwait(false);
            var selected = entries.FirstOrDefault(entry => entry.Identifier == identifier);
            if (selected is null)
            {
                return;
            }
            var operation = await ui.ChooseAsync("Codai / Integration policy",
            [
                new(selected.Enabled ? "Disable" : "Enable", "Change availability on next startup", selected.Enabled ? "disable" : "enable"),
                new("Remove configuration entry", "Keep plugin files and external services untouched", "remove"),
                new("Cancel", "Return to integrations without changes", "cancel"),
            ], cancellationToken).ConfigureAwait(false);
            if (operation is not ("enable" or "disable" or "remove") || !await ConfirmAsync("Apply integration change for next startup?", cancellationToken).ConfigureAwait(false))
            {
                continue;
            }
            var action = operation switch { "enable" => ConfigurationMutationAction.Enable, "disable" => ConfigurationMutationAction.Disable, _ => ConfigurationMutationAction.Remove };
            await _manager.MutateAsync(ConfigurationFile, new(target, action), selected.Identifier, context.ConfigurationDirectory, cancellationToken).ConfigureAwait(false);
            ui.WriteSuccess("Integration configuration saved. Restart Codai to rebuild tool connections.");
        }
    }

    private async Task BrowseSkillsAsync(CancellationToken cancellationToken)
    {
        var catalog = skillCatalog ?? new SkillCatalog(fileSystem, context.GlobalConfigurationDirectory);
        var skills = await catalog.LoadAllAsync(context.ProjectRoot, cancellationToken).ConfigureAwait(false);
        var items = skills.Select(skill => new SessionMenuItem(skill.Name, skill.Description, skill.Name)
        {
            Group = "Skills",
            RightLabel = skill.IsGlobal ? "Global" : "Repository",
            IsCurrent = skill.IsEnabled,
        }).ToArray();
        var selected = await ui.ChooseAsync("Skills", items,
            new SessionMenuOptions { Placeholder = "Search skills...", Width = 104, MultiSelect = true }, cancellationToken).ConfigureAwait(false);
        if (selected is null)
        {
            return;
        }
        try
        {
            var enabled = JsonSerializer.Deserialize<string[]>(selected);
            if (enabled is null || enabled.Any(name => !items.Any(item => item.Value == name)))
            {
                ui.WriteError("Skill selection contained an unknown skill; no changes were saved.");
                return;
            }
            await catalog.SaveEnabledAsync(context.ProjectRoot, enabled, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            ui.WriteError("Could not save skill availability in this host.");
        }
    }

    private async Task<bool> ConfirmAsync(string title, CancellationToken cancellationToken) =>
        await ui.ChooseAsync("Codai / " + title,
        [
            new("Save", "Save the selected configuration change", "confirm"),
            new("Cancel", "Return without saving", "cancel"),
        ], cancellationToken).ConfigureAwait(false) == "confirm";
}
