using System.Text.Json;
using Codai.Core.Abstractions;
using Codai.Core.Agents;
using Codai.Core.Tools;

namespace Codai.Application.Inspection;

/// <summary>
/// Answers "what is this session actually configured to do?" from the live runtime rather
/// than from the files alone.
/// </summary>
/// <remarks>
/// The inspector reads the same singletons the run uses — the mode holder, the budget, the
/// registry the model is offered, the catalogs the prompt is built from — so the report
/// describes the session in front of the user, including a mode changed with <c>:mode</c>
/// mid-session. Nothing it reads is a credential; see <see cref="ConfigurationReport"/>.
/// </remarks>
public sealed class ConfigurationInspector
{
    /// <summary>Maximum manifest directories inspected in one report.</summary>
    public const int MaxManifests = 8;

    private readonly ConfigurationInspectionContext _context;
    private readonly AgentModeState _mode;
    private readonly AgentBudget _budget;
    private readonly ToolCallGovernor _governor;
    private readonly Func<IToolRegistry> _registry;
    private readonly IProjectRuleCatalog _rules;
    private readonly ISkillCatalog _skills;
    private readonly ICommandCatalog _commands;
    private readonly IFileSystem _fileSystem;

    /// <summary>Creates the inspector.</summary>
    public ConfigurationInspector(
        ConfigurationInspectionContext context,
        AgentModeState mode,
        AgentBudget budget,
        ToolCallGovernor governor,
        Func<IToolRegistry> registry,
        IProjectRuleCatalog rules,
        ISkillCatalog skills,
        ICommandCatalog commands,
        IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(mode);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(governor);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(skills);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(fileSystem);

        _context = context;
        _mode = mode;
        _budget = budget;
        _governor = governor;
        _registry = registry;
        _rules = rules;
        _skills = skills;
        _commands = commands;
        _fileSystem = fileSystem;
    }

    /// <summary>Builds the report for <paramref name="target"/>.</summary>
    public async Task<ConfigurationReport> InspectAsync(
        ConfigurationTarget target = ConfigurationTarget.All,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var report = new ConfigurationReport();

        if (Includes(target, ConfigurationTarget.Paths))
        {
            report = report with
            {
                Paths = new ConfigurationPaths(
                    _context.ProjectRoot,
                    _context.ConfigurationDirectory,
                    _context.ConfigurationFile,
                    _context.DataDirectory)
                {
                    GlobalConfigurationDirectory = _context.GlobalConfigurationDirectory,
                },
            };
        }

        if (Includes(target, ConfigurationTarget.Model))
        {
            report = report with
            {
                Model = new ConfigurationModel(
                    _context.Provider,
                    _context.Model,
                    EndpointOrigin(_context.BaseUrl),
                    _context.ReasoningEffort?.ToString().ToLowerInvariant(),
                    _context.Temperature,
                    _context.MaxTokens,
                    _context.Stream,
                    _context.Theme,
                    _context.ApiKeyConfigured),
            };
        }

        if (Includes(target, ConfigurationTarget.Session))
        {
            report = report with
            {
                Session = new ConfigurationSession(
                    AgentModeParser.ToName(_mode.Mode),
                    _context.AutoCompact,
                    _context.CompactionStrategy,
                    _governor.MaxConsecutiveFailures,
                    _context.AllowShell,
                    _context.AllowSubAgents,
                    _budget.MaxTokens,
                    _budget.MaxCost,
                    _budget.MaxToolCalls),
            };
        }

        if (Includes(target, ConfigurationTarget.Tools))
        {
            report = report with { Tools = BuildTools() };
        }

        if (Includes(target, ConfigurationTarget.Rules))
        {
            var rules = await _rules.LoadAsync(_context.ProjectRoot, cancellationToken).ConfigureAwait(false);
            report = report with
            {
                Rules = [.. rules.Select(rule => new ConfigurationRuleEntry(rule.Name, rule.SourcePath, rule.ApplyTo)
                {
                    IsGlobal = rule.IsGlobal,
                })],
            };
        }

        if (Includes(target, ConfigurationTarget.Skills))
        {
            var skills = await _skills.LoadAsync(_context.ProjectRoot, cancellationToken).ConfigureAwait(false);
            report = report with
            {
                Skills = [.. skills.Select(skill =>
                    new ConfigurationSkillEntry(skill.Name, skill.Description, skill.SourcePath)
                    {
                        IsGlobal = skill.IsGlobal,
                    })],
            };
        }

        if (Includes(target, ConfigurationTarget.Commands))
        {
            var commands = await _commands.LoadAsync(_context.ProjectRoot, cancellationToken).ConfigureAwait(false);
            report = report with
            {
                Commands = [.. commands.Select(command =>
                    new ConfigurationCommandEntry(command.Name, command.Description, command.SourcePath)
                    {
                        IsGlobal = command.IsGlobal,
                    })],
            };
        }

        if (Includes(target, ConfigurationTarget.Hooks))
        {
            report = report with { Hooks = await ReadHooksAsync(cancellationToken).ConfigureAwait(false) };
        }

        if (Includes(target, ConfigurationTarget.Mcp))
        {
            report = report with
            {
                McpServers =
                [
                    .. _context.McpServers.Select(server => new ConfigurationMcpServerEntry(
                        server.Name,
                        server.Transport,
                        "configuration",
                        server.Enabled,
                        !string.IsNullOrWhiteSpace(server.Command),
                        server.Args.Count,
                        [.. server.Environment.Keys.Order(StringComparer.OrdinalIgnoreCase)],
                        server.ReadOnly)),
                ],
            };
        }

        if (Includes(target, ConfigurationTarget.Plugins))
        {
            var configured = _context.PluginPaths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => new ConfigurationPluginEntry(
                    Path.GetFullPath(path, _context.ProjectRoot),
                    "configuration",
                    _fileSystem.FileExists(Path.GetFullPath(path, _context.ProjectRoot)),
                    PluginAssemblyName(Path.GetFullPath(path, _context.ProjectRoot)),
                    PluginSize(_fileSystem, Path.GetFullPath(path, _context.ProjectRoot))))
                .ToList();
            var pluginDirectory = Path.Combine(_context.ProjectRoot, ".codai", "plugins");
            if (Directory.Exists(pluginDirectory))
            {
                configured.AddRange(Directory.EnumerateFiles(pluginDirectory, "*.dll", SearchOption.TopDirectoryOnly)
                    .Select(path => new ConfigurationPluginEntry(
                        path,
                        "project .codai/plugins",
                        true,
                        PluginAssemblyName(path),
                        PluginSize(_fileSystem, path))));
            }

            report = report with { Plugins = [.. configured.DistinctBy(plugin => plugin.Path, StringComparer.OrdinalIgnoreCase)] };
        }

        return report;
    }

    private static string? EndpointOrigin(string? endpoint) =>
        Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            ? new UriBuilder(uri.Scheme, uri.Host, uri.Port).Uri.GetLeftPart(UriPartial.Authority)
            : null;

    private static string? PluginAssemblyName(string path)
    {
        try
        {
            return System.Reflection.AssemblyName.GetAssemblyName(path).FullName;
        }
        catch (BadImageFormatException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static long? PluginSize(IFileSystem fileSystem, string path) =>
        fileSystem.FileExists(path) ? fileSystem.GetFileSize(path) : null;

    /// <summary>
    /// True when <paramref name="section"/> belongs to the requested <paramref name="target"/>.
    /// </summary>
    private static bool Includes(ConfigurationTarget target, ConfigurationTarget section) =>
        target == ConfigurationTarget.All ||
        target == section ||
        (target == ConfigurationTarget.Settings && section is ConfigurationTarget.Paths
            or ConfigurationTarget.Model or ConfigurationTarget.Session);

    /// <summary>
    /// The tools offered and the tools withheld. Policy controls are reported as withheld
    /// tools because that is what they are: a name the model will never see.
    /// </summary>
    private ConfigurationToolSet BuildTools()
    {
        var unavailable = new List<string>(_context.DisabledTools);

        if (!_context.AllowShell)
        {
            unavailable.Add("run_command");
        }

        if (!_context.AllowSubAgents)
        {
            unavailable.Add("task");
        }

        var registry = _registry();
        if (!_context.AllowShell)
        {
            unavailable.AddRange(["dotnet_build", "dotnet_test"]);
        }

        var disabled = unavailable.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var offered = AgentModePolicy.AllowedTools(_mode.Mode, registry.Tools)
            .Where(name => !disabled.Contains(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        unavailable.AddRange(registry.Tools.Where(tool => !offered.Contains(tool.Name)).Select(tool => tool.Name));

        return new ConfigurationToolSet(
            [.. offered.Order(StringComparer.Ordinal)],
            [.. unavailable
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.Ordinal)],
            _context.ExternalToolsInspected);
    }

    /// <summary>
    /// Reads each candidate manifest. A manifest that cannot be read is reported with the
    /// reason instead of failing the inspection, because "my hook never runs" is exactly the
    /// question an inspection is asked to answer.
    /// </summary>
    private async Task<IReadOnlyList<ConfigurationHookManifestEntry>> ReadHooksAsync(
        CancellationToken cancellationToken)
    {
        var entries = new List<ConfigurationHookManifestEntry>();
        var seen = new HashSet<string>(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var count = 0;

        foreach (var directory in _context.HookDirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(directory) || !seen.Add(directory))
            {
                continue;
            }

            if (count++ >= MaxManifests)
            {
                entries.Add(new ConfigurationHookManifestEntry(directory, false, [],
                    $"Inspection stops after {MaxManifests} manifest directories."));
                break;
            }

            try
            {
                var path = Path.Combine(directory, HookRunner.ManifestFileName);
                if (!_fileSystem.FileExists(path))
                {
                    entries.Add(new ConfigurationHookManifestEntry(directory, false, [], null));
                    continue;
                }

                if (_fileSystem.GetFileSize(path) > HookRunner.MaxManifestBytes)
                {
                    entries.Add(new ConfigurationHookManifestEntry(directory, true, [],
                        $"Manifest exceeds {HookRunner.MaxManifestBytes} bytes."));
                    continue;
                }

                var json = await _fileSystem.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
                if (json.Length > HookRunner.MaxManifestBytes)
                {
                    entries.Add(new ConfigurationHookManifestEntry(directory, true, [], "Manifest is too large."));
                    continue;
                }

                var manifest = JsonSerializer.Deserialize(json, HookJsonContext.Default.HookManifest);
                if (manifest?.Hooks is null)
                {
                    entries.Add(new ConfigurationHookManifestEntry(directory, true, [], "Invalid hook manifest."));
                    continue;
                }

                if (manifest.Hooks.Count > HookRunner.MaxHooks)
                {
                    entries.Add(new ConfigurationHookManifestEntry(directory, true, [],
                        $"Manifest exceeds {HookRunner.MaxHooks} hooks."));
                    continue;
                }

                var invalid = manifest.Hooks.Any(hook => hook is null ||
                    hook.Enabled != false && (string.IsNullOrWhiteSpace(hook.Name) ||
                    string.IsNullOrWhiteSpace(hook.Command) ||
                    !Enum.GetNames<AgentHookEvent>().Contains(hook.Event, StringComparer.Ordinal) ||
                    (hook.TimeoutSeconds ?? 10) is < 1 or > 300));
                entries.Add(new ConfigurationHookManifestEntry(
                    directory,
                    true,
                    [
                        .. manifest.Hooks
                            .Where(hook => hook is not null)
                            .Select(hook => new ConfigurationHookEntry(
                                hook!.Name ?? string.Empty,
                                hook.Event ?? string.Empty,
                                hook.Enabled != false,
                                hook.TimeoutSeconds ?? 10)),
                    ],
                    invalid ? "Invalid enabled hook definition." : null));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
            {
                entries.Add(new ConfigurationHookManifestEntry(directory, true, [], "Manifest could not be read or parsed."));
            }
        }

        return entries;
    }
}
