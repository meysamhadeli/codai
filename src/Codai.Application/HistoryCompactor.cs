using System.Globalization;
using System.Text;
using Codai.Core.Abstractions;
using Codai.Core.Agents;
using Codai.Core.Assets;
using Codai.Core.Chat;
using Codai.Core.Context;
using Codai.Core.Tokens;
using Codai.Core.Tools;

namespace Codai.Application;

/// <summary>
/// Produces a shorter version of the conversation, and applies it.
/// </summary>
/// <remarks>
/// <para>
/// The two halves are deliberately separate, and nothing else here is as important.
/// Producing a compaction means asking a model to read the conversation and write a summary
/// of it, which takes as long as a request takes. Doing that while the user waits turns the
/// one moment they might not have noticed into a pause they certainly will, so producing can
/// happen in the background — <see cref="BackgroundSummarizer"/> does exactly that — and
/// applying happens on a turn boundary, where it is safe.
/// </para>
/// <para>
/// Four decisions shape what a plan contains.
/// </para>
/// <para>
/// <b>A tail measured in tokens is kept verbatim.</b> A summary is a paraphrase, and a
/// paraphrase of the turn in progress is worse than the turn itself. Measuring the tail in
/// tokens rather than counting turns is what keeps the right turns: one pasted file costs
/// what twenty short exchanges cost, and both should buy the same amount of recent context.
/// The tail is capped at half the conversation too, so a conversation whose whole length is
/// small is never summarised into almost nothing.
/// </para>
/// <para>
/// <b>Only what has grown since the last summary is summarised.</b> A summary is itself a
/// turn, so a conversation that is compacted twice would otherwise summarise a summary of a
/// summary, and each pass would lose a little more of the original intent. The previous
/// summary is carried into the next one instead of being folded again from scratch.
/// </para>
/// <para>
/// <b>The turns being replaced are archived first.</b> Compaction has to drop detail; it
/// does not have to destroy it. The archive is a file in the project, so a model that needs
/// a path or an error message the summary left out can read it back.
/// </para>
/// <para>
/// <b>A replacement that is not smaller is refused.</b> Summarisation is a model call and
/// model calls can go wrong — a model that ignores the instruction and returns the
/// conversation, or reasons its way into a longer document than it was given. Measuring the
/// result is the only defence, and the cost of being wrong is one turn that stayed long
/// instead of one turn that lost its history. When a summary is refused, or cannot be
/// produced at all, <see cref="ConversationDigest"/> shortens the conversation without a
/// model, which is the lesser of the two failures.
/// </para>
/// </remarks>
public sealed class HistoryCompactor
{
    /// <summary>
    /// Turns left untouched at the end of the conversation, whatever the token budget says.
    /// The turn being worked on, and the one before it, are what the next request is about.
    /// </summary>
    public const int KeptTurns = 2;

    /// <summary>
    /// Tokens of the newest turns kept word for word when no budget is supplied. Chosen to
    /// match the tail the agent CLIs reserve for the same reason: enough that the request in
    /// progress and the exchanges leading up to it are read exactly as they were written.
    /// <see cref="SessionOptions.CompactionPreserveRecentTokens"/> is what a caller can set,
    /// and the automatic path narrows it further from the model's own budget.
    /// </summary>
    public const int PreserveRecentTokens = 20_000;

    /// <summary>
    /// Fraction of the turns being replaced that a deterministic compaction aims to shrink
    /// them to. Half is deliberate: compacting to the edge of the window only schedules the
    /// next compaction.
    /// </summary>
    public const double TargetRatio = 0.5;

    /// <summary>
    /// Fewer turns than this are not worth a summarisation request — the summary would cost
    /// more than the history it replaces. It leaves the kept tail and at least one turn to
    /// summarise.
    /// </summary>
    public const int MinimumTurnsToCompact = KeptTurns + 1;

    /// <summary>
    /// Longest summary worth keeping, in estimated tokens. A summary is the part of the
    /// context that is carried forward forever, so it is capped the way the prompt asks for
    /// it to be capped; a summary that ignores the cap is reported, and kept only if it is
    /// still smaller than the turns it replaced.
    /// </summary>
    public const int MaxSummaryTokens = 7_000;

    /// <summary>
    /// Characters of a single turn fed to the summariser before the rest is dropped. One
    /// pasted file in an old turn can be larger than the whole summary is allowed to be, and
    /// the summariser does not need it to say what the turn decided.
    /// </summary>
    public const int MaxTurnCharacters = 8_000;

    /// <summary>
    /// Estimated tokens of summariser input before the turns are packed down. A compaction
    /// must not fail because the thing it is compacting is too big to send, so the input is
    /// shortened the same way a conversation is when no summary can be produced.
    /// </summary>
    public const int MaxSummaryInputTokens = 90_000;

    /// <summary>
    /// Times a summarisation is retried with fewer turns after the provider refuses the
    /// request as too long. A conversation that overflowed is the one most likely to be too
    /// long to summarise, so a single failure would otherwise mean never compacting it.
    /// </summary>
    public const int MaxOverflowRetries = 3;

    /// <summary>Text used as the user request of the turn that holds the summary.</summary>
    public const string SummaryTurnLabel =
        "Summary of the conversation before it was compacted";

    private const string TruncationMarker = "\n… (truncated)";

    private readonly IChatCompletionService _chatCompletion;
    private readonly IChatHistory _history;
    private readonly ITokenTracker _tokenTracker;
    private readonly ToolAccessPolicy _access;
    private readonly IUserInterface _ui;
    private readonly ITranscriptStore _transcripts;
    private readonly SessionOptions _options;
    private readonly IAgentHooks _hooks;
    private readonly SessionFootprint? _footprint;
    private readonly ContextRestorer? _restorer;

    public bool HasHooks => !ReferenceEquals(_hooks, NullAgentHooks.Instance);

    /// <summary>Creates the compactor.</summary>
    /// <param name="chatCompletion">Client used to ask the model for the summary.</param>
    /// <param name="history">Conversation that a plan replaces when it is applied.</param>
    /// <param name="tokenTracker">Records what the summarisation cost.</param>
    /// <param name="access">Restricts a foreground summarisation request to no tools.</param>
    /// <param name="ui">Reports progress and failures.</param>
    /// <param name="transcripts">Writes the conversation being replaced to the project.</param>
    /// <param name="options">Supplies the project root, the strategy and the tail size.</param>
    /// <param name="hooks">Optional policy around history mutation.</param>
    /// <param name="footprint">
    /// Files and skills the session touched, restored into the summary so the model does not
    /// have to read them again. Optional: without it a summary is only a summary.
    /// </param>
    /// <param name="restorer">Reads the files back for the restore. Optional.</param>
    public HistoryCompactor(
        IChatCompletionService chatCompletion,
        IChatHistory history,
        ITokenTracker tokenTracker,
        ToolAccessPolicy access,
        IUserInterface ui,
        ITranscriptStore transcripts,
        SessionOptions options,
        IAgentHooks? hooks = null,
        SessionFootprint? footprint = null,
        ContextRestorer? restorer = null)
    {
        ArgumentNullException.ThrowIfNull(chatCompletion);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(tokenTracker);
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(transcripts);
        ArgumentNullException.ThrowIfNull(options);

        _chatCompletion = chatCompletion;
        _history = history;
        _tokenTracker = tokenTracker;
        _access = access;
        _ui = ui;
        _transcripts = transcripts;
        _options = options;
        _hooks = hooks ?? NullAgentHooks.Instance;
        _footprint = footprint;
        _restorer = restorer;
    }

    /// <summary>
    /// Summarises the older turns and replaces them with the summary, in one step.
    /// </summary>
    /// <remarks>
    /// The path <c>:compact</c> takes, where the user asked for it and is waiting for it, so
    /// progress is shown and failures are reported. A background compaction goes through
    /// <see cref="TryPlanAsync"/> and <see cref="Apply"/> instead, because it has to prove it
    /// did not disturb the turn that was running.
    /// </remarks>
    /// <param name="instructions">
    /// What the user asked the summary to focus on, as typed after <c>:compact</c>, or null
    /// to summarise on the summariser's own judgement.
    /// </param>
    /// <param name="cancellationToken">Cancels the summarisation.</param>
    /// <returns>True when the history was replaced.</returns>
    public async Task<bool> CompactAsync(
        string? instructions = null,
        CancellationToken cancellationToken = default)
    {
        var plan = await TryPlanAsync(instructions, foreground: true, cancellationToken)
            .ConfigureAwait(false);

        if (plan is null)
        {
            return false;
        }

        try
        {
            await ApplyAsync(plan, cancellationToken).ConfigureAwait(false);
        }
        catch (AgentHookRejectedException ex)
        {
            _ui.WriteError(ex.Message);
            return false;
        }

        if (plan.IsSummary)
        {
            _ui.WriteSuccess(
                $"✔️ Compacted the oldest {plan.ReplacedTurns} turn(s) into one summary, " +
                $"keeping the last {plan.KeptTurns}.");
        }
        else
        {
            _ui.WriteInfo(
                $"📉 The conversation was shortened instead: {plan.DroppedTurns} turn(s) are " +
                "gone from the middle" +
                (plan.ArchivePath is null
                    ? "."
                    : $", and the whole conversation is archived at {plan.ArchivePath}."));
        }

        return true;
    }

    /// <summary>
    /// Works out how the conversation should be shortened, without changing it.
    /// </summary>
    /// <param name="instructions">
    /// What the user asked the summary to focus on, or null for the summariser's own
    /// judgement.
    /// </param>
    /// <param name="foreground">
    /// True when the user is waiting for this and it is the only thing happening. False when
    /// it runs behind a turn the user is still reading, which changes three things: no
    /// progress is drawn, no failure is reported, and the shared tool-access policy is left
    /// alone — publishing "this request may call no tools" from a background task would
    /// withdraw the tools from the request the user is actually waiting for.
    /// </param>
    /// <param name="cancellationToken">Cancels the summarisation.</param>
    /// <param name="strategy">
    /// Overrides the configured strategy for this one plan. A request the provider refused
    /// for being too long passes <see cref="CompactionStrategy.Basic"/>, because the
    /// deterministic strategy cannot fail the way a model call can.
    /// </param>
    /// <param name="preserveRecentTokens">
    /// Tokens of the newest turns to keep word for word, or null to use the session's own
    /// value. The automatic path narrows it from the model's budget, because a tail larger
    /// than the room the conversation has would leave nothing a compaction could safely do.
    /// </param>
    /// <returns>
    /// The plan to apply, or null when the conversation cannot usefully be shortened and
    /// should be left exactly as it is.
    /// </returns>
    public async Task<CompactionPlan?> TryPlanAsync(
        string? instructions,
        bool foreground,
        CancellationToken cancellationToken,
        CompactionStrategy? strategy = null,
        int? preserveRecentTokens = null)
    {
        // Snapshot: the plan describes these turns and no others, so turns added while the
        // summary is being written are not part of what it replaces.
        var turns = _history.Turns.ToArray();

        if (turns.Length < MinimumTurnsToCompact)
        {
            Report(foreground, failure: false, "Nothing to compact yet.");
            return null;
        }

        var tail = ResolvePreserveTokens(
            turns,
            preserveRecentTokens ?? _options.CompactionPreserveRecentTokens);
        var cut = FindCutIndex(turns, tail);

        if (cut <= 0)
        {
            // The whole conversation fits inside the tail, so there is nothing older than
            // it to replace. Compacting would only rephrase what the next request reads
            // verbatim anyway.
            Report(foreground, failure: false, "Nothing to compact yet.");
            return null;
        }

        var replaced = turns[..cut];
        var kept = turns[cut..];
        var replacedTokens = Estimate(replaced);
        var totalTokens = Estimate(turns);

        var archive = await _transcripts
            .ArchiveAsync(_options.ProjectRoot, turns, cancellationToken)
            .ConfigureAwait(false);

        if ((strategy ?? _options.CompactionStrategy) == CompactionStrategy.Basic)
        {
            return BuildDigestPlan(turns, replacedTokens, totalTokens, archive, foreground);
        }

        // Only fold what has grown since the last summary. The summary a previous
        // compaction wrote is the front of the region being replaced, so it is carried into
        // this one instead of being summarised again.
        var summaryIndex = FindLastSummaryIndex(replaced);
        var foldStart = summaryIndex >= 0 ? summaryIndex + 1 : 0;

        if (foldStart >= cut)
        {
            Report(foreground, failure: false, "Nothing to compact yet.");
            return null;
        }

        var previousSummary = summaryIndex >= 0 ? replaced[summaryIndex].AiResponse : null;
        var toSummarise = replaced[foldStart..];

        var summary = await SummarizeAsync(
                previousSummary, toSummarise, instructions, archive, foreground, cancellationToken)
            .ConfigureAwait(false);

        if (summary is not null)
        {
            // The summary can say what was decided; it cannot carry the file. The files the
            // session was actually working in go in with it, because the first thing a model
            // does after losing a file is read it again — paying back the saving in the very
            // turn the user was waiting for.
            var restore = await BuildRestoreAsync(cancellationToken).ConfigureAwait(false);
            var candidate = restore is null ? summary : $"{summary}\n\n{restore}";
            var summaryTokens = TokenEstimator.Estimate(candidate);
            var replacementTokens = summaryTokens + Estimate(kept);

            if (replacementTokens >= totalTokens && restore is not null)
            {
                // The restore is what pushed the replacement over the turns it replaces, so
                // it is dropped rather than the summary: a summary that is not shorter is
                // no compaction at all.
                candidate = summary;
                summaryTokens = TokenEstimator.Estimate(candidate);
                replacementTokens = summaryTokens + Estimate(kept);
            }

            // Equal to "the summary is smaller than the turns it replaces", because the
            // kept tail is on both sides of the comparison: the tail is not being replaced,
            // so it does not have to be beaten by the summary on its own.
            if (replacementTokens < totalTokens)
            {
                if (summaryTokens > MaxSummaryTokens)
                {
                    // Not a failure: the conversation is still shorter than it was. Said out
                    // loud because a summary is what every later request pays for.
                    Report(
                        foreground,
                        failure: false,
                        $"📉 The summary is about {summaryTokens.ToString("N0", CultureInfo.InvariantCulture)} " +
                        $"tokens, above the {MaxSummaryTokens.ToString("N0", CultureInfo.InvariantCulture)} " +
                        "it was asked to stay under.");
                }

                var summaryTurn = new ChatTurn(
                    DateTimeOffset.UtcNow,
                    SummaryTurnLabel,
                    candidate,
                    ChatTurnKind.CompactionSummary);

                // ConsumedTurns is the whole conversation this plan saw, not just the turns
                // that were folded: the replacement below already carries the kept tail, so a
                // caller that appends everything past ConsumedTurns must not be handed the
                // tail twice.
                return new CompactionPlan(
                    turns.Length,
                    [summaryTurn, .. kept],
                    CompactionMethod.Summary,
                    replacedTokens,
                    replacementTokens,
                    archive)
                {
                    KeptTurns = kept.Length,
                };
            }

            Report(
                foreground,
                failure: true,
                $"The summary was about {summaryTokens:N0} tokens against the " +
                $"{replacedTokens:N0} it would have replaced, so it was not used.");
        }
        else
        {
            Report(foreground, failure: true, "The model returned an empty summary.");
        }

        // The model could not do it, so the conversation is shortened without one. Still
        // better than leaving a conversation that no longer fits: the archive has everything
        // that was dropped, and a session that cannot answer at all is the only outcome that
        // cannot be recovered from.
        return BuildDigestPlan(turns, replacedTokens, totalTokens, archive, foreground);
    }

    /// <summary>
    /// Replaces the turns <paramref name="plan"/> consumed with what it decided should stand
    /// in for them.
    /// </summary>
    /// <remarks>
    /// Only called between turns. The replacement is anchored at the front of the
    /// conversation rather than swapped for the whole of it, so a turn added after the plan
    /// was made — the answer the user was reading while the summary was written — is still
    /// there afterwards, in the right place.
    /// </remarks>
    /// <param name="plan">The plan to apply.</param>
    public void Apply(CompactionPlan plan)
    {
        if (HasHooks)
        {
            throw new InvalidOperationException("Use ApplyAsync when compaction hooks are configured.");
        }

        ApplyCore(plan);
    }

    public async Task ApplyAsync(CompactionPlan plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        await CheckHookAsync(AgentHookEvent.PreCompact, cancellationToken).ConfigureAwait(false);
        ApplyCore(plan);
        await CheckHookAsync(AgentHookEvent.PostCompact, cancellationToken).ConfigureAwait(false);
    }

    private async Task CheckHookAsync(AgentHookEvent stage, CancellationToken cancellationToken)
    {
        AgentHookDecision decision;
        try
        {
            decision = await _hooks.InvokeAsync(new AgentHookContext(stage, _options.ProjectRoot),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            decision = AgentHookDecision.Block($"Hook handler failed: {ex.Message}");
        }

        if (!decision.Allowed)
        {
            var applied = stage == AgentHookEvent.PostCompact ? " History already compacted; no rollback performed." : string.Empty;
            throw new AgentHookRejectedException((decision.Reason ?? $"Hook blocked {stage}.") + applied);
        }
    }

    private void ApplyCore(CompactionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        // Snapshotted, because <see cref="IChatHistory.Turns"/> is the live list: clearing
        // the history empties it, so a tail read back out of it afterwards would be gone —
        // and reading it while adding to it would never end.
        var turns = _history.Turns.ToArray();
        var consumed = Math.Clamp(plan.ConsumedTurns, 0, turns.Length);

        _history.Clear();

        foreach (var turn in plan.Replacement)
        {
            _history.Add(turn);
        }

        for (var index = consumed; index < turns.Length; index++)
        {
            _history.Add(turns[index]);
        }
    }

    /// <summary>
    /// How many of the oldest turns a compaction should replace, leaving the newest ones
    /// alone.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Walks back from the newest turn until the tail is worth <paramref name="preserveRecentTokens"/>,
    /// then snaps to never leave fewer than <see cref="KeptTurns"/> turns. A tail too large
    /// to leave anything to summarise comes back as zero, which a caller reads as "there is
    /// nothing older than the tail to replace".
    /// </para>
    /// <para>
    /// A turn is the unit here, which is what makes the cut safe: in this history a turn is
    /// a request and its answer, and the tool calls a model made happen inside the provider.
    /// There is no half-run pair to orphan the way there is in a message-level transcript.
    /// </para>
    /// </remarks>
    internal static int FindCutIndex(IReadOnlyList<ChatTurn> turns, int preserveRecentTokens)
    {
        if (preserveRecentTokens <= 0)
        {
            return 0;
        }

        var cut = turns.Count;
        var total = 0;

        for (var index = turns.Count - 1; index >= 0; index--)
        {
            total += Cost(turns[index]);
            cut = index;

            if (total >= preserveRecentTokens)
            {
                break;
            }
        }

        return Math.Min(cut, Math.Max(0, turns.Count - KeptTurns));
    }

    /// <summary>
    /// Narrows the requested tail so a compaction always has something to replace.
    /// </summary>
    /// <remarks>
    /// A tail as large as the conversation leaves no older turns to fold, and the session
    /// would then compact nothing however far over its window it grew. Half is the ceiling:
    /// a conversation is never reduced to less than its newer half, and the automatic path
    /// asks for a far smaller tail than that from the model's own budget.
    /// </remarks>
    internal static int ResolvePreserveTokens(IReadOnlyList<ChatTurn> turns, int requested)
    {
        var half = Estimate(turns) / 2;

        if (requested <= 0)
        {
            return half;
        }

        return Math.Min(requested, half);
    }

    /// <summary>Index of the newest summary turn in <paramref name="turns"/>, or -1.</summary>
    internal static int FindLastSummaryIndex(IReadOnlyList<ChatTurn> turns)
    {
        for (var index = turns.Count - 1; index >= 0; index--)
        {
            if (turns[index].IsCompactionSummary)
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// Shortens the conversation without a model, and refuses if that would not shorten it.
    /// </summary>
    /// <param name="turns">The conversation as it is.</param>
    /// <param name="replacedTokens">Estimated tokens of the turns being replaced.</param>
    /// <param name="totalTokens">Estimated tokens of the whole conversation.</param>
    /// <param name="archive">Archive to name in the notice, or null when it was not written.</param>
    /// <param name="foreground">Whether the user is waiting and failures should be reported.</param>
    private CompactionPlan? BuildDigestPlan(
        ChatTurn[] turns,
        int replacedTokens,
        int totalTokens,
        string? archive,
        bool foreground)
    {
        var target = (int)Math.Round(
            replacedTokens * Math.Clamp(_options.CompactionTargetRatio, 0.05, 0.95),
            MidpointRounding.AwayFromZero);

        var packed = ConversationDigest.Pack(turns, target, archive);
        var packedTokens = Estimate(packed);

        if (packedTokens >= totalTokens)
        {
            Report(
                foreground,
                failure: true,
                "The history was left alone; use :clear-history to start the conversation over.");
            return null;
        }

        return new CompactionPlan(
            turns.Length,
            packed,
            CompactionMethod.Digest,
            totalTokens,
            packedTokens,
            archive)
        {
            KeptTurns = 0,
        };
    }

    /// <summary>
    /// Renders the files the session touched, for the summary turn to carry, or null when
    /// there is nothing to restore.
    /// </summary>
    private async Task<string?> BuildRestoreAsync(CancellationToken cancellationToken)
    {
        if (_restorer is null || _footprint is null)
        {
            return null;
        }

        var files = _footprint.Files;

        if (files.Count == 0)
        {
            return null;
        }

        return await _restorer
            .BuildAsync(_options.ProjectRoot, files, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Asks the model for a summary of <paramref name="turns"/>, or returns null when it
    /// produced something unusable.
    /// </summary>
    /// <param name="previousSummary">
    /// The summary an earlier compaction wrote, or null when this is the first one. Passing
    /// it forward is what stops a conversation from being summarised more than once: the
    /// model rewrites one summary rather than learning the whole history again.
    /// </param>
    /// <param name="turns">The turns that have grown since that summary, oldest first.</param>
    /// <param name="instructions">What the summary should weight, or null.</param>
    /// <param name="archive">Archive to name in the summary, or null.</param>
    /// <param name="foreground">Whether the user is waiting for this.</param>
    /// <param name="cancellationToken">Cancels the summarisation.</param>
    private async Task<string?> SummarizeAsync(
        string? previousSummary,
        ChatTurn[] turns,
        string? instructions,
        string? archive,
        bool foreground,
        CancellationToken cancellationToken)
    {
        var response = new StringBuilder();
        var usage = TokenUsage.Empty;

        async Task ReadAsync(CancellationToken ct)
        {
            // The conversation the summary was asked to read can itself be too long to send —
            // a conversation that overflowed is exactly the one most likely to. Dropping the
            // oldest half and asking again is the only way to get a summary at all, and the
            // archive already holds every turn this drops.
            var remaining = turns;
            var omitted = 0;

            for (var attempt = 0; ; attempt++)
            {
                var request = new ChatRequest(
                    EmbeddedResources.CompactHistoryPrompt,
                    CompactionRequest(previousSummary, remaining, instructions, archive, omitted),
                    AllowedTools: []);

                try
                {
                    await foreach (var update in _chatCompletion
                        .StreamAsync(request, ct)
                        .WithCancellation(ct)
                        .ConfigureAwait(false))
                    {
                        if (!string.IsNullOrEmpty(update.ContentDelta))
                        {
                            response.Append(update.ContentDelta);
                        }

                        if (update.IsCompleted && update.Usage.HasValue)
                        {
                            usage = update.Usage;
                        }
                    }

                    return;
                }
                catch (ChatProviderException ex)
                    when (ex.IsContextOverflow && attempt < MaxOverflowRetries && remaining.Length > 1)
                {
                    var dropped = Math.Max(1, remaining.Length / 2);
                    remaining = remaining[dropped..];
                    omitted += dropped;
                    response.Clear();
                    usage = TokenUsage.Empty;
                }
            }
        }

        if (foreground)
        {
            _access.BeginRequest([]);
        }

        if (foreground)
        {
            await _ui
                .RunWithProgressAsync("Compacting history...", async ct => { await ReadAsync(ct); return true; }, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            // Read without the progress wrapper: a background summary the user can see is
            // not a background summary.
            await ReadAsync(cancellationToken).ConfigureAwait(false);
        }

        _tokenTracker.Record(usage);

        return SummaryMarkup.Extract(response.ToString());
    }

    /// <summary>Says something to the user, but only when the user is listening.</summary>
    /// <param name="foreground">True when the user asked for this and is waiting for it.</param>
    /// <param name="failure">
    /// True when the conversation could not be shortened, as opposed to not needing to be.
    /// The two are not the same message: one is a problem the user may have to act on, the
    /// other is the summariser telling them what it did.
    /// </param>
    /// <param name="message">What to say.</param>
    private void Report(bool foreground, bool failure, string message)
    {
        if (!foreground)
        {
            // A background summary that failed is not news: the conversation is left exactly
            // as it was, the next request carries it, and the count of failures is what
            // decides whether to keep trying. Interrupting an answer with it would cost more
            // than it tells anyone.
            return;
        }

        if (failure)
        {
            _ui.WriteError(message);
            return;
        }

        _ui.WriteInfo(message);
    }

    /// <summary>
    /// Builds the user message: the summary it has to continue from, the conversation, the
    /// length it has to stay under, the archive to name, and the user's focus when they gave
    /// one.
    /// </summary>
    /// <remarks>
    /// The focus is phrased as something to weight, not as a replacement, because a summary
    /// that drops what the user did not think to mention is worse than a long one. The
    /// previous summary is phrased the same way: it is context to continue, not a document
    /// to copy.
    /// </remarks>
    /// <param name="previousSummary">The summary an earlier compaction wrote, or null.</param>
    /// <param name="turns">The turns to fold into it, oldest first.</param>
    /// <param name="instructions">What the summary should weight, or null.</param>
    /// <param name="archive">Archive to name in the summary, or null.</param>
    /// <param name="omittedTurns">
    /// Turns dropped from the front because reading them was too long, which the summary has
    /// to know about: a summary that silently loses the start of the conversation reads as a
    /// conversation that started in the middle.
    /// </param>
    private static string CompactionRequest(
        string? previousSummary,
        ChatTurn[] turns,
        string? instructions,
        string? archive,
        int omittedTurns = 0)
    {
        var builder = new StringBuilder();

        if (omittedTurns > 0)
        {
            builder
                .Append("[earlier conversation truncated for compaction retry]\n\n")
                .Append(omittedTurns.ToString(CultureInfo.InvariantCulture))
                .Append(" turn(s) at the start of this conversation were left out because ")
                .Append("they were too long to send again. Say so where the summary records ")
                .Append("what came before, and rely on the archive for their detail.\n\n");
        }

        if (!string.IsNullOrWhiteSpace(previousSummary))
        {
            builder
                .Append("## The summary that already exists\n\n")
                .Append("An earlier compaction wrote the summary below for the turns that came ")
                .Append("before the ones further down. Continue it: fold the conversation ")
                .Append("that follows into this summary rather than starting again, and keep ")
                .Append("every decision and open thread it already records.\n\n")
                .Append(previousSummary.Trim())
                .Append("\n\n");
        }

        // The turns are packed down first when there are more of them than a summariser can
        // be sent. A compaction that failed because the thing it was compacting was too big
        // to send would leave the conversation over its budget for no reason.
        var packable = ConversationDigest.Pack(
            turns,
            Math.Min(Estimate(turns), MaxSummaryInputTokens),
            archive);

        builder
            .Append("## Here is the conversation to compact\n\n")
            .Append(Render([.. packable]))
            .Append("\n## How long the summary may be\n\n")
            .Append("Stay under ")
            .Append(MaxSummaryTokens.ToString("N0", CultureInfo.InvariantCulture))
            .Append(" tokens. Shorter is better as long as nothing is left out: this ")
            .Append("summary is carried into every following request of the session.\n");

        if (!string.IsNullOrWhiteSpace(archive))
        {
            builder
                .Append("\n## Where the full conversation is archived\n\n")
                .Append("The whole conversation, including the turns above in full and the ")
                .Append("recent turns that are not being summarised, was written to ")
                .Append('`').Append(archive).Append("` relative to the project root. ")
                .Append("Name that path in the \"Full Conversation Archive\" section of the ")
                .Append("summary.\n");
        }

        if (!string.IsNullOrWhiteSpace(instructions))
        {
            builder
                .Append("\n## The user asked for the summary to focus on\n\n")
                .AppendLine(instructions.Trim())
                .Append("\nWeight the summary towards that, without dropping anything that would then be lost.");
        }

        return builder.ToString();
    }

    /// <summary>
    /// Renders the turns for the summariser. Deliberately terser than the prompt format: the
    /// point of compaction is to send fewer characters than the turns it replaces.
    /// </summary>
    private static string Render(ChatTurn[] turns)
    {
        var builder = new StringBuilder();

        for (var i = 0; i < turns.Length; i++)
        {
            if (i > 0)
            {
                builder.AppendLine();
            }

            if (turns[i].IsCompactionSummary)
            {
                builder
                    .Append("Summary so far: ").AppendLine(Truncate(turns[i].AiResponse));
                continue;
            }

            builder
                .Append("User: ").AppendLine(Truncate(turns[i].UserRequest))
                .Append("Assistant: ").AppendLine(Truncate(turns[i].AiResponse));
        }

        return builder.ToString();
    }

    /// <summary>Cuts a turn down to <see cref="MaxTurnCharacters"/> and says that it did.</summary>
    private static string Truncate(string text) =>
        text.Length <= MaxTurnCharacters
            ? text
            : string.Concat(text.AsSpan(0, MaxTurnCharacters), TruncationMarker);

    /// <summary>Estimated tokens of a set of turns as they will be replaced.</summary>
    internal static int Estimate(IReadOnlyList<ChatTurn> turns)
    {
        var total = 0;

        foreach (var turn in turns)
        {
            total += Cost(turn);
        }

        return total;
    }

    /// <summary>Estimated tokens of one turn, framed the way the request that sends it will be.</summary>
    private static int Cost(ChatTurn turn) =>
        TokenEstimator.EstimateRequest(turn.UserRequest, turn.AiResponse);
}
