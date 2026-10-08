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
/// <see cref="ChatExchangeService"/> (prompting, streaming, history),
/// <see cref="ChangeApplicator"/> (proposed file edits) and
/// <see cref="SessionCommandHandler"/> (slash commands and token reporting).
/// </remarks>
public sealed class CodeSession
{
    private readonly IUserInterface _ui;
    private readonly ProjectContextLoader _contextLoader;
    private readonly ChatExchangeService _chatExchange;
    private readonly ChangeApplicator _changeApplicator;
    private readonly SessionCommandHandler _commands;

    /// <summary>Creates the session from its collaborators.</summary>
    public CodeSession(
        IUserInterface ui,
        ProjectContextLoader contextLoader,
        ChatExchangeService chatExchange,
        ChangeApplicator changeApplicator,
        SessionCommandHandler commands)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(contextLoader);
        ArgumentNullException.ThrowIfNull(chatExchange);
        ArgumentNullException.ThrowIfNull(changeApplicator);
        ArgumentNullException.ThrowIfNull(commands);

        _ui = ui;
        _contextLoader = contextLoader;
        _chatExchange = chatExchange;
        _changeApplicator = changeApplicator;
        _commands = commands;
    }

    /// <summary>
    /// Runs the loop until the user exits, the input ends, or cancellation is requested.
    /// </summary>
    /// <returns>The process exit code.</returns>
    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        _ui.WriteLine(":help  Help for code subcommand");

        var context = await _contextLoader.LoadAsync(cancellationToken).ConfigureAwait(false);

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
                if (_commands.Handle(command))
                {
                    break;
                }

                continue;
            }

            if (!await HandleRequestAsync(context, userInput, cancellationToken).ConfigureAwait(false))
            {
                break;
            }
        }

        return 0;
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
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _chatExchange.SendAsync(context, userInput, cancellationToken)
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
