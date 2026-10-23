using Codai.Core.Abstractions;
using Codai.Core.Agents;
using Codai.Core.Chat;
using Codai.Core.IO;
using Codai.Core.Projects;

namespace Codai.Application;

/// <summary>
/// The interactive coding session: it scans the project once, then answers user
/// requests in a loop, applying accepted file changes.
/// </summary>
/// <remarks>
/// This type only owns the loop and the session's lifecycle. The work of each step is
/// delegated to a dedicated collaborator:
/// <see cref="ProjectContextLoader"/> (scanning),
/// <see cref="ProjectGuidanceLoader"/> (instructions and skills),
/// <see cref="ChatExchangeService"/> (prompting, streaming, history),
/// <see cref="ChangeApplicator"/> (proposed file edits),
/// <see cref="UserCommandResolver"/> (project-defined <c>:commands</c>) and
/// <see cref="SessionCommandHandler"/> (slash commands and token reporting).
/// </remarks>
public sealed class CodeSession
{
    private readonly SessionOptions _options;
    private readonly IUserInterface _ui;
    private readonly ProjectContextLoader _contextLoader;
    private readonly ProjectGuidanceLoader _guidanceLoader;
    private readonly ChatExchangeService _chatExchange;
    private readonly ChangeApplicator _changeApplicator;
    private readonly SessionCommandHandler _commands;
    private readonly UserCommandResolver _userCommands;
    private readonly IChatHistory _history;
    private readonly ISessionStore _sessionStore;
    private readonly IAgentTaskStore _taskStore;
    private readonly AutomaticRepairWorkflow? _repairWorkflow;
    private readonly IBackgroundAgentManager? _backgroundAgents;
    private readonly IAgentTeamCoordinator? _teams;
    private readonly AgentBudget _budget;
    private readonly IAgentHooks _hooks;
    private readonly IWorkspaceCheckpointStore _checkpoints;
    private readonly AgentTaskContext _taskContext;
    private readonly AgentModeState _mode;
    private readonly IAgentProgressSink _progress;
    private string? _conversationTaskId;
    public const int MaxPlanChars = 32_768;

    /// <summary>Creates the session from its collaborators.</summary>
    public CodeSession(
        SessionOptions options,
        IUserInterface ui,
        ProjectContextLoader contextLoader,
        ProjectGuidanceLoader guidanceLoader,
        ChatExchangeService chatExchange,
        ChangeApplicator changeApplicator,
        SessionCommandHandler commands,
        UserCommandResolver userCommands,
        IChatHistory history,
        ISessionStore sessionStore,
        IAgentTaskStore? taskStore = null,
        AutomaticRepairWorkflow? repairWorkflow = null,
        IBackgroundAgentManager? backgroundAgents = null,
        AgentBudget? budget = null,
        IAgentHooks? hooks = null,
        IWorkspaceCheckpointStore? checkpoints = null,
        AgentTaskContext? taskContext = null,
        IAgentProgressSink? progress = null,
        IAgentTeamCoordinator? teams = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(contextLoader);
        ArgumentNullException.ThrowIfNull(guidanceLoader);
        ArgumentNullException.ThrowIfNull(chatExchange);
        ArgumentNullException.ThrowIfNull(changeApplicator);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(userCommands);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(sessionStore);

        _options = options;
        _ui = ui;
        _contextLoader = contextLoader;
        _guidanceLoader = guidanceLoader;
        _chatExchange = chatExchange;
        _changeApplicator = changeApplicator;
        _commands = commands;
        _userCommands = userCommands;
        _history = history;
        _sessionStore = sessionStore;
        _taskStore = taskStore ?? NullAgentTaskStore.Instance;
        _repairWorkflow = repairWorkflow;
        _backgroundAgents = backgroundAgents;
        _teams = teams;
        _budget = budget ?? new AgentBudget();
        _hooks = hooks ?? NullAgentHooks.Instance;
        _checkpoints = checkpoints ?? (options.CheckpointsEnabled
            ? new FileWorkspaceCheckpointStore(PhysicalFileSystem.Instance, options.CheckpointRetention)
            : new NullWorkspaceCheckpointStore());
        _taskContext = taskContext ?? new AgentTaskContext();
        _mode = commands.ModeState;
        _progress = progress ?? NullAgentProgressSink.Instance;
    }

    /// <summary>
    /// Runs the loop until the user exits, the input ends, or cancellation is requested.
    /// </summary>
    /// <returns>The process exit code.</returns>
    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var context = await _contextLoader.LoadAsync(cancellationToken).ConfigureAwait(false);
        var guidance = await _guidanceLoader.LoadAsync(cancellationToken).ConfigureAwait(false);

        if (_options.ResumePreviousSession)
        {
            await ResumeAsync(cancellationToken).ConfigureAwait(false);
        }

        if (_backgroundAgents is not null)
        {
            await _backgroundAgents.RestoreAsync(cancellationToken).ConfigureAwait(false);
        }

        if (_teams is not null)
        {
            await _teams.RestoreAsync(cancellationToken).ConfigureAwait(false);
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            var userInput = await ReadInputAsync(cancellationToken).ConfigureAwait(false);

            // End of input (Ctrl+D, closed stdin) ends the session.
            if (userInput is null)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(userInput))
            {
                continue;
            }

            var request = userInput;

            if (userInput.TrimStart().StartsWith(':') || userInput.TrimStart().StartsWith('/'))
            {
                var command = SlashCommandParser.Parse(userInput);

                if (command != SlashCommand.None)
                {
                    var arguments = SlashCommandParser.Arguments(userInput);

                    if (command == SlashCommand.Session)
                    {
                        await ManageSessionAsync(arguments, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    if (command == SlashCommand.Act)
                    {
                        var result = await ExecutePlanCoreAsync(context, guidance, arguments, cancellationToken).ConfigureAwait(false);
                        await PersistAsync(CancellationToken.None).ConfigureAwait(false);
                        if (result == AgentTaskStatus.Cancelled && cancellationToken.IsCancellationRequested)
                        {
                            break;
                        }

                        continue;
                    }

                    if (command == SlashCommand.DeepPlan)
                    {
                        if (string.IsNullOrWhiteSpace(arguments))
                        {
                            _ui.WriteError("Usage: :deep-plan <request>");
                            continue;
                        }

                        _mode.Mode = AgentMode.Plan;
                        var deepPlanRequest =
                            "Create a deep implementation plan for this request. Explore the repository first. " +
                            "Return: current-state findings, numbered implementation steps with file paths, " +
                            "test and validation steps, risks, assumptions, and unresolved questions. " +
                            "Do not modify files or execute mutating commands.\n\nRequest:\n" + arguments;
                        if (await HandleRequestAsync(context, deepPlanRequest, guidance, null, cancellationToken)
                            == AgentTaskStatus.Cancelled && cancellationToken.IsCancellationRequested)
                        {
                            break;
                        }

                        await PersistAsync(cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    if (command == SlashCommand.Repair)
                    {
                        if (_repairWorkflow is null)
                        {
                            _ui.WriteError("Automatic repair requires shell workflow tools.");
                        }
                        else
                        {
                            await _repairWorkflow.RunAsync(context, guidance, cancellationToken: cancellationToken)
                                .ConfigureAwait(false);
                        }

                        await PersistAsync(cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    if (await _commands.HandleAsync(command, arguments, cancellationToken).ConfigureAwait(false))
                    {
                        break;
                    }

                    await PersistAsync(cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var expansion = await _userCommands.ExpandAsync(userInput, cancellationToken).ConfigureAwait(false);

                if (expansion is null)
                {
                    await ReportUnknownCommandAsync(userInput, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var sourceKind = expansion.IsSkill ? "skill" : "a project command";
                _ui.WriteInfo($"📜 Running {sourceKind} from {expansion.SourcePath}");
                request = expansion.Request;
            }

            if (await HandleRequestAsync(context, request, guidance, null, cancellationToken).ConfigureAwait(false)
                == AgentTaskStatus.Cancelled && cancellationToken.IsCancellationRequested)
            {
                break;
            }

            await PersistAsync(cancellationToken).ConfigureAwait(false);
        }

        return 0;
    }

    /// <summary>Runs one task without reading interactive input.</summary>
    public async Task<int> RunTaskAsync(string request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request);

        try
        {
            var context = await _contextLoader.LoadAsync(cancellationToken).ConfigureAwait(false);
            var guidance = await _guidanceLoader.LoadAsync(cancellationToken).ConfigureAwait(false);
            if (_options.ResumePreviousSession)
            {
                await ResumeAsync(cancellationToken).ConfigureAwait(false);
            }

            if (_backgroundAgents is not null)
            {
                await _backgroundAgents.RestoreAsync(cancellationToken).ConfigureAwait(false);
            }

            if (_teams is not null)
            {
                await _teams.RestoreAsync(cancellationToken).ConfigureAwait(false);
            }

            var status = await HandleRequestAsync(context, request, guidance, null, cancellationToken).ConfigureAwait(false);
            await PersistAsync(CancellationToken.None).ConfigureAwait(false);
            return status switch
            {
                AgentTaskStatus.Completed => 0,
                AgentTaskStatus.Cancelled => 130,
                _ => 1,
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return 130;
        }
    }

    /// <summary>
    /// Reports a colon-prefixed line that matched neither a session command nor a project
    /// command. It is never sent to the model: a typo silently answered as if it were a
    /// question is the worst possible outcome, because the answer looks plausible.
    /// </summary>
    private async Task ReportUnknownCommandAsync(string userInput, CancellationToken cancellationToken)
    {
        var names = await _userCommands.NamesAsync(cancellationToken).ConfigureAwait(false);

        var hint = names.Count > 0
            ? $" Type :help for the session commands, or use one of the project commands: {string.Join(", ", names)}."
            : " Type :help for the session commands.";

        _ui.WriteError($"Unknown command '{userInput}'.{hint}");
    }

    public async Task<int> ExecutePlanAsync(string instructions = "", CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instructions);
        try
        {
            var context = await _contextLoader.LoadAsync(cancellationToken).ConfigureAwait(false);
            var guidance = await _guidanceLoader.LoadAsync(cancellationToken).ConfigureAwait(false);
            var status = await ExecutePlanCoreAsync(context, guidance, instructions, cancellationToken).ConfigureAwait(false);
            await PersistAsync(CancellationToken.None).ConfigureAwait(false);
            return status switch
            {
                AgentTaskStatus.Completed => 0,
                AgentTaskStatus.Cancelled => 130,
                _ => 1,
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return 130;
        }
    }

    private async Task<AgentTaskStatus> ExecutePlanCoreAsync(ProjectContext context, ProjectGuidance guidance,
        string instructions, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var plan = await _taskStore.LoadAsync(_options.ProjectRoot, cancellationToken).ConfigureAwait(false);
        if (plan is not { Mode: AgentMode.Plan, Status: AgentTaskStatus.Completed } ||
            string.IsNullOrWhiteSpace(plan.ProposedPlan) || plan.ProposedPlan.Length > MaxPlanChars ||
            plan.Request.Length > MaxPlanChars || instructions.Length > MaxPlanChars)
        {
            _ui.WriteError("No usable completed Plan proposal. Generate a new plan before requesting Act execution.");
            return AgentTaskStatus.Failed;
        }

        if (_history.Turns.Count == 0)
        {
            await ResumeAsync(cancellationToken).ConfigureAwait(false);
        }

        if (!_history.Turns.Any(turn => turn.UserRequest == plan.Request && turn.AiResponse == plan.ProposedPlan))
        {
            _ui.WriteError("Planning conversation is unavailable, cleared, or does not match the saved proposal. Generate a new plan.");
            return AgentTaskStatus.Failed;
        }

        bool approved;
        try
        {
            approved = await _ui.ConfirmAsync($"Switch to Agent mode and execute plan {plan.Id}?\n{plan.ProposedPlan}",
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _ui.WriteError($"Plan execution consent unavailable: {ex.Message}");
            return AgentTaskStatus.Failed;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!approved)
        {
            _ui.WriteInfo("Plan execution declined; mode and proposal remain unchanged.");
            return AgentTaskStatus.Failed;
        }

        _mode.Mode = AgentMode.Agent;
        var request = $"Execute the reviewed plan for this request:\n{plan.Request}\n\nReviewed plan:\n{plan.ProposedPlan}";
        if (!string.IsNullOrWhiteSpace(instructions))
        {
            request += $"\n\nAdditional user instructions:\n{instructions}";
        }

        return await HandleRequestAsync(context, request, guidance, plan.Id, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Restores the conversation saved by an earlier run.</summary>
    private async Task ResumeAsync(CancellationToken cancellationToken)
    {
        var taskId = _options.ResumeTaskId ?? "session";
        await PublishResumeAsync(AgentProgressEventKind.TaskResumeStarted, taskId, taskId, cancellationToken).ConfigureAwait(false);
        try
        {
            var turns = _options.ResumeTaskId is { Length: > 0 } requestedTaskId
                ? await _sessionStore.LoadAsync(_options.ProjectRoot, requestedTaskId, cancellationToken).ConfigureAwait(false)
                : await _sessionStore.LoadAsync(_options.ProjectRoot, cancellationToken).ConfigureAwait(false);

            if (turns.Count == 0)
            {
                _ui.WriteInfo("No previous session to continue.");
                await PublishResumeAsync(AgentProgressEventKind.TaskResumeFailed, taskId, "No previous session.", CancellationToken.None).ConfigureAwait(false);
                return;
            }

            foreach (var turn in turns)
            {
                _history.Add(turn);
            }

            _conversationTaskId = _options.ResumeTaskId;

            _ui.WriteSuccess($"✔️ Resumed {turns.Count} turn(s) from the previous session.");
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

    /// <summary>
    /// Writes the conversation to the session store after every step, so a crash or a
    /// Ctrl+C never loses more than the step in flight. Failing to persist is reported
    /// but never ends the session.
    /// </summary>
    private async Task PersistAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _sessionStore
                .SaveAsync(_options.ProjectRoot, _history.Turns, cancellationToken)
                .ConfigureAwait(false);

            var latestTask = await _taskStore.LoadAsync(_options.ProjectRoot, cancellationToken).ConfigureAwait(false);
            var taskId = _conversationTaskId ?? latestTask?.Id;
            if (taskId is not null)
            {
                await _sessionStore
                    .SaveAsync(_options.ProjectRoot, taskId, _history.Turns, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ui.WriteError($"Could not save the session: {ex.Message}");
        }
    }

    private async Task ManageSessionAsync(string operation, CancellationToken cancellationToken)
    {
        try
        {
            if (operation is "new" or "fork")
            {
                var confirmation = await _ui.ChooseAsync("Codai / " + (operation == "new" ? "Start an empty conversation?" : "Branch this conversation?"),
                [
                    new("Keep current conversation", "Return without switching", "cancel"),
                    new("Continue", "Save the current conversation before switching", "confirm"),
                ], cancellationToken).ConfigureAwait(false);
                if (confirmation != "confirm")
                {
                    return;
                }
                await ArchiveConversationAsync(cancellationToken).ConfigureAwait(false);
                var branch = (AgentTask.Create(operation == "new" ? "Fresh conversation" : "Conversation branch") with { Mode = _mode.Mode }).Complete();
                var turns = operation == "new" ? [] : _history.Turns.ToArray();
                await _sessionStore.SaveAsync(_options.ProjectRoot, branch.Id, turns, cancellationToken).ConfigureAwait(false);
                await _taskStore.SaveAsync(_options.ProjectRoot, branch, cancellationToken).ConfigureAwait(false);
                await _sessionStore.SaveAsync(_options.ProjectRoot, turns, cancellationToken).ConfigureAwait(false);
                _conversationTaskId = branch.Id;
                if (operation == "new")
                {
                    _history.Clear();
                    _budget.BeginTask();
                    await _commands.HandleAsync(SlashCommand.ClearToken, cancellationToken: cancellationToken).ConfigureAwait(false);
                    _ui.Clear();
                }
                _ui.WriteSuccess($"{(operation == "new" ? "Started conversation" : "Created conversation branch")} {branch.Id}. Previous saved history remains available.");
                return;
            }
            if (operation is not ("history" or "restore"))
            {
                _ui.WriteError("Use :session [new|fork|history|restore].");
                return;
            }
            var tasks = (await _taskStore.ListAsync(_options.ProjectRoot, cancellationToken).ConfigureAwait(false))
                .OrderByDescending(task => task.UpdatedAt).Take(100).ToArray();
            if (tasks.Length == 0)
            {
                _ui.WriteInfo("No saved conversations are available.");
                return;
            }
            var selectedId = await _ui.ChooseAsync("Codai / Saved conversations",
                tasks.Select(task => new SessionMenuItem(task.Request, $"{task.UpdatedAt:u}  {task.Mode}  {task.Id}", task.Id)).ToArray(), cancellationToken).ConfigureAwait(false);
            var selected = tasks.FirstOrDefault(task => task.Id == selectedId);
            if (selected is null)
            {
                return;
            }
            if (operation == "restore")
            {
                var checkpoints = await _checkpoints.ListAsync(_options.ProjectRoot, selected.Id, cancellationToken).ConfigureAwait(false);
                if (checkpoints.Count == 0)
                {
                    _ui.WriteInfo("No checkpoints exist for the selected conversation.");
                    return;
                }
                var checkpointId = await _ui.ChooseAsync("Codai / Checkpoint",
                    checkpoints.Select(checkpoint => new SessionMenuItem(checkpoint.Label, $"{checkpoint.CreatedAt:u}", checkpoint.Id)).ToArray(), cancellationToken).ConfigureAwait(false);
                if (!checkpoints.Any(checkpoint => checkpoint.Id == checkpointId))
                {
                    return;
                }
                var scope = await _ui.ChooseAsync("Codai / Restore scope",
                [
                    new("Workspace files", "Replace files only; keep current conversation", "files-only"),
                    new("Conversation", "Restore saved task conversation only", "task-only"),
                    new("Workspace and conversation", "Replace files and restore saved task conversation", "files-and-task"),
                ], cancellationToken).ConfigureAwait(false);
                if (scope is not ("files-only" or "task-only" or "files-and-task"))
                {
                    return;
                }
                var consent = await _ui.ChooseAsync("Codai / Confirm checkpoint restore",
                [
                    new("Keep current state", "Do not restore this checkpoint", "cancel"),
                    new("Restore selected checkpoint", "This may replace workspace files and conversation", "confirm"),
                ], cancellationToken).ConfigureAwait(false);
                if (consent != "confirm")
                {
                    return;
                }
                await ArchiveConversationAsync(cancellationToken).ConfigureAwait(false);
                if (!await _commands.RestoreCheckpointAsync($"{selected.Id} {checkpointId} {scope}", confirmationGranted: true, cancellationToken).ConfigureAwait(false))
                {
                    return;
                }
                if (scope != "files-only")
                {
                    await CreateConversationBranchAsync("Conversation after checkpoint restore", _history.Turns, _mode.Mode, cancellationToken).ConfigureAwait(false);
                    ShowConversation();
                }
                await PersistAsync(cancellationToken).ConfigureAwait(false);
                return;
            }
            var turnsToResume = await _sessionStore.LoadAsync(_options.ProjectRoot, selected.Id, cancellationToken).ConfigureAwait(false);
            if (turnsToResume.Count == 0)
            {
                _ui.WriteInfo("The selected task has no saved conversation.");
                return;
            }
            await ArchiveConversationAsync(cancellationToken).ConfigureAwait(false);
            await CreateConversationBranchAsync("Continued saved conversation", turnsToResume, selected.Mode, cancellationToken).ConfigureAwait(false);
            _history.Clear();
            foreach (var turn in turnsToResume)
            {
                _history.Add(turn);
            }
            _mode.Mode = selected.Mode;
            ShowConversation();
            _ui.WriteSuccess($"Resumed conversation from {selected.Id} in a new branch.");
            await PersistAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _ui.WriteError($"Could not switch conversation: {exception.Message}");
        }
    }

    private async Task ArchiveConversationAsync(CancellationToken cancellationToken)
    {
        if (_history.Turns.Count == 0)
        {
            return;
        }
        var latest = await _taskStore.LoadAsync(_options.ProjectRoot, cancellationToken).ConfigureAwait(false);
        var id = _conversationTaskId ?? latest?.Id;
        if (id is null)
        {
            var archive = (AgentTask.Create("Saved conversation") with { Mode = _mode.Mode }).Complete();
            id = archive.Id;
            await _taskStore.SaveAsync(_options.ProjectRoot, archive, cancellationToken).ConfigureAwait(false);
        }
        await _sessionStore.SaveAsync(_options.ProjectRoot, id, _history.Turns, cancellationToken).ConfigureAwait(false);
        await _sessionStore.SaveAsync(_options.ProjectRoot, _history.Turns, cancellationToken).ConfigureAwait(false);
    }

    private async Task CreateConversationBranchAsync(string label, IReadOnlyList<ChatTurn> turns, AgentMode mode, CancellationToken cancellationToken)
    {
        var branch = (AgentTask.Create(label) with { Mode = mode }).Complete();
        await _sessionStore.SaveAsync(_options.ProjectRoot, branch.Id, turns, cancellationToken).ConfigureAwait(false);
        await _taskStore.SaveAsync(_options.ProjectRoot, branch, cancellationToken).ConfigureAwait(false);
        await _sessionStore.SaveAsync(_options.ProjectRoot, turns, cancellationToken).ConfigureAwait(false);
        _conversationTaskId = branch.Id;
    }

    private void ShowConversation()
    {
        _ui.Clear();
        foreach (var turn in _history.Turns.TakeLast(20))
        {
            _ui.WriteLine("> " + turn.UserRequest);
            _ui.WriteMarkdown(turn.AiResponse + "\n\n");
        }
    }

    /// <summary>Reads a line, returning null when the input ended or was cancelled.</summary>
    private async Task<string?> ReadInputAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _ui.ReadInputAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// Handles one user request and returns its terminal status.
    /// </summary>
    private Task<AgentTaskStatus> HandleRequestAsync(
        ProjectContext context,
        string userInput,
        ProjectGuidance guidance,
        string? sourcePlanId,
        CancellationToken cancellationToken) =>
        _ui.RunTurnAsync(token => HandleRequestCoreAsync(context, userInput, guidance, sourcePlanId, token), cancellationToken);

    private async Task<AgentTaskStatus> HandleRequestCoreAsync(
        ProjectContext context,
        string userInput,
        ProjectGuidance guidance,
        string? sourcePlanId,
        CancellationToken cancellationToken)
    {
        AgentTask? task = null;
        var status = AgentTaskStatus.Completed;

        try
        {
            _budget.BeginTask();
            task = (AgentTask.Create(userInput) with { Mode = _mode.Mode, SourcePlanId = sourcePlanId }).Start();
            _taskContext.TaskId = task.Id;
            _conversationTaskId = task.Id;
            await _sessionStore.SaveAsync(_options.ProjectRoot, task.Id, _history.Turns, cancellationToken)
                .ConfigureAwait(false);
            await _checkpoints.CaptureAsync(_options.ProjectRoot, task.Id, "Before task", cancellationToken)
                .ConfigureAwait(false);
            await SaveTaskAsync(task, cancellationToken).ConfigureAwait(false);
            await CheckHookAsync(AgentHookEvent.TaskStarted, task, cancellationToken).ConfigureAwait(false);
            await CheckHookAsync(AgentHookEvent.UserPromptSubmitted, task, cancellationToken).ConfigureAwait(false);

            task = task
                .StartPlanStep("generate-response")
                .StartCheckpoint("Generate response", "generate-response");
            await SaveTaskAsync(task, cancellationToken).ConfigureAwait(false);
            var response = await _chatExchange.SendAsync(context, userInput, guidance, cancellationToken)
                .ConfigureAwait(false);

            if (task.Mode == AgentMode.Plan)
            {
                task = task with { ProposedPlan = response };
            }

            task = task
                .CompleteCheckpoint()
                .CompletePlanStep("generate-response")
                .StartPlanStep("apply-changes")
                .StartCheckpoint("Apply changes", "apply-changes");
            await SaveTaskAsync(task, cancellationToken).ConfigureAwait(false);
            await _changeApplicator.ApplyAsync(response, cancellationToken).ConfigureAwait(false);
            await _sessionStore.SaveAsync(_options.ProjectRoot, task.Id, _history.Turns, cancellationToken)
                .ConfigureAwait(false);
            await _checkpoints.CaptureAsync(_options.ProjectRoot, task.Id, "After task", cancellationToken)
                .ConfigureAwait(false);
            await CheckHookAsync(AgentHookEvent.TaskCompleted, task, cancellationToken).ConfigureAwait(false);
            await SaveTaskAsync(
                task.CompleteCheckpoint().CompletePlanStep("apply-changes").Complete(),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await SaveTaskAsync(
                (task ?? AgentTask.Create(userInput).Start()).Cancel(),
                CancellationToken.None).ConfigureAwait(false);
            _commands.DisplayTokens();
            return AgentTaskStatus.Cancelled;
        }
        catch (Exception ex) when (ex is ChatProviderException or AgentHookRejectedException or IOException or UnauthorizedAccessException)
        {
            status = AgentTaskStatus.Failed;
            _ui.WriteError(ex.Message);
            await SaveTaskAsync(
                (task ?? AgentTask.Create(userInput).Start()).Fail(ex.Message),
                CancellationToken.None).ConfigureAwait(false);
            if (task is not null && !cancellationToken.IsCancellationRequested)
            {
                await NotifyHookAsync(AgentHookEvent.TaskFailed, task, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _taskContext.TaskId = null;
        }

        _commands.DisplayTokens();

        return status;
    }

    private async Task CheckHookAsync(AgentHookEvent stage, AgentTask task, CancellationToken cancellationToken)
    {
        AgentHookDecision decision;
        try
        {
            decision = await _hooks.InvokeAsync(new AgentHookContext(stage, _options.ProjectRoot)
            {
                TaskId = task.Id,
                Prompt = task.Request,
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new AgentHookRejectedException($"Hook handler failed at {stage}: {ex.Message}");
        }

        if (!decision.Allowed)
        {
            throw new AgentHookRejectedException(decision.Reason ?? $"Hook blocked {stage}.");
        }
    }

    private async Task NotifyHookAsync(AgentHookEvent stage, AgentTask task, CancellationToken cancellationToken)
    {
        try
        {
            await CheckHookAsync(stage, task, cancellationToken).ConfigureAwait(false);
        }
        catch (AgentHookRejectedException ex)
        {
            _ui.WriteError(ex.Message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _ui.WriteInfo("Failure-hook notification cancelled; task failure remains saved.");
        }
    }

    private async Task SaveTaskAsync(AgentTask task, CancellationToken cancellationToken)
    {
        try
        {
            await _taskStore.SaveAsync(_options.ProjectRoot, task, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ui.WriteError($"Could not save agent task state: {ex.Message}");
        }
    }
}
