using Codai.Core.Chat;

namespace Codai.Application;

/// <summary>
/// Summarises the conversation while the user is reading the answer, instead of in front of
/// the next question.
/// </summary>
/// <remarks>
/// <para>
/// Summarising is a model call, and a model call takes as long as a request takes. Waiting
/// for it is the one pause a user reliably notices, and it happens at the worst moment: the
/// turn they just asked for has already been answered, so the work is entirely for the
/// future. Copilot starts it in the background for exactly this reason and this is the same
/// idea, applied to the same threshold.
/// </para>
/// <para>
/// The result is a <see cref="CompactionPlan"/> and nothing more. It is not applied here,
/// because a plan that lands while a request is in flight would rewrite the conversation
/// underneath a request that has already been sent. Whoever takes the plan applies it on a
/// turn boundary, where nothing is being read.
/// </para>
/// <para>
/// One attempt at a time, and the attempt outlives the request that started it: it has a
/// lifetime of its own, cancelled only when the session ends. A summary that were cancelled
/// with the answer it started behind would never finish on any turn.
/// </para>
/// </remarks>
public sealed class BackgroundSummarizer : IDisposable
{
    private readonly HistoryCompactor _compactor;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Lock _gate = new();

    private Task<CompactionPlan?>? _flight;
    private CompactionPlan? _ready;
    private bool _lastAttemptFailed;
    private bool _disposed;

    /// <summary>Creates the summarizer.</summary>
    /// <param name="compactor">Produces the plan.</param>
    public BackgroundSummarizer(HistoryCompactor compactor)
    {
        ArgumentNullException.ThrowIfNull(compactor);
        _compactor = compactor;
    }

    /// <summary>Where the last attempt got to.</summary>
    public BackgroundSummarizationState State
    {
        get
        {
            lock (_gate)
            {
                if (_ready is not null)
                {
                    return BackgroundSummarizationState.Completed;
                }

                if (_flight is not null)
                {
                    return BackgroundSummarizationState.InProgress;
                }

                return _lastAttemptFailed
                    ? BackgroundSummarizationState.Failed
                    : BackgroundSummarizationState.Idle;
            }
        }
    }

    /// <summary>
    /// Starts summarising, unless one is already running or one is waiting to be taken.
    /// </summary>
    /// <param name="instructions">
    /// What the summary should focus on, or null for the summariser's own judgement.
    /// </param>
    /// <param name="preserveRecentTokens">
    /// Tokens of the newest turns to keep word for word, or null to use the compactor's own
    /// default. The caller derives it from the model's budget, so the replacement leaves the
    /// conversation under the threshold that started this.
    /// </param>
    /// <remarks>
    /// Returns as soon as the work has been handed off, so the caller never waits. Starting
    /// twice is a no-op rather than an error: the caller's question is "is anything being
    /// done about this", and the answer being yes is a success, not a conflict.
    /// </remarks>
    public void Start(string? instructions = null, int? preserveRecentTokens = null)
    {
        lock (_gate)
        {
            if (_disposed || _flight is not null || _ready is not null)
            {
                return;
            }

            _lastAttemptFailed = false;

            // The task is stored before it can complete, so the state is never InProgress
            // with nothing to wait for.
            _flight = RunAsync(instructions, preserveRecentTokens);
        }
    }

    /// <summary>Takes the plan that is ready, if there is one.</summary>
    /// <param name="plan">The plan, when there was one to take.</param>
    /// <returns>True when a plan was taken.</returns>
    /// <remarks>Taking resets the state, so the next <see cref="Start"/> begins a new one.</remarks>
    public bool TryTake(out CompactionPlan plan)
    {
        lock (_gate)
        {
            if (_ready is null)
            {
                plan = null!;
                return false;
            }

            plan = _ready;
            _ready = null;
            _lastAttemptFailed = false;

            return true;
        }
    }

    /// <summary>
    /// Completes when the attempt in flight has finished, leaving its result to be taken.
    /// </summary>
    /// <returns>A task that completes when nothing is being written.</returns>
    /// <remarks>
    /// This is the counterpart to <see cref="WaitAsync"/>: it is for callers that want the
    /// work finished without taking responsibility for what it produced. Taking the plan and
    /// applying it are the same moment on purpose, so a caller that only wants to know the
    /// background is quiet should not have to steal the plan to find out.
    /// </remarks>
    public Task WhenIdleAsync()
    {
        lock (_gate)
        {
            return _flight ?? Task.CompletedTask;
        }
    }

    /// <summary>
    /// Waits for the attempt in flight, or for one that has just finished, and returns what
    /// it produced.
    /// </summary>
    /// <param name="cancellationToken">Stops waiting, leaving the attempt to finish on its own.</param>
    /// <returns>The plan, or null when the attempt produced nothing usable.</returns>
    /// <remarks>
    /// The wait does not cancel the attempt: a summary that is nearly done is worth more than
    /// a cancelled one, and the next turn will take it. Only the session ending cancels the
    /// work itself.
    /// </remarks>
    public async Task<CompactionPlan?> WaitAsync(CancellationToken cancellationToken = default)
    {
        Task<CompactionPlan?>? flight;

        lock (_gate)
        {
            if (_ready is not null)
            {
                var ready = _ready;
                _ready = null;
                return ready;
            }

            flight = _flight;
        }

        if (flight is null)
        {
            return null;
        }

        // The task never faults — RunAsync catches everything — so awaiting it is safe.
        return await flight.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Stops the work in flight without waiting for it.
    /// </summary>
    /// <remarks>
    /// Called when the session ends. Nothing is published from here on, and the attempt that
    /// was running is abandoned rather than applied: a summary of a conversation nobody is
    /// going to continue is not worth finishing.
    /// </remarks>
    public void Cancel()
    {
        lock (_gate)
        {
            _ready = null;
        }

        try
        {
            _lifetime.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already torn down.
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _ready = null;
        }

        Cancel();
        _lifetime.Dispose();
    }

    /// <summary>
    /// Runs one attempt and publishes what it produced.
    /// </summary>
    /// <remarks>
    /// Nothing is allowed out of here. A background job is not worth a failure the user has
    /// to see, and a task that faults with nobody awaiting it takes the process down on a
    /// finalizer thread. A failed attempt is recorded as the state instead, which is what
    /// lets the caller decide whether to try a different way.
    /// </remarks>
    private async Task<CompactionPlan?> RunAsync(string? instructions, int? preserveRecentTokens)
    {
        CompactionPlan? plan = null;
        var failed = false;

        try
        {
            plan = await _compactor
                .TryPlanAsync(
                    instructions,
                    foreground: false,
                    _lifetime.Token,
                    preserveRecentTokens: preserveRecentTokens)
                .ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Includes cancellation: the session is ending, which is not a failure worth
            // reporting but is not a plan either.
            failed = true;
        }

        lock (_gate)
        {
            if (!_disposed)
            {
                _ready = plan;
                _lastAttemptFailed = failed || plan is null;
            }

            _flight = null;
        }

        return plan;
    }
}

/// <summary>Where a <see cref="BackgroundSummarizer"/> is up to.</summary>
public enum BackgroundSummarizationState
{
    /// <summary>Nothing is running and nothing is waiting.</summary>
    Idle,

    /// <summary>A summary is being written.</summary>
    InProgress,

    /// <summary>A plan has been produced and is waiting to be taken.</summary>
    Completed,

    /// <summary>The last attempt produced nothing usable.</summary>
    Failed,
}
