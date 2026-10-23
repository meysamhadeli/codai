using System.Diagnostics;
using Codai.Application;
using Codai.Application.Acp;
using Codai.Application.Inspection;
using Codai.Cli.Ui;
using Codai.Configuration;
using Codai.Core.Abstractions;
using Codai.Core.Agents;
using Codai.Core.IO;
using Codai.Providers;
using Codai.Providers.Accounts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Codai.Cli;

/// <summary>
/// Entry point wiring: parses the command line, loads configuration, composes the
/// service provider and runs the interactive coding session.
/// </summary>
public sealed class CliApplication
{
    /// <summary>Subcommand that starts the interactive assistant.</summary>
    public const string CodeCommand = "code";

    /// <summary>Subcommand that reports the resolved configuration.</summary>
    public const string ConfigCommand = "config";

    private readonly IConsoleOutput _output;
    private readonly Func<string> _currentDirectory;
    private readonly IUserInterface? _uiOverride;
    private readonly IConsoleInput _input;
    private readonly Func<IServiceProvider, ICodaiSession>? _sessionFactory;
    private readonly IEnvironmentReader _environment;
    private readonly Func<ProviderDescriptor, string, string?, CancellationToken, Task<IReadOnlyList<string>>>? _modelDiscovery;

    /// <summary>
    /// Creates the application.
    /// </summary>
    /// <param name="output">Console used for diagnostics (defaults to the real console).</param>
    /// <param name="currentDirectory">Working-directory provider (defaults to <see cref="Directory.GetCurrentDirectory"/>).</param>
    /// <param name="uiOverride">User interface override, used by tests.</param>
    /// <param name="input">Console input source; defaults to standard input.</param>
    /// <param name="sessionFactory">Optional session factory for hosting and tests.</param>
    /// <param name="environment">Environment source for provider settings and isolated paths.</param>
    /// <param name="modelDiscovery">Optional model-list source for connection setup.</param>
    public CliApplication(
        IConsoleOutput? output = null,
        Func<string>? currentDirectory = null,
        IUserInterface? uiOverride = null,
        IConsoleInput? input = null,
        Func<IServiceProvider, ICodaiSession>? sessionFactory = null,
        IEnvironmentReader? environment = null,
        Func<ProviderDescriptor, string, string?, CancellationToken, Task<IReadOnlyList<string>>>? modelDiscovery = null)
    {
        _output = output ?? SystemConsoleOutput.Instance;
        _currentDirectory = currentDirectory ?? Directory.GetCurrentDirectory;
        _uiOverride = uiOverride;
        _input = input ?? SystemConsoleInput.Instance;
        _sessionFactory = sessionFactory;
        _environment = environment ?? ProcessEnvironmentReader.Instance;
        _modelDiscovery = modelDiscovery;
    }

    /// <summary>Runs the CLI and returns the process exit code.</summary>
    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (WantsHelp(args))
        {
            WriteHelp();
            return 0;
        }

        var authentication = args.Length == 0
            ? _uiOverride is not null || !_input.IsRedirected
            : args[0].Equals("auth", StringComparison.OrdinalIgnoreCase);
        var parsed = CommandLineParser.Parse(authentication ? ["code", .. args.Skip(1)] : args);
        if (parsed.Acp && !authentication)
        {
            return await RunAcpAsync(cancellationToken).ConfigureAwait(false);
        }
        IConsoleOutput output = parsed.JsonOutput ? new JsonDiagnosticOutput(_output) : _output;

        if (parsed.Errors.Count > 0)
        {
            foreach (var error in parsed.Errors)
            {
                output.WriteError(error);
            }

            output.WriteError("Run 'codai --help' for usage.");
            return 1;
        }

        if (parsed.ShowVersion)
        {
            _output.WriteLine($"version: {CodaiDefaults.Version}");
            return 0;
        }

        if (HasConfigCommand(args))
        {
            return await RunConfigAsync(args, output, cancellationToken).ConfigureAwait(false);
        }

        if (authentication)
        {
            return await RunAuthenticationAsync(parsed, output, cancellationToken).ConfigureAwait(false);
        }

        var jsonUi = parsed.JsonOutput ? new JsonRunUserInterface(_output) : null;
        var stopwatch = Stopwatch.StartNew();
        jsonUi?.Start();
        var exitCode = await RunConfiguredAsync(args, parsed, output, jsonUi, cancellationToken).ConfigureAwait(false);
        jsonUi?.Complete(exitCode, stopwatch.Elapsed.TotalMilliseconds);
        return exitCode;
    }

    private async Task<int> RunAcpAsync(CancellationToken cancellationToken)
    {
        using var host = new AcpJsonLinesHost(Console.In, Console.Out, new CliAcpSessionFactory(_environment));
        return await host.RunAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<int> RunAuthenticationAsync(CommandLineParseResult parsed, IConsoleOutput output, CancellationToken cancellationToken)
    {
        if (parsed.Acp || parsed.JsonOutput || parsed.Prompt is not null ||
            _uiOverride is null && (_input.IsRedirected || Console.IsOutputRedirected))
        {
            output.WriteError("Connection setup requires an interactive terminal. Use 'codai auth [options]' or configure provider flags for scripted runs.");
            return 1;
        }
        if (!TryResolveRunPaths(parsed, output, out var paths))
        {
            return 1;
        }
        var ui = _uiOverride ?? new SpectreUserInterface(input: _input, projectRoot: paths.WorkingDirectory) { ConnectionSetup = true };
        using var terminal = ui as SpectreUserInterface;
        try
        {
            var file = paths.ConfigPath ?? CodaiConfigurationLoader.FindDefaultConfigFile(paths.ConfigurationDirectory)
                ?? Path.Combine(paths.ConfigurationDirectory, CodaiOptions.FileName + ".json");
            var options = File.Exists(file)
                ? CodaiConfigurationLoader.Load(new(paths.ConfigurationDirectory, file), _environment)
                : new CodaiOptions();
            using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            {
                Timeout = TimeSpan.FromSeconds(8),
                MaxResponseContentBufferSize = 2 * 1024 * 1024,
            };
            var discovery = new ProviderModelDiscovery(http, options.AiProviderConfig.ApiVersion);
            var manager = new CodaiConfigurationManager(new PhysicalFileSystem());
            var accounts = new AccountConnectionWorkflow(ui, manager, file, paths.ConfigurationDirectory,
                new AccountCredentialStore(), new AccountAuthorizationClient(http));
            await new ConnectionSetupWorkflow(ui, manager, file,
                paths.ConfigurationDirectory, options.AiProviderConfig, _modelDiscovery ?? discovery.ListAsync, accounts.RunAsync)
                .RunAsync(cancellationToken).ConfigureAwait(false);
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return 130;
        }
        catch (Exception exception) when (exception is CodaiConfigurationException or IOException or UnauthorizedAccessException)
        {
            output.WriteError("Connection setup could not save configuration. Check the file format and directory permissions.");
            return 1;
        }
    }

    private sealed class CliAcpSessionFactory(IEnvironmentReader environment) : IAcpSessionFactory
    {
        public AcpSessionHandle Create(AcpSessionRequest request, IUserInterface userInterface)
        {
            var args = new List<string> { "code" };
            if (!string.IsNullOrWhiteSpace(request.Provider))
            {
                args.AddRange(["--provider", request.Provider]);
            }

            if (!string.IsNullOrWhiteSpace(request.Model))
            {
                args.AddRange(["--model", request.Model]);
            }

            var options = CodaiConfigurationLoader.Load(
                new CodaiConfigurationRequest(request.ProjectRoot, CommandLineArgs: args), environment);
            var services = new ServiceCollection();
            services.AddSingleton(userInterface);
            services.AddCodaiApplication(
                options,
                request.ProjectRoot,
                request.Resume,
                request.TaskId,
                request.Mode,
                allowShell: true,
                dataDirectory: request.DataDirectory,
                configurationDirectory: request.ProjectRoot);
            var provider = services.BuildServiceProvider();
            return new AcpSessionHandle(
                provider.GetRequiredService<ICodaiSession>(),
                provider.GetRequiredService<SessionOptions>(),
                provider.GetRequiredService<AgentModeState>(),
                provider);
        }
    }

    private async Task<int> RunConfiguredAsync(string[] args, CommandLineParseResult parsed,
        IConsoleOutput output, JsonRunUserInterface? jsonUi, CancellationToken cancellationToken)
    {
        using var runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (parsed.RunTimeout > TimeSpan.Zero)
        {
            runCancellation.CancelAfter(parsed.RunTimeout);
        }

        var runToken = runCancellation.Token;
        var prompt = parsed.Prompt;
        if (_input.IsRedirected)
        {
            string pipedInput;
            try
            {
                pipedInput = await _input.ReadToEndAsync(runToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (runToken.IsCancellationRequested)
            {
                return CancellationExitCode(output, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                output.WriteError($"Could not read task input: {ex.Message}");
                return 1;
            }

            if (!string.IsNullOrWhiteSpace(pipedInput))
            {
                prompt = prompt is null ? pipedInput : $"{prompt}\n\n{pipedInput}";
            }

            if (string.IsNullOrWhiteSpace(prompt) && !parsed.ExecutePlan)
            {
                output.WriteError("Redirected input did not contain a task prompt.");
                return 1;
            }
        }

        if (prompt is not null && string.IsNullOrWhiteSpace(prompt) && !parsed.ExecutePlan)
        {
            output.WriteError("Task prompt must not be empty.");
            return 1;
        }

        if (jsonUi is not null && prompt is null && !parsed.ExecutePlan)
        {
            output.WriteError("JSON output requires a positional or piped task prompt.");
            return 1;
        }

        if (!TryResolveRunPaths(parsed, output, out var paths))
        {
            return 1;
        }

        CodaiOptions options;
        try
        {
            options = CodaiConfigurationLoader.Load(new CodaiConfigurationRequest(
                WorkingDirectory: paths.ConfigurationDirectory,
                ConfigFilePath: paths.ConfigPath,
                CommandLineArgs: args), _environment);
        }
        catch (CodaiConfigurationException ex)
        {
            output.WriteError(ex.Message);
            return 1;
        }

        var services = new ServiceCollection();
        AgentModeState? interactiveModeState = null;
        ApprovalState? interactiveApprovalState = null;
        IUserInterface ui;
        if (jsonUi is not null)
        {
            ui = jsonUi;
        }
        else if (prompt is not null || parsed.ExecutePlan)
        {
            ui = new NonInteractiveUserInterface(output);
        }
        else
        {
            interactiveModeState = new AgentModeState(parsed.Mode);
            interactiveApprovalState = new ApprovalState(parsed.AutoApprove);
            ui = _uiOverride ?? new SpectreUserInterface(
                input: _input,
                provider: options.AiProviderConfig.Provider,
                model: options.AiProviderConfig.Model,
                mode: parsed.Mode,
                autoApprove: parsed.AutoApprove,
                projectRoot: paths.WorkingDirectory,
                modeState: interactiveModeState,
                approvalState: interactiveApprovalState,
                theme: options.Theme)
            {
                UsesAccountPlan = !string.IsNullOrWhiteSpace(options.AiProviderConfig.Account),
            };
        }

        using var terminalLifetime = ui as SpectreUserInterface;
        var effectiveApprovalState = interactiveApprovalState ??
            (parsed.AutoApprove ? new ApprovalState(autoApprove: true) : null);
        services.AddSingleton<IUserInterface>(effectiveApprovalState is null
            ? ui
            : new AutoApprovingUserInterface(ui, effectiveApprovalState));

        // The CLI can ask before every change and every command, so it is one of the
        // hosts that may offer run_command; --no-shell is how a user takes that back
        // without losing the rest of the agent, and the library default stays off.
        services.AddCodaiApplication(
            options,
            paths.WorkingDirectory,
            parsed.ContinueSession,
            resumeTaskId: parsed.ResumeTaskId,
            mode: parsed.Mode,
            allowShell: parsed.AllowShell,
            autoCompact: parsed.AutoCompact,
            allowSubAgents: parsed.AllowSubAgents,
            systemPrompt: parsed.SystemPrompt,
            dataDirectory: paths.DataDirectory,
            hookDirectories: paths.HookDirectories,
            compactionStrategy: parsed.CompactionStrategy,
            configurationDirectory: paths.ConfigurationDirectory,
            globalConfigurationDirectory: paths.GlobalConfigurationDirectory);

        if (interactiveModeState is not null)
        {
            services.Replace(ServiceDescriptor.Singleton(interactiveModeState));
        }

        if (jsonUi is not null)
        {
            services.Replace(ServiceDescriptor.Singleton<IAgentProgressSink>(jsonUi));
        }

        await using var provider = services.BuildServiceProvider();

        try
        {
            runToken.ThrowIfCancellationRequested();
            jsonUi?.SetModel(provider.GetRequiredService<SessionOptions>().Model);
            var session = _sessionFactory?.Invoke(provider) ?? provider.GetRequiredService<ICodaiSession>();
            var exitCode = parsed.ExecutePlan
                ? await session.ExecutePlanAsync(prompt ?? string.Empty, runToken).ConfigureAwait(false)
                : prompt is null
                ? await session.RunAsync(runToken).ConfigureAwait(false)
                : await session.RunTaskAsync(prompt, runToken).ConfigureAwait(false);
            return runToken.IsCancellationRequested ? CancellationExitCode(output, cancellationToken) : exitCode;
        }
        catch (OperationCanceledException) when (runToken.IsCancellationRequested)
        {
            return CancellationExitCode(output, cancellationToken);
        }
        catch (Exception ex) when (ex is ProviderConfigurationException or ChatProviderException or ArgumentException)
        {
            output.WriteError(ex.Message);
            return 1;
        }
    }

    private static int CancellationExitCode(IConsoleOutput output, CancellationToken callerToken)
    {
        if (callerToken.IsCancellationRequested)
        {
            return 130;
        }

        output.WriteError("Run timed out.");
        return 124;
    }

    private string? RunDirectory(string? option, string variable)
    {
        var value = option ?? _environment.GetVariable(variable);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>
    /// Handles <c>codai config [target]</c>: reports what this directory and this
    /// configuration resolve to, without starting a session.
    /// </summary>
    /// <remarks>
    /// It resolves the same services a run does — the report describes the real registry,
    /// the real catalogs and the real hook directories — but nothing resolves a chat client,
    /// so no provider is contacted and no prompt is needed. <c>--json</c> prints the report
    /// itself rather than the JSON Lines event stream, because this is not a task.
    /// </remarks>
    private async Task<int> RunConfigAsync(string[] args, IConsoleOutput output, CancellationToken cancellationToken)
    {
        // The subcommand name is a positional argument to the shared parser, which would
        // otherwise read it as part of a prompt; drop it here instead of teaching every
        // caller about a second reserved word.
        var commandArgs = args.Skip(1).ToArray();
        var parsed = CommandLineParser.Parse(["code", .. commandArgs]);

        if (parsed.Errors.Count > 0)
        {
            foreach (var error in parsed.Errors)
            {
                output.WriteError(error);
            }

            output.WriteError("Run 'codai --help' for usage.");
            return 1;
        }

        var commandParts = SplitConfigCommand(parsed.Prompt);
        if (!ConfigurationTargets.TryParse(commandParts.Target, out var target))
        {
            output.WriteError(
                $"Unknown configuration target '{commandParts.Target}'. Use: {ConfigurationTargets.Names}");
            return 1;
        }

        if (commandParts.Action is not null)
        {
            if (target is not (ConfigurationTarget.Mcp or ConfigurationTarget.Plugins))
            {
                output.WriteError(
                    $"Unknown configuration target '{parsed.Prompt}'. Use: {ConfigurationTargets.Names}");
                return 1;
            }

            if (!TryParseMutation(commandParts.Action, out var mutationAction) ||
                string.IsNullOrWhiteSpace(commandParts.Identifier))
            {
                output.WriteError(
                    "Configuration management syntax: codai config mcp|plugin enable|disable|remove <name-or-path>");
                return 1;
            }

            return await RunConfigurationMutationAsync(
                args,
                commandArgs,
                parsed,
                output,
                new ConfigurationMutation(
                    target == ConfigurationTarget.Mcp
                        ? ConfigurationMutationTarget.Mcp
                        : ConfigurationMutationTarget.Plugin,
                    mutationAction),
                commandParts.Identifier!,
                cancellationToken).ConfigureAwait(false);
        }

        using var runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (parsed.RunTimeout > TimeSpan.Zero)
        {
            runCancellation.CancelAfter(parsed.RunTimeout);
        }

        var runToken = runCancellation.Token;
        if (runToken.IsCancellationRequested)
        {
            return CancellationExitCode(output, cancellationToken);
        }

        if (!TryResolveRunPaths(parsed, output, out var paths))
        {
            return 1;
        }

        CodaiOptions options;
        try
        {
            options = CodaiConfigurationLoader.Load(new CodaiConfigurationRequest(
                WorkingDirectory: paths.ConfigurationDirectory,
                ConfigFilePath: paths.ConfigPath,
                CommandLineArgs: commandArgs), _environment);
        }
        catch (CodaiConfigurationException ex)
        {
            output.WriteError(ex.Message);
            return 1;
        }

        var services = new ServiceCollection();
        services.AddCodaiConfigurationInspection(
            options,
            paths.WorkingDirectory,
            mode: parsed.Mode,
            allowShell: parsed.AllowShell,
            autoCompact: parsed.AutoCompact,
            allowSubAgents: parsed.AllowSubAgents,
            dataDirectory: paths.DataDirectory,
            hookDirectories: paths.HookDirectories,
            compactionStrategy: parsed.CompactionStrategy,
            configurationDirectory: paths.ConfigurationDirectory,
            globalConfigurationDirectory: paths.GlobalConfigurationDirectory);

        await using var provider = services.BuildServiceProvider();

        ConfigurationReport report;
        try
        {
            report = await provider
                .GetRequiredService<ConfigurationInspector>()
                .InspectAsync(target, runToken)
                .ConfigureAwait(false);
            runToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (runToken.IsCancellationRequested)
        {
            return CancellationExitCode(output, cancellationToken);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            output.WriteError(ex.Message);
            return 1;
        }

        if (parsed.JsonOutput)
        {
            output.WriteLine(report.ToJson());
            return 0;
        }

        foreach (var line in ConfigurationReportRenderer.Render(report))
        {
            output.WriteLine(line);
        }

        return 0;
    }

    private async Task<int> RunConfigurationMutationAsync(
        string[] args,
        string[] commandArgs,
        CommandLineParseResult parsed,
        IConsoleOutput output,
        ConfigurationMutation mutation,
        string identifier,
        CancellationToken cancellationToken)
    {
        using var runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (parsed.RunTimeout > TimeSpan.Zero)
        {
            runCancellation.CancelAfter(parsed.RunTimeout);
        }

        if (!TryResolveRunPaths(parsed, output, out var paths))
        {
            return 1;
        }

        var configurationFile = paths.ConfigPath ??
            CodaiConfigurationLoader.FindDefaultConfigFile(paths.ConfigurationDirectory);
        if (configurationFile is null)
        {
            output.WriteError("No existing codai-config.yml, codai-config.yaml or codai-config.json was found to modify.");
            return 1;
        }

        try
        {
            await new CodaiConfigurationManager(PhysicalFileSystem.Instance)
                .MutateAsync(configurationFile, mutation, identifier, paths.ConfigurationDirectory, runCancellation.Token)
                .ConfigureAwait(false);
            output.WriteLine(
                $"Updated {mutation.Target.ToString().ToLowerInvariant()} '{identifier}' in {configurationFile}.");
            return 0;
        }
        catch (OperationCanceledException) when (runCancellation.IsCancellationRequested)
        {
            return CancellationExitCode(output, cancellationToken);
        }
        catch (CodaiConfigurationException ex)
        {
            output.WriteError(ex.Message);
            return 1;
        }
    }

    private static ConfigCommandParts SplitConfigCommand(string? prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return new ConfigCommandParts(null, null, null);
        }

        var parts = prompt.Trim().Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            1 => new ConfigCommandParts(parts[0], null, null),
            2 => new ConfigCommandParts(parts[0], parts[1], null),
            _ => new ConfigCommandParts(parts[0], parts[1], parts[2].Trim()),
        };
    }

    private static bool TryParseMutation(string value, out ConfigurationMutationAction action)
    {
        action = value.ToLowerInvariant() switch
        {
            "enable" => ConfigurationMutationAction.Enable,
            "disable" => ConfigurationMutationAction.Disable,
            "remove" => ConfigurationMutationAction.Remove,
            _ => default,
        };

        return value is "enable" or "disable" or "remove" ||
            value.Equals("enable", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("disable", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("remove", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record ConfigCommandParts(string? Target, string? Action, string? Identifier);

    /// <summary>
    /// The directories a run resolves before it loads anything from them. Resolved here,
    /// once, so <c>codai config</c> and a real run can never disagree about where the
    /// project, the configuration and the hook manifests are.
    /// </summary>
    private sealed record RunPaths(
        string WorkingDirectory,
        string? ConfigPath,
        string ConfigurationDirectory,
        string? DataDirectory,
        IReadOnlyList<string> HookDirectories,
        string GlobalConfigurationDirectory);

    /// <summary>Resolves and validates every directory a run needs.</summary>
    /// <returns>False when a directory is missing or unusable, after reporting why.</returns>
    private bool TryResolveRunPaths(CommandLineParseResult parsed, IConsoleOutput output, out RunPaths paths)
    {
        paths = null!;
        try
        {
            var currentDirectory = _currentDirectory();
            var workingDirectory = Path.GetFullPath(parsed.WorkingDirectory ?? currentDirectory, currentDirectory);
            if (!Directory.Exists(workingDirectory))
            {
                output.WriteError($"Working directory '{workingDirectory}' was not found.");
                return false;
            }

            var configPath = parsed.ConfigPath is null ? null : Path.GetFullPath(parsed.ConfigPath, workingDirectory);
            var configOption = RunDirectory(parsed.ConfigurationDirectory, "CODAI_CONFIG_DIR");
            var dataOption = RunDirectory(parsed.DataDirectory, "CODAI_DATA_DIR");
            var configurationDirectory = configOption is null
                ? workingDirectory
                : Path.GetFullPath(configOption, workingDirectory);
            var dataDirectory = dataOption is null ? null : Path.GetFullPath(dataOption, workingDirectory);
            if (!Directory.Exists(configurationDirectory))
            {
                output.WriteError($"Configuration directory '{configurationDirectory}' was not found.");
                return false;
            }

            var globalConfig = configOption is null
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codai")
                : configurationDirectory;
            var hookDirectories = new List<string>();
            if (!parsed.DisableHooks)
            {
                hookDirectories.Add(Path.Combine(globalConfig, "hooks"));
                hookDirectories.Add(Path.Combine(workingDirectory, ".codai", "hooks"));
                var hookOption = RunDirectory(parsed.HooksDirectory, "CODAI_HOOKS_DIR");
                if (hookOption is not null)
                {
                    var hooksDirectory = Path.GetFullPath(hookOption, workingDirectory);
                    if (!Directory.Exists(hooksDirectory))
                    {
                        output.WriteError($"Hooks directory '{hooksDirectory}' was not found.");
                        return false;
                    }

                    hookDirectories.Add(hooksDirectory);
                }
            }

            paths = new RunPaths(workingDirectory, configPath, configurationDirectory, dataDirectory, hookDirectories, globalConfig);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            output.WriteError(ex.Message);
            return false;
        }
    }

    /// <summary>True when the first argument is the <c>config</c> subcommand.</summary>
    public static bool HasConfigCommand(string[] args) =>
        args.Length > 0 && string.Equals(args[0], ConfigCommand, StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the first argument is the <c>code</c> subcommand.</summary>
    public static bool HasCodeCommand(string[] args) =>
        args.Length > 0 && string.Equals(args[0], CodeCommand, StringComparison.OrdinalIgnoreCase);

    private static bool WantsHelp(string[] args) =>
        (args.Length == 1 && args[0] == "help") ||
        args.TakeWhile(arg => arg != "--").Any(arg => arg is "--help" or "-h");

    private void WriteHelp()
    {
        _output.WriteLine(
            $"""
            codai — AI code assistant that works from the full context of your project.

            Usage:
                            codai [options] [prompt]
                            codai code [options] [prompt]
                            echo "task" | codai [options]
              codai config [target] [--json]
              codai auth [options]
              codai config mcp|plugin enable|disable|remove <name-or-path>
              codai --version

            Targets for 'codai config' ({ConfigurationTargets.Names}) default to all.
                        Inspection skips plugin/MCP discovery; credential values are omitted.

                        Options:
                                    --cwd <path>            Project working directory (host directory unchanged).
                            -c, --config <path>         Configuration file (codai-config.yml|yaml|json).
                  --provider <name>       Chat provider: {string.Join(", ", ProviderCatalog.SupportedNames)}.
                  --base-url <url>        Provider endpoint override.
                  --model <name>          Model identifier (provider default applies when omitted).
                  --temperature <n>       Sampling temperature, 0 to 2.
                  --reasoning-effort <l>  Reasoning effort: low | medium | high.
                  --max-tokens <n>        Maximum number of tokens to generate.
                  --retries <n>           Stop after n consecutive tool failures (default: 3).
                  --stream <bool>         Stream the response (default: true).
                  --api-key <key>         API key (prefer the API_KEY environment variable).
                  --api-version <v>       Version header for providers that need one (Anthropic).
                  --mode <name>           How much the assistant may do: {AgentModeParser.Names} (default: {AgentModeParser.ToName(AgentModeParser.Default)}).
                  --no-shell              Do not offer the model a way to run commands.
                  --disable-tool <name>   Hide and block a named tool for this run; repeatable.
                  --no-auto-compact       Never summarise a conversation that grows too long.
                  --compaction <name>     Automatic compaction: agentic | basic | off (default: agentic).
                  --no-sub-agents         Do not let the model delegate a search to a sub-agent.
                  --auto-approve <bool>   Approve all writes/commands explicitly (default: false).
              -t, --timeout <seconds>    Run-wide timeout, including stdin and tools (0 disables).
              -s, --system <prompt>      Replace default template; mode/tool policies remain enforced.
                  --json                 JSON Lines events and final result for one-shot/piped tasks.
                  --acp                  Start the host-neutral ACP compatibility JSON-lines adapter.
                  --data-dir <path>      Isolated conversation/task/transcript state directory.
                  --config-dir <path>    Configuration discovery directory (project root unchanged).
                  --hooks-dir <path>     Additional hooks.json directory; commands require consent.
                  --no-hooks             Disable global, project, and injected lifecycle hooks.
                  --continue              Resume the conversation stored for this project.
                  --execute-plan          Execute latest completed Plan after consent; prompt/stdin adds instructions.
              -v, --version               Print the version and exit.
              -h, --help                  Print this help and exit.

            Flags also accept the legacy snake_case spelling (for example --base_url).
            Use -- before literal prompt text that starts with a dash.
            One-shot/piped tasks deny writes and commands unless --auto-approve true; no approval input is read.
            Exit codes: 0 request completed, 1 error, 124 timeout, 130 cancelled. Completion does not mean edits were applied.

            Session commands:
              {CodaiApplicationCommands.HelpText.Replace("\n", "\n  ", StringComparison.Ordinal)}
            """);
    }
}
