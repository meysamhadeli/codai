using Codai.Core.Abstractions;
using Codai.Core.Agents;
using Codai.Core.Chat;
using Codai.Core.Context;
using Codai.Core.Tools;

namespace Codai.Application;

/// <summary>
/// Keeps the conversation inside the model's context window without the user ever noticing
/// that the window exists.
/// </summary>
/// <remarks>
/// <para>
/// The decision is made from an estimate of what is about to be sent, because asking the
/// provider instead means discovering the overflow by being told the request failed — at
/// which point the turn is already lost.
/// </para>
/// <para>
/// Two thresholds, and the gap between them is the whole point.
/// </para>
/// <list type="bullet">
/// <item>
/// <description>
/// Past <see cref="ContextPressure.ProactiveRatio"/> there is still room to send the
/// request, so summarising is started <em>in the background</em> while the answer is being
/// read. The user waits for nothing.
/// </description>
/// </item>
/// <item>
/// <description>
/// Past <see cref="ContextPressure.CriticalRatio"/> there is no room left, so the request
/// waits for the summary — which by then has usually been written already, because
/// <see cref="BackgroundSummarizer"/> started it a turn or two earlier.
/// </description>
/// </item>
/// </list>
/// <para>
/// Below both of them sits a tier that costs nothing. <see cref="MicroCompactor"/> clears the
/// bulk of old tool output, which is what makes an agent conversation long in the first
/// place, and it runs first: a conversation it brings back under the threshold never pays for
/// a summary at all.
/// </para>
/// <para>
/// Three things keep this from doing harm. The estimate errs high and the threshold carries
/// a reserve and a safety margin, so compaction happens a little early rather than too late.
/// The tool definitions are measured and deducted, so a session in agent mode does not
/// compact because of room the conversation cannot actually use. And the failure counter in
/// <see cref="CompactionPolicy"/> gives up after a few consecutive failures: a session that
/// cannot summarise is better off long than stuck in a loop of trying.
/// </para>
/// <para>
/// One case bypasses all of that. When the provider itself refuses a request for being too
/// long, the estimate that decided it would fit is known to have been wrong, so
/// <see cref="RecoverFromOverflowAsync"/> shortens the conversation directly.
/// </para>
/// </remarks>
public sealed class AutoCompactor
{
    /// <summary>
    /// Share of the conversation budget kept as a tail when the session summarises. The
    /// tail is what the next request reads verbatim, so it has to be large enough to be
    /// worth keeping and small enough that the replacement leaves the conversation under the
    /// threshold that started the compaction in the first place.
    /// </summary>
    public const double TailHeadroomRatio = 0.25;

    private readonly SessionOptions _options;
    private readonly IChatHistory _history;
    private readonly HistoryCompactor _compactor;
    private readonly BackgroundSummarizer _summarizer;
    private readonly CompactionPolicy _policy;
    private readonly IUserInterface _ui;
    private readonly AgentModeState _mode;
    private readonly IToolRegistry _registry;
    private readonly MicroCompactor? _trimmer;

    /// <summary>Creates the compactor.</summary>
    public AutoCompactor(
        SessionOptions options,
        IChatHistory history,
        HistoryCompactor compactor,
        BackgroundSummarizer summarizer,
        CompactionPolicy policy,
        IUserInterface ui,
        AgentModeState mode,
        IToolRegistry registry,
        MicroCompactor? trimmer = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(compactor);
        ArgumentNullException.ThrowIfNull(summarizer);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(mode);
        ArgumentNullException.ThrowIfNull(registry);

        _options = options;
        _history = history;
        _compactor = compactor;
        _summarizer = summarizer;
        _policy = policy;
        _ui = ui;
        _mode = mode;
        _registry = registry;
        _trimmer = trimmer;
    }

    /// <summary>
    /// Does whatever the conversation needs before a request is sent.
    /// </summary>
    /// <remarks>
    /// This is the only place a plan is applied, because it is the only place that is
    /// reliably between turns: the history is not being read by anything, so rewriting it
    /// cannot pull the ground out from under a request.
    /// </remarks>
    /// <param name="pendingRequest">
    /// The user's request, estimated with the rest of the conversation because it is part of
    /// what will be sent.
    /// </param>
    /// <param name="cancellationToken">Cancels the summarisation when the request has to wait for one.</param>
    public async Task BeforeRequestAsync(
        string pendingRequest,
        CancellationToken cancellationToken = default)
    {
        if (!_options.AutoCompact || _policy.IsTripped)
        {
            return;
        }

        // Whatever the background managed to produce since the last turn. Applied first, so
        // the measurement below is of the conversation that will actually be sent.
        if (_summarizer.TryTake(out var produced))
        {
            await _compactor.ApplyAsync(produced, cancellationToken).ConfigureAwait(false);
            _policy.RecordSuccess();
            Report(produced);
        }

        var pressure = Measure(pendingRequest);

        if (pressure.IsUrgent)
        {
            // Clearing old tool output needs no model and cannot fail, so it is tried before
            // the request is made to wait for a summary: if it is enough, the user waits for
            // nothing at all.
            if (await TryTrimAsync(pressure, cancellationToken).ConfigureAwait(false))
            {
                pressure = Measure(pendingRequest);

                if (!pressure.IsUrgent)
                {
                    return;
                }
            }

            // No room left to send it, so the summary is not a convenience any more: the
            // request cannot go out until the conversation is shorter.
            await WaitForShorterHistoryAsync(pressure, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (_policy.ShouldCompact(pressure))
        {
            // The cheap tier first, for the same reason: a conversation that can be brought
            // back under the threshold by clearing what the model has stopped reading does
            // not need the model to be asked anything.
            if (await TryTrimAsync(pressure, cancellationToken).ConfigureAwait(false) &&
                !_policy.ShouldCompact(Measure(pendingRequest)))
            {
                return;
            }

            // Room to send it, no room to keep growing. Started now and not waited for: it
            // finishes while the user reads the answer.
            _summarizer.Start(preserveRecentTokens: MeasureTailTokens());
        }
    }

    /// <summary>
    /// Runs the tier below summarising, which clears old tool output without a model call.
    /// </summary>
    /// <returns>True when the conversation is shorter than it was.</returns>
    private async Task<bool> TryTrimAsync(
        ContextPressure pressure,
        CancellationToken cancellationToken)
    {
        if (_trimmer is null)
        {
            return false;
        }

        var saved = await _trimmer.TrimAsync(pressure, cancellationToken).ConfigureAwait(false);

        return saved > 0;
    }

    /// <summary>
    /// Takes stock once the answer has been added, and starts summarising if the
    /// conversation has grown past the point where it should.
    /// </summary>
    /// <remarks>
    /// The measurement includes the turn that was just recorded, which is what puts the work
    /// one turn ahead of the problem: by the time the next request is built, the summary has
    /// had a whole answer's worth of time to be written.
    /// </remarks>
    public void AfterResponse()
    {
        if (!_options.AutoCompact || _policy.IsTripped)
        {
            return;
        }

        if (_summarizer.TryTake(out var produced))
        {
            _compactor.Apply(produced);
            _policy.RecordSuccess();
            Report(produced);
        }

        if (_policy.ShouldCompact(Measure()))
        {
            _summarizer.Start(preserveRecentTokens: MeasureTailTokens());
        }
    }

    public async Task AfterResponseAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.AutoCompact || _policy.IsTripped)
        {
            return;
        }

        if (_summarizer.TryTake(out var produced))
        {
            await _compactor.ApplyAsync(produced, cancellationToken).ConfigureAwait(false);
            _policy.RecordSuccess();
            Report(produced);
        }

        if (_policy.ShouldCompact(Measure()))
        {
            _summarizer.Start(preserveRecentTokens: MeasureTailTokens());
        }
    }

    /// <summary>
    /// Shortens the conversation because the provider refused the request as too long.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the one case where the estimate is known to have been wrong: the provider
    /// itself said the request does not fit, so the conversation has to change before the
    /// request is sent again. It is deliberately not the normal path — arriving here means a
    /// request was already refused.
    /// </para>
    /// <para>
    /// The deterministic strategy is forced. Recovery must not depend on a model call
    /// succeeding, and a summarisation is a model call that may itself be too long to send;
    /// shortening without a model is the only thing that works when the conversation is
    /// already at the limit.
    /// </para>
    /// <para>
    /// The breaker is ignored on purpose. It exists to stop a session spending every turn on
    /// a summary that keeps failing, but that rule is about work nobody asked for. Here the
    /// alternative to a shorter conversation is no answer at all, so one attempt is worth
    /// making when automatic compaction is enabled. Explicitly disabling compaction also
    /// disables this automatic recovery path.
    /// </para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the shortening.</param>
    /// <returns>True when the conversation is shorter and the request is worth sending again.</returns>
    public async Task<bool> RecoverFromOverflowAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_options.AutoCompact)
        {
            return false;
        }

        _ui.WriteInfo(
            $"📉 {_options.Model} refused the request as longer than its context window. " +
            "Shortening the conversation before trying it again.");

        var plan = await _compactor
            .TryPlanAsync(
                instructions: null,
                foreground: true,
                cancellationToken,
                strategy: CompactionStrategy.Basic,
                preserveRecentTokens: MeasureTailTokens())
            .ConfigureAwait(false);

        if (plan is null)
        {
            _policy.RecordFailure();
            return false;
        }

        await _compactor.ApplyAsync(plan, cancellationToken).ConfigureAwait(false);
        _policy.RecordSuccess();

        return true;
    }

    /// <summary>
    /// Makes the conversation short enough to send, waiting for a summary if one is being
    /// written and writing one if it is not.
    /// </summary>
    private async Task WaitForShorterHistoryAsync(
        ContextPressure pressure,
        CancellationToken cancellationToken)
    {
        _ui.WriteInfo(
            $"📉 This conversation needs about {pressure.UsedTokens:N0} of the " +
            $"{pressure.MessageBudget:N0} tokens {_options.Model} can spare for it " +
            $"({pressure.Ratio:P0}). Shortening it before the request goes out.");

        // Started here so that WaitAsync has something to wait for on the turn where the
        // pressure arrives without warning.
        _summarizer.Start(preserveRecentTokens: MeasureTailTokens());

        var plan = await _summarizer.WaitAsync(cancellationToken).ConfigureAwait(false);

        if (plan is null)
        {
            // The background attempt came back with nothing, so this turn pays for a
            // foreground one. Summarising is the only way the request fits, which makes it
            // worth the wait.
            plan = await _compactor
                .TryPlanAsync(
                    instructions: null,
                    foreground: true,
                    cancellationToken,
                    preserveRecentTokens: MeasureTailTokens())
                .ConfigureAwait(false);
        }

        if (plan is not null)
        {
            await _compactor.ApplyAsync(plan, cancellationToken).ConfigureAwait(false);
            _policy.RecordSuccess();

            // A replacement that leaves the conversation where it was compacted has bought
            // one turn rather than a fix, and the next request would shorten it again. Said
            // out loud, because the alternative is a session that looks like it is stuck in a
            // loop and gives no hint that its history is simply larger than the window.
            var after = Measure();

            if (after.IsWorthCompacting)
            {
                _ui.WriteInfo(
                    $"📉 It is still about {after.UsedTokens:N0} of the {after.MessageBudget:N0} " +
                    $"tokens {_options.Model} can spare for it ({after.Ratio:P0}), so the next " +
                    "turn will shorten it again. The archive holds what was dropped.");
            }

            return;
        }

        _policy.RecordFailure();

        if (_policy.IsTripped)
        {
            // Said once, at the moment it happens: from here on the session simply carries
            // the long history, which is the lesser of the two failures.
            _ui.WriteError(
                "Summarising the conversation keeps failing, so it will not be attempted " +
                "again in this session. Use :clear-history to start the conversation over.");
        }
    }

    /// <summary>Says what the background work did, once it is done rather than while it runs.</summary>
    private void Report(CompactionPlan plan)
    {
        _ui.WriteInfo(
            plan.IsSummary
                ? $"📉 The conversation was summarised in the background: the older turns " +
                  $"are now one summary, {plan.SavedTokens:N0} tokens shorter. " +
                  $"The last {plan.KeptTurns} turn(s) were kept as they were."
                : $"📉 The conversation was shortened in the background: " +
                  $"{plan.DroppedTurns} turn(s) are gone from the middle" +
                  (plan.ArchivePath is null
                      ? "."
                      : $", and the whole conversation is archived at {plan.ArchivePath}."));
    }

    /// <summary>
    /// Estimates the whole request — the conversation plus the new user message — and
    /// measures it against the window of the active model.
    /// </summary>
    private ContextPressure Measure(string? pendingRequest = null)
    {
        var tokens = pendingRequest is null ? 0 : TokenEstimator.EstimateRequest(pendingRequest);

        foreach (var turn in _history.Turns)
        {
            tokens += TokenEstimator.EstimateRequest(turn.UserRequest, turn.AiResponse);
        }

        return _policy.Measure(tokens, _options.Model, EstimateToolTokens());
    }

    /// <summary>
    /// Tokens of the newest turns a compaction should keep, derived from the room the model
    /// has for the conversation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A tail as large as the conversation leaves nothing to summarise, and a tail as large
    /// as the model's budget leaves a replacement that still does not fit. Deriving it from
    /// the budget keeps the two apart: the replacement has to leave the conversation under
    /// the threshold that triggered the compaction, or it only schedules the next one.
    /// </para>
    /// <para>
    /// Null when the model's window is too small to derive anything from — a window smaller
    /// than the reserve leaves no measurable budget at all. The compactor then falls back to
    /// its own default, capped at half the conversation, which still leaves something to
    /// summarise.
    /// </para>
    /// </remarks>
    private int? MeasureTailTokens()
    {
        var headroom = (int)Math.Floor(Measure().AvailableTokens * TailHeadroomRatio);

        return headroom > 0
            ? Math.Min(_options.CompactionPreserveRecentTokens, headroom)
            : null;
    }

    /// <summary>
    /// Estimates the tool definitions the next request will carry, which are the ones the
    /// active mode offers.
    /// </summary>
    /// <remarks>
    /// The mode is read here rather than taken from the session options because the user can
    /// change it mid-session with <c>:mode</c>, and a mode that offers fewer tools really
    /// does leave the conversation more room. Measuring the tools the request will actually
    /// carry is the difference between a threshold that means something and a threshold that
    /// is an approximation of one.
    /// </remarks>
    private int EstimateToolTokens()
    {
        var tools = _registry.Tools;

        if (tools.Count == 0)
        {
            return 0;
        }

        var allowed = AgentModePolicy.AllowedTools(_mode.Mode, tools);

        return TokenEstimator.EstimateTools(tools.Where(tool => allowed.Contains(tool.Name)));
    }
}
