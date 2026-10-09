using Codai.Core.Abstractions;
using Codai.Core.Agents;

namespace Codai.Session;

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

    /// <summary>Creates the handler.</summary>
    public SessionCommandHandler(
        SessionOptions options,
        IChatHistory history,
        ITokenTracker tokenTracker,
        ICostCalculator costCalculator,
        HistoryCompactor compactor,
        AgentModeState mode,
        IUserInterface ui)
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
                return false;

            case SlashCommand.Mode:
                ShowOrChangeMode(arguments);
                return false;

            case SlashCommand.Compact:
                // Anything the user typed after :compact is what they want the summary to
                // concentrate on, which is the difference between an acceptable summary
                // and one that is useful.
                await _compactor
                    .CompactAsync(arguments, cancellationToken)
                    .ConfigureAwait(false);
                return false;

            case SlashCommand.Exit:
                return true;

            default:
                return false;
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

    /// <summary>Shows the token usage and estimated cost for the session.</summary>
    public void DisplayTokens()
    {
        var session = _tokenTracker.SessionUsage;
        var cost = _costCalculator.Calculate(_options.Provider, _options.Model, session);

        _ui.ShowTokens(_options.Model, session, _tokenTracker.LastUsage, cost);
    }
}
