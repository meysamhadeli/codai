using Codai.Core.Abstractions;
using Codai.Core.Context;

namespace Codai.Session;

/// <summary>
/// Compacts the conversation on the session's behalf when it is about to outgrow the
/// model's context window, so the user never has to notice the window exists.
/// </summary>
/// <remarks>
/// <para>
/// The decision is made <em>before</em> a request is sent, from an estimate of what that
/// request will contain. Asking the provider instead would mean discovering the overflow
/// by being told the request failed, at which point the turn is already lost.
/// </para>
/// <para>
/// Two things keep this from doing harm. The estimate is taken with
/// <see cref="TokenEstimator"/>, which errs high, and the threshold carries a reserve, so
/// compaction happens a little early rather than too late. And the failure counter in
/// <see cref="CompactionPolicy"/> gives up after a few consecutive failures: a session
/// that cannot summarise is better off long than stuck in a loop of trying.
/// </para>
/// </remarks>
public sealed class AutoCompactor
{
    private readonly SessionOptions _options;
    private readonly IChatHistory _history;
    private readonly HistoryCompactor _compactor;
    private readonly CompactionPolicy _policy;
    private readonly IUserInterface _ui;

    /// <summary>Creates the compactor.</summary>
    public AutoCompactor(
        SessionOptions options,
        IChatHistory history,
        HistoryCompactor compactor,
        CompactionPolicy policy,
        IUserInterface ui)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(compactor);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(ui);

        _options = options;
        _history = history;
        _compactor = compactor;
        _policy = policy;
        _ui = ui;
    }

    /// <summary>
    /// Compacts the history when the request about to be sent would not fit.
    /// </summary>
    /// <param name="pendingRequest">
    /// The user's request, estimated with the rest of the conversation because it is
    /// part of what will be sent.
    /// </param>
    /// <param name="cancellationToken">Cancels the summarisation.</param>
    /// <returns>True when a compaction was performed.</returns>
    public async Task<bool> CompactIfNeededAsync(
        string pendingRequest,
        CancellationToken cancellationToken = default)
    {
        if (!_options.AutoCompact)
        {
            return false;
        }

        var budget = Measure(pendingRequest);

        if (!_policy.ShouldCompact(budget))
        {
            return false;
        }

        _ui.WriteInfo(
            $"📉 This conversation needs about {budget.UsedTokens:N0} of the " +
            $"{budget.ContextWindow:N0} tokens {_options.Model} can hold. Summarising it first.");

        if (await _compactor.CompactAsync(cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            _policy.RecordSuccess();
            return true;
        }

        _policy.RecordFailure();

        if (_policy.IsTripped)
        {
            // Said once, at the moment it happens: from here on the session simply
            // carries the long history, which is the lesser of the two failures.
            _ui.WriteError(
                "Summarising the conversation keeps failing, so it will not be attempted " +
                "again in this session. Use :clear-history to start the conversation over.");
        }

        return false;
    }

    /// <summary>
    /// Estimates the whole request — the conversation plus the new user message — and
    /// measures it against the window of the active model.
    /// </summary>
    private ContextBudget Measure(string pendingRequest)
    {
        var tokens = TokenEstimator.EstimateRequest(pendingRequest);

        foreach (var turn in _history.Turns)
        {
            tokens += TokenEstimator.EstimateRequest(turn.UserRequest, turn.AiResponse);
        }

        return _policy.Measure(tokens, ContextWindow.For(_options.Model));
    }
}
