using Codai.Core.Abstractions;
using Codai.Core.Projects;
using Codai.Core.Prompting;

namespace Codai.Session;

/// <summary>
/// The interactive coding session: it scans the project once, then answers user
/// requests in a loop, applying accepted file changes.
/// </summary>
/// <remarks>
/// This type only owns the loop and the session's lifecycle. The work of each step is
/// delegated to a dedicated collaborator:
/// <see cref="ProjectContextLoader"/> (scanning),
/// <see cref="ChatExchangeService"/> (prompting, streaming, history),
/// <see cref="ChangeApplicator"/> (proposed file edits) and
/// <see cref="SessionCommandHandler"/> (slash commands and token reporting).
/// </remarks>
public sealed class CodeSession
{
    private readonly SessionOptions _options;
    private readonly IUserInterface _ui;
    private readonly ProjectContextLoader _contextLoader;
    private readonly ChatExchangeService _chatExchange;
    private readonly ChangeApplicator _changeApplicator;
    private readonly SessionCommandHandler _commands;
    private readonly IChatHistory _history;
    private readonly IProjectInstructionsProvider _instructionsProvider;
    private readonly ISessionStore _sessionStore;

    /// <summary>Creates the session from its collaborators.</summary>
    public CodeSession(
        SessionOptions options,
        IUserInterface ui,
        ProjectContextLoader contextLoader,
        ChatExchangeService chatExchange,
        ChangeApplicator changeApplicator,
        SessionCommandHandler commands,
        IChatHistory history,
        IProjectInstructionsProvider instructionsProvider,
        ISessionStore sessionStore)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(contextLoader);
        ArgumentNullException.ThrowIfNull(chatExchange);
        ArgumentNullException.ThrowIfNull(changeApplicator);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(instructionsProvider);
        ArgumentNullException.ThrowIfNull(sessionStore);

        _options = options;
        _ui = ui;
        _contextLoader = contextLoader;
        _chatExchange = chatExchange;
        _changeApplicator = changeApplicator;
        _commands = commands;
        _history = history;
        _instructionsProvider = instructionsProvider;
        _sessionStore = sessionStore;
    }

    /// <summary>
    /// Runs the loop until the user exits, the input ends, or cancellation is requested.
    /// </summary>
    /// <returns>The process exit code.</returns>
    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        _ui.WriteLine(":help  Help for code subcommand");

        var context = await _contextLoader.LoadAsync(cancellationToken).ConfigureAwait(false);
        var instructions = await LoadInstructionsAsync(cancellationToken).ConfigureAwait(false);

        if (_options.ResumePreviousSession)
        {
            await ResumeAsync(cancellationToken).ConfigureAwait(false);
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

            var command = SlashCommandParser.Parse(userInput);
            if (command != SlashCommand.None)
            {
                if (await _commands.HandleAsync(command, cancellationToken).ConfigureAwait(false))
                {
                    break;
                }

                await PersistAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (!await HandleRequestAsync(context, userInput, instructions, cancellationToken).ConfigureAwait(false))
            {
                break;
            }

            await PersistAsync(cancellationToken).ConfigureAwait(false);
        }

        return 0;
    }

    /// <summary>
    /// Reads the project instructions, or returns null when the project has none.
    /// The file is read once per session: it steers the whole conversation.
    /// </summary>
    private async Task<ProjectInstructions?> LoadInstructionsAsync(CancellationToken cancellationToken)
    {
        var instructions = await _instructionsProvider
            .LoadAsync(_options.ProjectRoot, cancellationToken)
            .ConfigureAwait(false);

        if (instructions is not null)
        {
            _ui.WriteInfo($"📄 Following project instructions from {instructions.SourcePath}");
        }

        return instructions;
    }

    /// <summary>Restores the conversation saved by an earlier run.</summary>
    private async Task ResumeAsync(CancellationToken cancellationToken)
    {
        var turns = await _sessionStore
            .LoadAsync(_options.ProjectRoot, cancellationToken)
            .ConfigureAwait(false);

        if (turns.Count == 0)
        {
            _ui.WriteInfo("No previous session to continue.");
            return;
        }

        foreach (var turn in turns)
        {
            _history.Add(turn);
        }

        _ui.WriteSuccess($"✔️ Resumed {turns.Count} turn(s) from the previous session.");
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
    /// Handles one user request. Returns false when the session should end because the
    /// caller cancelled it.
    /// </summary>
    private async Task<bool> HandleRequestAsync(
        ProjectContext context,
        string userInput,
        ProjectInstructions? instructions,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _chatExchange.SendAsync(context, userInput, instructions, cancellationToken)
                .ConfigureAwait(false);

            await _changeApplicator.ApplyAsync(response, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (ChatProviderException ex)
        {
            _ui.WriteError(ex.Message);
        }

        _commands.DisplayTokens();

        return true;
    }
}
