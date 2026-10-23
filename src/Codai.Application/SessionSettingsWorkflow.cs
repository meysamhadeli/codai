using System.Globalization;
using Codai.Application.Inspection;
using Codai.Configuration;
using Codai.Core.Abstractions;

namespace Codai.Application;

public sealed class SessionSettingsWorkflow(
    IUserInterface ui,
    ConfigurationInspector inspector,
    ConfigurationInspectionContext context,
    IFileSystem fileSystem)
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

    public async Task RunAsync(string section, CancellationToken cancellationToken = default)
    {
        try
        {
            switch (section.Trim().ToLowerInvariant())
            {
                case "theme":
                    await PickThemeAsync(cancellationToken).ConfigureAwait(false);
                    break;
                case "mcp":
                    await ManageIntegrationsAsync(ConfigurationMutationTarget.Mcp, cancellationToken).ConfigureAwait(false);
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
                default:
                    ui.WriteError("Use :preferences [config|provider|theme|mcp|plugins|skills].");
                    break;
            }
        }
        catch (Exception exception) when (exception is CodaiConfigurationException or IOException or UnauthorizedAccessException)
        {
            ui.WriteError(exception is CodaiConfigurationException ? exception.Message : "Could not update local configuration. Check file access.");
        }
    }

    private async Task EditAsync(bool providerOnly, CancellationToken cancellationToken)
    {
        var fields = providerOnly ? Fields.Where(field => field.Value.StartsWith("ai_provider_config.", StringComparison.Ordinal)).ToArray() : Fields;
        while (true)
        {
            var key = await ui.ChooseAsync("Codai / Edit configuration", fields, cancellationToken).ConfigureAwait(false);
            var field = fields.FirstOrDefault(field => field.Value == key);
            if (field is null)
            {
                return;
            }
            var secret = key == "ai_provider_config.api_key";
            var initial = _saved.GetValueOrDefault(key!) ?? key switch
            {
                "ai_provider_config.provider" => context.Provider,
                "ai_provider_config.model" => context.Model,
                "ai_provider_config.temperature" => context.Temperature?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                "ai_provider_config.max_tokens" => context.MaxTokens.ToString(CultureInfo.InvariantCulture),
                "ai_provider_config.stream" => context.Stream.ToString().ToLowerInvariant(),
                _ => string.Empty,
            };
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

    private async Task PickThemeAsync(CancellationToken cancellationToken)
    {
        var name = await ui.ChooseAsync("Codai / Appearance",
        [
            new("Graphite", "Cool blue selection", "graphite"),
            new("Forest", "Soft green selection", "forest"),
            new("Ember", "Warm gold selection", "ember"),
        ], cancellationToken).ConfigureAwait(false);
        if (name is not ("graphite" or "forest" or "ember"))
        {
            return;
        }
        ui.SetTheme(name);
        if (await ConfirmAsync("Save appearance for future sessions?", cancellationToken).ConfigureAwait(false))
        {
            await _manager.SetValueAsync(ConfigurationFile, "theme", name, context.ConfigurationDirectory, cancellationToken).ConfigureAwait(false);
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
                entries.Select(entry => new SessionMenuItem(entry.Identifier, entry.Enabled ? "Enabled on next startup" : "Disabled on next startup", entry.Identifier)).ToArray(), cancellationToken).ConfigureAwait(false);
            var selected = entries.FirstOrDefault(entry => entry.Identifier == identifier);
            if (selected is null)
            {
                return;
            }
            var operation = await ui.ChooseAsync("Codai / Integration policy",
            [
                new(selected.Enabled ? "Disable" : "Enable", "Change availability on next startup", selected.Enabled ? "disable" : "enable"),
                new("Remove configuration entry", "Keep plugin files and external services untouched", "remove"),
                new("Keep unchanged", "Return to integrations", "cancel"),
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
        var skills = await inspector.InspectAsync(ConfigurationTarget.Skills, cancellationToken).ConfigureAwait(false);
        var commands = await inspector.InspectAsync(ConfigurationTarget.Commands, cancellationToken).ConfigureAwait(false);
        var items = (skills.Skills ?? []).Select(skill => new SessionMenuItem(skill.Name, skill.Description, ":" + skill.Name))
            .Concat((commands.Commands ?? []).Select(command => new SessionMenuItem(command.Name, command.Description, ":" + command.Name)))
            .DistinctBy(item => item.Value).ToArray();
        if (items.Length == 0)
        {
            ui.WriteInfo("No skills or workspace commands were discovered.");
            return;
        }
        var selected = await ui.ChooseAsync("Codai / Skills and workflows", items, cancellationToken).ConfigureAwait(false);
        if (items.Any(item => item.Value == selected))
        {
            ui.InsertInput(selected! + " ");
        }
    }

    private async Task<bool> ConfirmAsync(string title, CancellationToken cancellationToken) =>
        await ui.ChooseAsync("Codai / " + title,
        [
            new("Keep unchanged", "Return without saving", "cancel"),
            new("Confirm change", "Apply the selected configuration change", "confirm"),
        ], cancellationToken).ConfigureAwait(false) == "confirm";
}