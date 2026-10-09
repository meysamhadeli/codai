using Codai.Core.Abstractions;
using Codai.Core.Projects;

namespace Codai.Session;

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
        ISessionStore sessionStore)
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
    }

    /// <summary>
    /// Runs the loop until the user exits, the input ends, or cancellation is requested.
    /// </summary>
    /// <returns>The process exit code.</returns>
    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        _ui.WriteLine(":help  Help for code subcommand");

        var context = await _contextLoader.LoadAsync(cancellationToken).ConfigureAwait(false);
        var guidance = await _guidanceLoader.LoadAsync(cancellationToken).ConfigureAwait(false);

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

            var request = userInput;

            if (userInput.TrimStart().StartsWith(':'))
            {
                var command = SlashCommandParser.Parse(userInput);

                if (command != SlashCommand.None)
                {
                    var arguments = SlashCommandParser.Arguments(userInput);

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

                _ui.WriteInfo($"📜 Running a project command from {expansion.SourcePath}");
                request = expansion.Request;
            }

            if (!await HandleRequestAsync(context, request, guidance, cancellationToken).ConfigureAwait(false))
            {
                break;
            }

            await PersistAsync(cancellationToken).ConfigureAwait(false);
        }

        return 0;
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
        ProjectGuidance guidance,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _chatExchange.SendAsync(context, userInput, guidance, cancellationToken)
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
