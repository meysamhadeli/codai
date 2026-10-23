using Codai.Application.Inspection;
using Codai.Core.Abstractions;
using Codai.Core.Agents;
using Codai.Core.Chat;
using Codai.Core.IO;

namespace Codai.Application;

/// <summary>
/// Handles the <c>:</c>-prefixed session commands and reports token usage.
/// </summary>
public sealed class SessionCommandHandler
{
    private readonly SessionOptions _options;
    private readonly IChatHistory _history;
    private readonly ITokenTracker _tokenTracker;
    private readonly ICostCalculator _costCalculator;
    private readonly HistoryCompactor _compactor;
    private readonly AgentModeState _mode;
    private readonly IUserInterface _ui;
    private readonly IBackgroundAgentManager? _backgroundAgents;
    private readonly IAgentTeamCoordinator? _teams;
    private readonly IAgentTaskStore? _taskStore;
    private readonly ISessionStore? _sessionStore;
    private readonly IWorkspaceCheckpointStore? _checkpoints;
    private readonly IAgentProgressSink _progress;
    private readonly ConfigurationInspector? _inspector;
    private readonly ModeModelState? _models;
    private readonly IFileSystem _fileSystem;
    private readonly SessionSettingsWorkflow? _settings;
    private readonly SessionFootprint? _footprint;

    private const int MaxRuleCharacters = 8 * 1024;
    private const int MaxBugSummaryCharacters = 2 * 1024;

    internal AgentModeState ModeState => _mode;

    /// <summary>Creates the handler.</summary>
    public SessionCommandHandler(
        SessionOptions options,
        IChatHistory history,
        ITokenTracker tokenTracker,
        ICostCalculator costCalculator,
        HistoryCompactor compactor,
        AgentModeState mode,
        IUserInterface ui,
        IBackgroundAgentManager? backgroundAgents = null,
        IAgentTeamCoordinator? teams = null,
        IAgentTaskStore? taskStore = null,
        ISessionStore? sessionStore = null,
        ConfigurationInspector? inspector = null,
        IWorkspaceCheckpointStore? checkpoints = null,
        IAgentProgressSink? progress = null,
        ModeModelState? models = null,
        IFileSystem? fileSystem = null,
        SessionSettingsWorkflow? settings = null,
        SessionFootprint? footprint = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(tokenTracker);
        ArgumentNullException.ThrowIfNull(costCalculator);
        ArgumentNullException.ThrowIfNull(compactor);
        ArgumentNullException.ThrowIfNull(mode);
        ArgumentNullException.ThrowIfNull(ui);

        _options = options;
        _history = history;
        _tokenTracker = tokenTracker;
        _costCalculator = costCalculator;
        _compactor = compactor;
        _mode = mode;
        _ui = ui;
        _backgroundAgents = backgroundAgents;
        _teams = teams;
        _taskStore = taskStore;
        _sessionStore = sessionStore;
        _inspector = inspector;
        _checkpoints = checkpoints;
        _progress = progress ?? NullAgentProgressSink.Instance;
        _models = models;
        _fileSystem = fileSystem ?? PhysicalFileSystem.Instance;
        _settings = settings;
        _footprint = footprint;
    }

    /// <summary>
    /// Executes a session command. Returns true when the session should end.
    /// </summary>
    /// <param name="command">The command to run.</param>
    /// <param name="arguments">Everything the user typed after the command name.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    public async Task<bool> HandleAsync(
        SlashCommand command,
        string arguments = "",
        CancellationToken cancellationToken = default)
    {
        switch (command)
        {
            case SlashCommand.Help:
                _ui.WriteLine(SlashCommandParser.HelpText);
                return false;

            case SlashCommand.Clear:
                _ui.Clear();
                return false;

            case SlashCommand.Token:
                DisplayTokens();
                return false;

            case SlashCommand.ClearToken:
                _tokenTracker.Clear();
                return false;

            case SlashCommand.ClearHistory:
                _history.Clear();

                // Starting the conversation over also forgets the files it was working in:
                // restoring them into a session that never read them would put context into
                // a conversation the user emptied on purpose.
                _footprint?.Clear();
                return false;

            case SlashCommand.Mode:
                ShowOrChangeMode(arguments);
                return false;

            case SlashCommand.Model:
                ShowOrChangeModel(arguments);
                return false;

            case SlashCommand.Compact:
                // Anything the user typed after :compact is what they want the summary to
                // concentrate on, which is the difference between an acceptable summary
                // and one that is useful.
                await _compactor
                    .CompactAsync(arguments, cancellationToken)
                    .ConfigureAwait(false);
                return false;

            case SlashCommand.NewTask:
                await CreateTaskHandoffAsync(arguments, cancellationToken).ConfigureAwait(false);
                return false;

            case SlashCommand.NewRule:
                await CreateRuleAsync(arguments, cancellationToken).ConfigureAwait(false);
                return false;

            case SlashCommand.ReportBug:
                await CreateBugReportAsync(arguments, cancellationToken).ConfigureAwait(false);
                return false;

            case SlashCommand.Task:
                await HandleTaskAsync(arguments, cancellationToken).ConfigureAwait(false);
                return false;

            case SlashCommand.Team:
                await HandleTeamAsync(arguments, cancellationToken).ConfigureAwait(false);
                return false;

            case SlashCommand.Settings:
                if (_settings is not null)
                {
                    await _settings.RunAsync(arguments, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    _ui.WriteError("Interactive settings are unavailable in this host.");
                }
                return false;

            case SlashCommand.Config:
                await ShowConfigurationAsync(arguments, cancellationToken).ConfigureAwait(false);
                return false;

            case SlashCommand.Exit:
                return true;

            default:
                return false;
        }
    }

    private async Task CreateTaskHandoffAsync(string arguments, CancellationToken cancellationToken)
    {
        var focus = string.IsNullOrWhiteSpace(arguments)
            ? "Prepare a concise handoff: current objective, completed work, relevant files, decisions, risks, and next steps."
            : $"Prepare a concise handoff focused on: {arguments}";
        var compacted = await _compactor.CompactAsync(focus, cancellationToken).ConfigureAwait(false);
        if (compacted)
        {
            _ui.WriteSuccess("Task handoff prepared. Continue from the summary above as a fresh task.");
        }
        else
        {
            _ui.WriteError("Could not prepare a task handoff because the conversation could not be summarized.");
        }
    }

    private async Task CreateRuleAsync(string arguments, CancellationToken cancellationToken)
    {
        var parts = arguments.Split('|', 2, StringSplitOptions.TrimEntries);
        var name = parts.Length > 0 ? parts[0].Trim() : string.Empty;
        var body = parts.Length == 2 ? parts[1].Trim() : string.Empty;

        if (!IsSafeRuleName(name))
        {
            _ui.WriteError("Usage: :new-rule <name> [| <rule text>] (name must be one file name).");
            return;
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            _ui.WriteInfo("Enter rule text on the next line:");
            body = (await _ui.ReadInputAsync(cancellationToken).ConfigureAwait(false))?.Trim() ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            _ui.WriteError("Rule text cannot be empty.");
            return;
        }

        if (body.Length > MaxRuleCharacters)
        {
            _ui.WriteError($"Rule text is limited to {MaxRuleCharacters} characters.");
            return;
        }

        var path = Path.Combine(_options.ProjectRoot, CodaiPaths.ToPlatformPath(CodaiPaths.RulesDirectory), name + ".md");
        if (_fileSystem.FileExists(path))
        {
            _ui.WriteError($"Rule already exists: {Path.GetFileName(path)}");
            return;
        }

        if (!await _ui.ConfirmAsync($"Create project rule '{name}.md'?", cancellationToken).ConfigureAwait(false))
        {
            _ui.WriteInfo("Rule creation cancelled.");
            return;
        }

        _fileSystem.CreateDirectory(Path.GetDirectoryName(path)!);
        await _fileSystem.WriteAllTextAsync(path, body + Environment.NewLine, cancellationToken).ConfigureAwait(false);
        _ui.WriteSuccess($"Created project rule: {path}");
    }

    private async Task CreateBugReportAsync(string arguments, CancellationToken cancellationToken)
    {
        var summary = string.IsNullOrWhiteSpace(arguments) ? "Unspecified issue" : arguments.Trim();
        if (summary.Length > MaxBugSummaryCharacters)
        {
            summary = summary[..MaxBugSummaryCharacters];
        }

        var report = $"# Codai bug report\n\n" +
            $"## Summary\n{summary}\n\n" +
            $"## Diagnostics\n" +
            $"- Created: {DateTimeOffset.UtcNow:O}\n" +
            $"- Operating system: {Environment.OSVersion}\n" +
            $"- Runtime: {Environment.Version}\n" +
            $"- Provider: {_options.Provider}\n" +
            $"- Model: {_options.Model}\n" +
            $"- Mode: {_mode.Mode}\n" +
            $"- Project: {_options.ProjectRoot}\n" +
            $"- Conversation turns: {_history.Turns.Count}\n" +
            $"\n## Reproduction details\n\n";

        if (!await _ui.ConfirmAsync("Save a local diagnostic report?", cancellationToken).ConfigureAwait(false))
        {
            _ui.WriteInfo("Bug report cancelled.");
            return;
        }

        var directory = Path.Combine(_options.ProjectRoot, CodaiPaths.Folder, "reports");
        var fileName = $"bug-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfff}.md";
        var path = Path.Combine(directory, fileName);
        _fileSystem.CreateDirectory(directory);
        await _fileSystem.WriteAllTextAsync(path, report, cancellationToken).ConfigureAwait(false);
        _ui.WriteSuccess($"Saved bug report: {path}");
    }

    private static bool IsSafeRuleName(string name) =>
        !string.IsNullOrWhiteSpace(name) &&
        name.Length <= 128 &&
        name is not "." and not ".." &&
        name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
        !name.Contains('/') &&
        !name.Contains('\\');

    /// <summary>
    /// Reports the resolved configuration: what this session is, rather than what the
    /// files say it should be. The argument narrows the answer the same way
    /// <c>codai config &lt;target&gt;</c> does, because "which rules apply?" should not
    /// require reading through tools and MCP servers.
    /// </summary>
    private async Task ShowConfigurationAsync(string arguments, CancellationToken cancellationToken)
    {
        if (_inspector is null)
        {
            _ui.WriteError("Configuration inspection is unavailable in this host.");
            return;
        }

        if (!ConfigurationTargets.TryParse(arguments, out var target))
        {
            _ui.WriteError($"Unknown configuration target '{arguments}'. Use: {ConfigurationTargets.Names}");
            return;
        }

        var report = await _inspector.InspectAsync(target, cancellationToken).ConfigureAwait(false);

        foreach (var line in ConfigurationReportRenderer.Render(report))
        {
            _ui.WriteLine(line);
        }
    }

    private async Task HandleTaskAsync(string arguments, CancellationToken cancellationToken)
    {
        var parts = arguments.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length > 0 && string.Equals(parts[0], "history", StringComparison.OrdinalIgnoreCase))
        {
            await DisplayTaskHistoryAsync(parts.Length > 1 ? parts[1].Trim() : string.Empty, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (parts.Length > 0 && string.Equals(parts[0], "show", StringComparison.OrdinalIgnoreCase))
        {
            await ShowTaskAsync(parts.Length > 1 ? parts[1].Trim() : string.Empty, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (parts.Length > 0 && string.Equals(parts[0], "resume", StringComparison.OrdinalIgnoreCase))
        {
            await ResumeTaskAsync(parts.Length > 1 ? parts[1].Trim() : string.Empty, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (parts.Length > 0 && string.Equals(parts[0], "checkpoints", StringComparison.OrdinalIgnoreCase))
        {
            await DisplayCheckpointsAsync(parts.Length > 1 ? parts[1].Trim() : string.Empty, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (parts.Length > 0 && (string.Equals(parts[0], "storage", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(parts[0], "diagnostics", StringComparison.OrdinalIgnoreCase)))
        {
            await DisplayCheckpointStorageAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        if (parts.Length > 0 && string.Equals(parts[0], "restore", StringComparison.OrdinalIgnoreCase))
        {
            await RestoreCheckpointAsync(parts.Length > 1 ? parts[1].Trim() : string.Empty, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (parts.Length > 0 && string.Equals(parts[0], "diff", StringComparison.OrdinalIgnoreCase))
        {
            await CompareCheckpointAsync(parts.Length > 1 ? parts[1].Trim() : string.Empty, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (parts.Length > 0 && string.Equals(parts[0], "export", StringComparison.OrdinalIgnoreCase))
        {
            await ExportTaskAsync(parts.Length > 1 ? parts[1].Trim() : string.Empty, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (parts.Length > 0 && string.Equals(parts[0], "import", StringComparison.OrdinalIgnoreCase))
        {
            await ImportTaskAsync(parts.Length > 1 ? parts[1].Trim() : string.Empty, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (_backgroundAgents is null)
        {
            _ui.WriteError("Background agents are disabled. Start with sub-agents enabled.");
            return;
        }

        if (parts.Length == 0 || string.Equals(parts[0], "list", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var task in _backgroundAgents.List())
            {
                _ui.WriteInfo($"{task.Id}  {task.State}  {task.Description}");
                if (!string.IsNullOrWhiteSpace(task.Report) && task.State != BackgroundAgentState.Running)
                {
                    _ui.WriteLine(task.Report);
                }
            }

            return;
        }

        if (string.Equals(parts[0], "cancel", StringComparison.OrdinalIgnoreCase))
        {
            var id = parts.Length > 1 ? parts[1].Trim() : string.Empty;
            _ui.WriteLine(_backgroundAgents.Cancel(id) ? $"Cancelled {id}." : $"No running task found: {id}");
            return;
        }

        if (string.Equals(parts[0], "start", StringComparison.OrdinalIgnoreCase) && parts.Length > 1)
        {
            var separator = parts[1].IndexOf('|');
            if (separator > 0 && separator < parts[1].Length - 1)
            {
                var task = _backgroundAgents.Start(
                    parts[1][..separator].Trim(),
                    parts[1][(separator + 1)..].Trim());
                _ui.WriteSuccess($"Started background task {task.Id}: {task.Description}");
                return;
            }
        }

        _ui.WriteError("Usage: :task start <description> | <prompt>, :task list, :task export <task-id> <path>, or :task import <path>");
    }

    private async Task HandleTeamAsync(string arguments, CancellationToken cancellationToken)
    {
        if (_teams is null)
        {
            _ui.WriteError("Teams are disabled. Start with sub-agents enabled.");
            return;
        }

        var parts = arguments.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var command = parts.Length == 0 ? "list" : parts[0];
        var rest = parts.Length > 1 ? parts[1].Trim() : string.Empty;
        if (string.Equals(command, "list", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var team in _teams.List())
            {
                _ui.WriteInfo($"{team.Id}  {team.State}  {team.Coordinator}  specialists={team.Members.Count}  board={(team.Board ?? []).Count}");
                if (!string.IsNullOrWhiteSpace(team.CoordinatorPlan))
                {
                    _ui.WriteLine($"  plan: {team.CoordinatorPlan}");
                }
                foreach (var item in team.Board ?? [])
                {
                    _ui.WriteLine($"  [board {item.State}] {item.Id} {item.Text}  specialist={item.AssignedSpecialistId ?? "unassigned"}");
                }
                foreach (var message in team.Mailbox)
                {
                    _ui.WriteLine($"  [{message.From}] {message.Body}");
                }
            }

            return;
        }

        if (string.Equals(command, "add", StringComparison.OrdinalIgnoreCase))
        {
            var itemParts = rest.Split('|', 2, StringSplitOptions.TrimEntries);
            if (itemParts.Length is 1 or 2)
            {
                var item = _teams.AddBoardItem(itemParts[0].Split(' ', 2)[0],
                    itemParts[0].Contains(' ') ? itemParts[0][(itemParts[0].IndexOf(' ') + 1)..] : string.Empty,
                    itemParts.Length == 2 ? itemParts[1] : null);
                _ui.WriteLine(item is null ? "Board item rejected: unknown team, specialist, or bounded board." : $"Board item added: {item.Id} ({item.State}).");
                return;
            }
        }

        if (string.Equals(command, "complete", StringComparison.OrdinalIgnoreCase))
        {
            var itemParts = rest.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (itemParts.Length == 2)
            {
                _ui.WriteLine(_teams.CompleteBoardItem(itemParts[0], itemParts[1])
                    ? "Board item completed." : "Board item not found or already terminal.");
                return;
            }
        }

        if (string.Equals(command, "cancel", StringComparison.OrdinalIgnoreCase))
        {
            _ui.WriteLine(await _teams.CancelAsync(rest, cancellationToken).ConfigureAwait(false)
                ? $"Cancelled team {rest}." : $"No running team found: {rest}");
            return;
        }

        if (string.Equals(command, "message", StringComparison.OrdinalIgnoreCase))
        {
            var messageParts = rest.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            var separator = messageParts.Length == 2 ? messageParts[1].IndexOf('|') : -1;
            if (messageParts.Length == 2 && separator > 0)
            {
                var sent = _teams.Send(messageParts[0], messageParts[1][..separator].Trim(), messageParts[1][(separator + 1)..].Trim());
                _ui.WriteLine(sent ? "Message added." : "Message rejected: unknown team or bounded mailbox is full.");
                return;
            }
        }

        if (string.Equals(command, "start", StringComparison.OrdinalIgnoreCase))
        {
            var planSeparator = rest.IndexOf("||", StringComparison.Ordinal);
            var plan = default(string);
            var startHeader = rest;
            var specialistText = rest;
            if (planSeparator >= 0)
            {
                startHeader = rest[..planSeparator].Trim();
                specialistText = rest[(planSeparator + 2)..].Trim();
                var headerParts = startHeader.Split('|', 2, StringSplitOptions.TrimEntries);
                if (headerParts.Length != 2)
                {
                    _ui.WriteError("Usage: :team start <coordinator> | <plan> || <description> | <prompt> ; <description> | <prompt>");
                    return;
                }

                startHeader = headerParts[0];
                plan = headerParts[1];
            }

            var sections = specialistText.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (sections.Length >= 2)
            {
                var first = sections[0].Split('|', 2, StringSplitOptions.TrimEntries);
                var coordinator = planSeparator >= 0 ? startHeader : first[0];
                var specialistStart = planSeparator >= 0 ? 0 : 1;
                if (first.Length == (planSeparator >= 0 ? 2 : 3) && first.All(value => value.Length > 0))
                {
                    var specialists = new[] { (Description: first[specialistStart], Prompt: first[specialistStart + 1]) }
                        .Concat(sections.Skip(1).Select(section =>
                    {
                        var separator = section.IndexOf('|');
                        return separator > 0
                            ? (Description: section[..separator].Trim(), Prompt: section[(separator + 1)..].Trim())
                            : (Description: string.Empty, Prompt: string.Empty);
                    })).ToArray();
                    if (specialists.All(specialist => specialist.Description.Length > 0 && specialist.Prompt.Length > 0))
                    {
                        try
                        {
                            var team = await _teams.StartAsync(coordinator, specialists, plan, cancellationToken).ConfigureAwait(false);
                            _ui.WriteSuccess($"Started team {team.Id} with {team.Members.Count} read-only specialists.");
                            return;
                        }
                        catch (ArgumentOutOfRangeException ex)
                        {
                            _ui.WriteError(ex.Message);
                            return;
                        }
                    }
                }
            }
        }

        _ui.WriteError("Usage: :team start <coordinator> [|| <plan>] | <description> | <prompt> ; <description> | <prompt>, :team list, :team add <team-id> <item> [| <specialist-id>], :team complete <team-id> <item-id>, :team message <id> <from> | <message>, or :team cancel <id>");
    }

    private async Task ExportTaskAsync(string arguments, CancellationToken cancellationToken)
    {
        if (_taskStore is null)
        {
            _ui.WriteError("Task export is unavailable in this host.");
            return;
        }

        var parts = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
        {
            _ui.WriteError("Usage: :task export <task-id> <path>");
            return;
        }

        try
        {
            await _taskStore.ExportAsync(_options.ProjectRoot, parts[0], parts[1], cancellationToken)
                .ConfigureAwait(false);
            _ui.WriteSuccess($"Exported task {parts[0]} to {parts[1]}.");
        }
        catch (FileNotFoundException)
        {
            _ui.WriteError($"Task not found: {parts[0]}");
        }
        catch (IOException ex)
        {
            _ui.WriteError($"Could not export task: {ex.Message}");
        }
        catch (NotSupportedException ex)
        {
            _ui.WriteError(ex.Message);
        }
    }

    private async Task ImportTaskAsync(string arguments, CancellationToken cancellationToken)
    {
        if (_taskStore is null)
        {
            _ui.WriteError("Task import is unavailable in this host.");
            return;
        }

        var path = arguments.Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            _ui.WriteError("Usage: :task import <path>");
            return;
        }

        try
        {
            await _taskStore.ImportAsync(_options.ProjectRoot, path, cancellationToken).ConfigureAwait(false);
            _ui.WriteSuccess($"Imported task metadata from {path}.");
        }
        catch (InvalidDataException ex)
        {
            _ui.WriteError($"Could not import task: {ex.Message}");
        }
        catch (FileNotFoundException)
        {
            _ui.WriteError($"Task export not found: {path}");
        }
        catch (IOException ex)
        {
            _ui.WriteError($"Could not import task: {ex.Message}");
        }
        catch (NotSupportedException ex)
        {
            _ui.WriteError(ex.Message);
        }
    }

    private async Task CompareCheckpointAsync(string arguments, CancellationToken cancellationToken)
    {
        if (_checkpoints is null)
        {
            _ui.WriteError("Workspace checkpoints are unavailable in this host.");
            return;
        }

        var parts = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
        {
            _ui.WriteError("Usage: :task diff <task-id> <checkpoint-id>");
            return;
        }

        var diff = await _checkpoints.CompareAsync(_options.ProjectRoot, parts[0], parts[1], cancellationToken)
            .ConfigureAwait(false);
        _ui.WriteLine(string.IsNullOrWhiteSpace(diff) ? "No workspace changes." : diff);
    }

    private async Task DisplayCheckpointStorageAsync(CancellationToken cancellationToken)
    {
        if (_checkpoints is not IWorkspaceCheckpointDiagnostics diagnostics)
        {
            _ui.WriteError("Checkpoint storage diagnostics are unavailable in this host.");
            return;
        }

        var report = await diagnostics.GetStorageDiagnosticsAsync(_options.ProjectRoot, cancellationToken)
            .ConfigureAwait(false);
        _ui.WriteLine($"Checkpoints: {report.CheckpointCount}");
        _ui.WriteLine($"Retained storage: {report.RetainedBytes} bytes");
    }

    private async Task DisplayCheckpointsAsync(string taskId, CancellationToken cancellationToken)
    {
        if (_checkpoints is null || string.IsNullOrWhiteSpace(taskId))
        {
            _ui.WriteError("Usage: :task checkpoints <task-id>");
            return;
        }

        var checkpoints = await _checkpoints.ListAsync(_options.ProjectRoot, taskId, cancellationToken)
            .ConfigureAwait(false);
        if (checkpoints.Count == 0)
        {
            _ui.WriteInfo($"No checkpoints for task {taskId}.");
            return;
        }

        foreach (var checkpoint in checkpoints)
        {
            _ui.WriteInfo($"{checkpoint.Id}  {checkpoint.CreatedAt:u}  {checkpoint.Label}");
        }
    }

    public async Task<bool> RestoreCheckpointAsync(string arguments, bool confirmationGranted = false, CancellationToken cancellationToken = default)
    {
        var parts = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (_checkpoints is null || parts.Length is < 2 or > 3)
        {
            _ui.WriteError("Usage: :task restore <task-id> <checkpoint-id>");
            return false;
        }

        try
        {
            if (!TryParseRestoreMode(parts.Length == 3 ? parts[2] : "files-only", out var mode))
            {
                _ui.WriteError("Restore mode must be files-only, task-only, or files-and-task.");
                return false;
            }

            if (!confirmationGranted && !await _ui.ConfirmAsync(
                $"Restore checkpoint {parts[1]} for task {parts[0]}? This changes {FormatRestoreMode(mode)}.",
                cancellationToken).ConfigureAwait(false))
            {
                _ui.WriteInfo("Checkpoint restore declined.");
                return false;
            }

            await _checkpoints.RestoreAsync(_options.ProjectRoot, parts[0], parts[1], mode, cancellationToken)
                .ConfigureAwait(false);

            if (mode is WorkspaceCheckpointRestoreMode.TaskOnly or WorkspaceCheckpointRestoreMode.FilesAndTask)
            {
                var turns = await _sessionStore!.LoadAsync(_options.ProjectRoot, parts[0], cancellationToken)
                    .ConfigureAwait(false);
                _history.Clear();
                foreach (var turn in turns)
                {
                    _history.Add(turn);
                }
            }

            _ui.WriteSuccess($"Restored checkpoint {parts[1]} for task {parts[0]}.");
            return true;
        }
        catch (FileNotFoundException)
        {
            _ui.WriteError($"Checkpoint not found: {parts[1]}");
        }
        catch (IOException ex)
        {
            _ui.WriteError($"Could not restore checkpoint: {ex.Message}");
        }
        catch (NotSupportedException ex)
        {
            _ui.WriteError(ex.Message);
        }
        return false;
    }

    private async Task ShowTaskAsync(string taskId, CancellationToken cancellationToken)
    {
        if (_taskStore is null || string.IsNullOrWhiteSpace(taskId))
        {
            _ui.WriteError("Usage: :task show <task-id>");
            return;
        }

        var task = (await _taskStore.ListAsync(_options.ProjectRoot, cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(item => item.Id.Equals(taskId, StringComparison.OrdinalIgnoreCase));
        if (task is null)
        {
            _ui.WriteError($"Task not found: {taskId}");
            return;
        }

        _ui.WriteInfo($"{task.Id}  {task.Status}  {task.Request}");
        _ui.WriteLine($"Mode: {AgentModeParser.ToName(task.Mode)}");
        if (task.Plan.Count > 0)
        {
            _ui.WriteLine("Plan:");
            foreach (var step in task.Plan)
            {
                _ui.WriteLine($"  [{step.Status}] {step.Description}");
            }
        }
        if (!string.IsNullOrWhiteSpace(task.ProposedPlan))
        {
            _ui.WriteLine("Proposal:");
            _ui.WriteLine(task.ProposedPlan);
        }
        foreach (var checkpoint in task.Checkpoints)
        {
            _ui.WriteLine($"{checkpoint.Status}  {checkpoint.Description}");
        }
    }

    private async Task ResumeTaskAsync(string taskId, CancellationToken cancellationToken)
    {
        if (_taskStore is null || _sessionStore is null || string.IsNullOrWhiteSpace(taskId))
        {
            _ui.WriteError("Usage: :task resume <task-id>");
            return;
        }

        await PublishResumeAsync(AgentProgressEventKind.TaskResumeStarted, taskId, taskId, cancellationToken).ConfigureAwait(false);
        try
        {
            var task = (await _taskStore.ListAsync(_options.ProjectRoot, cancellationToken).ConfigureAwait(false))
                .FirstOrDefault(item => item.Id.Equals(taskId, StringComparison.OrdinalIgnoreCase));
            if (task is null)
            {
                _ui.WriteError($"Task not found: {taskId}");
                await PublishResumeAsync(AgentProgressEventKind.TaskResumeFailed, taskId, "Task not found.", CancellationToken.None).ConfigureAwait(false);
                return;
            }

            var turns = await _sessionStore.LoadAsync(_options.ProjectRoot, task.Id, cancellationToken).ConfigureAwait(false);
            if (turns.Count == 0)
            {
                _ui.WriteError($"No saved conversation for task {task.Id}.");
                await PublishResumeAsync(AgentProgressEventKind.TaskResumeFailed, taskId, "No saved conversation.", CancellationToken.None).ConfigureAwait(false);
                return;
            }

            _history.Clear();
            foreach (var turn in turns)
            {
                _history.Add(turn);
            }

            _mode.Mode = task.Mode;
            _ui.WriteSuccess($"Resumed task {task.Id} with {turns.Count} turn(s) in {AgentModeParser.ToName(task.Mode)} mode.");
            await PublishResumeAsync(AgentProgressEventKind.TaskResumeCompleted, taskId, $"{turns.Count} turn(s)", cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await PublishResumeAsync(AgentProgressEventKind.TaskResumeFailed, taskId, ex.Message, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private async Task PublishResumeAsync(AgentProgressEventKind kind, string taskId, string message, CancellationToken cancellationToken)
    {
        try
        {
            await _progress.PublishAsync(new AgentProgressEvent(taskId, "task resume", kind, message, DateTimeOffset.UtcNow), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
    }

    private static bool TryParseRestoreMode(string value, out WorkspaceCheckpointRestoreMode mode)
    {
        switch (value.ToLowerInvariant())
        {
            case "files-only":
                mode = WorkspaceCheckpointRestoreMode.FilesOnly;
                return true;
            case "task-only":
                mode = WorkspaceCheckpointRestoreMode.TaskOnly;
                return true;
            case "files-and-task":
                mode = WorkspaceCheckpointRestoreMode.FilesAndTask;
                return true;
            default:
                mode = default;
                return false;
        }
    }

    private static string FormatRestoreMode(WorkspaceCheckpointRestoreMode mode) => mode switch
    {
        WorkspaceCheckpointRestoreMode.FilesOnly => "workspace files",
        WorkspaceCheckpointRestoreMode.TaskOnly => "task metadata",
        _ => "workspace files and task metadata",
    };

    private async Task DisplayTaskHistoryAsync(string query, CancellationToken cancellationToken)
    {
        if (_taskStore is null)
        {
            _ui.WriteError("Task history is unavailable in this host.");
            return;
        }

        var tasks = await _taskStore
            .ListAsync(_options.ProjectRoot, cancellationToken)
            .ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(query))
        {
            tasks = tasks
                .Where(task =>
                    task.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    task.Request.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    task.Status.ToString().Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    task.Mode.ToString().Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        if (tasks.Count == 0)
        {
            _ui.WriteInfo(string.IsNullOrWhiteSpace(query)
                ? "No task history."
                : $"No task history matching '{query}'.");
            return;
        }

        foreach (var task in tasks)
        {
            _ui.WriteInfo($"{task.Id}  {task.Status}  {task.Request}");
        }
    }

    /// <summary>
    /// Shows or changes the agent mode. An empty argument reports the current mode, so
    /// <c>:mode</c> on its own answers the question "what am I allowed to do?" rather
    /// than doing nothing.
    /// </summary>
    private void ShowOrChangeMode(string arguments)
    {
        if (arguments.Length == 0)
        {
            _ui.WriteInfo(
                $"Mode: {AgentModeParser.ToName(_mode.Mode)} — {AgentModePolicy.Summary(_mode.Mode)} " +
                $"Change it with :mode {AgentModeParser.Names}.");
            return;
        }

        if (!AgentModeParser.TryParse(arguments, out var mode))
        {
            _ui.WriteError($"Unknown mode '{arguments}'. Use one of: {AgentModeParser.Names}.");
            return;
        }

        _mode.Mode = mode;

        _ui.WriteSuccess($"✔️ Mode is now {AgentModeParser.ToName(mode)}. {AgentModePolicy.Summary(mode)}");
    }

    private void ShowOrChangeModel(string arguments)
    {
        if (_models is null)
        {
            _ui.WriteError("Model selection is unavailable in this host.");
            return;
        }

        var parts = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            foreach (var mode in Enum.GetValues<AgentMode>())
            {
                var selected = _models.Get(mode);
                _ui.WriteLine($"{AgentModeParser.ToName(mode)}: {selected.Provider}/{selected.Model}");
            }

            return;
        }

        if (!AgentModeParser.TryParse(parts[0], out var targetMode))
        {
            _ui.WriteError("Usage: :model [plan|agent] [provider] [model]");
            return;
        }

        if (parts.Length == 1)
        {
            var selected = _models.Get(targetMode);
            _ui.WriteLine($"{AgentModeParser.ToName(targetMode)}: {selected.Provider}/{selected.Model}");
            return;
        }

        var provider = parts.Length == 2 && parts[1].Contains('/', StringComparison.Ordinal)
            ? parts[1][..parts[1].IndexOf('/')]
            : parts[1];
        var model = parts.Length == 2 && parts[1].Contains('/', StringComparison.Ordinal)
            ? parts[1][(parts[1].IndexOf('/') + 1)..]
            : parts.Length > 2 ? parts[2] : string.Empty;
        if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(model))
        {
            _ui.WriteError("Usage: :model [plan|agent] [provider] [model]");
            return;
        }

        _models.Set(targetMode, provider, model);
        _ui.WriteSuccess($"{AgentModeParser.ToName(targetMode)} model set to {provider}/{model}.");
    }

    /// <summary>Shows the token usage and estimated cost for the session.</summary>
    public void DisplayTokens()
    {
        var session = _tokenTracker.SessionUsage;
        var cost = _costCalculator.Calculate(_options.Provider, _options.Model, session);

        _ui.ShowTokens(_options.Model, session, _tokenTracker.LastUsage, cost);
    }
}
