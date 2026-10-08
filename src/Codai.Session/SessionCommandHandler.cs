using Codai.Core.Abstractions;

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
    private readonly IUserInterface _ui;

    /// <summary>Creates the handler.</summary>
    public SessionCommandHandler(
        SessionOptions options,
        IChatHistory history,
        ITokenTracker tokenTracker,
        ICostCalculator costCalculator,
        HistoryCompactor compactor,
        IUserInterface ui)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(tokenTracker);
        ArgumentNullException.ThrowIfNull(costCalculator);
        ArgumentNullException.ThrowIfNull(compactor);
        ArgumentNullException.ThrowIfNull(ui);

        _options = options;
        _history = history;
        _tokenTracker = tokenTracker;
        _costCalculator = costCalculator;
        _compactor = compactor;
        _ui = ui;
    }

    /// <summary>
    /// Executes a session command. Returns true when the session should end.
    /// </summary>
    public async Task<bool> HandleAsync(
        SlashCommand command,
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

            case SlashCommand.Compact:
                await _compactor.CompactAsync(cancellationToken).ConfigureAwait(false);
                return false;

            case SlashCommand.Exit:
                return true;

            default:
                return false;
        }
    }

    /// <summary>Shows the token usage and estimated cost for the session.</summary>
    public void DisplayTokens()
    {
        var session = _tokenTracker.SessionUsage;
        var cost = _costCalculator.Calculate(_options.Provider, _options.Model, session);

        _ui.ShowTokens(_options.Model, session, _tokenTracker.LastUsage, cost);
    }
}
