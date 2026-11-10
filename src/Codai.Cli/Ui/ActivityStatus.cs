using System.Globalization;
using Codai.Core.Tokens;

namespace Codai.Cli.Ui;

/// <summary>One drawn moment of the activity row.</summary>
/// <param name="Label">What the agent is doing, in the present tense.</param>
/// <param name="Elapsed">How long the current wait has been running.</param>
/// <param name="Tokens">Provider-reported output tokens, or an eased estimate when usage is unavailable.</param>
/// <param name="ShowMetrics">Whether the wait has crossed the optional detailed-metrics threshold.</param>
/// <param name="Interruptible">Whether Escape still stops the work being described.</param>
/// <param name="Stalled">Whether no output has arrived for long enough to suggest a stalled request.</param>
public sealed record ActivitySnapshot(string Label, TimeSpan Elapsed, long Tokens, bool ShowMetrics, bool Interruptible, bool Stalled)
{
    /// <summary>Whether output tokens are estimated rather than provider-reported.</summary>
    public bool TokensEstimated { get; init; } = true;
    /// <summary>Provider-reported input tokens for this request.</summary>
    public long InputTokens { get; init; }
    /// <summary>Time spent in provider-reported reasoning phases.</summary>
    public TimeSpan ThoughtElapsed { get; init; }
}

/// <summary>
/// Tracks the live state behind the single activity row shown while the agent works.
/// </summary>
/// <remarks>
/// The row answers three questions without a keystroke: what the agent is doing, how long it has
/// been doing it, and whether the wait can still be interrupted. Streamed text produces an eased
/// character-based estimate; provider-reported usage replaces that estimate without easing.
/// Reasoning signals track time without exposing private reasoning. The terminal may show elapsed
/// time and zero output tokens immediately, independently of the optional detailed-metrics threshold.
/// A request that stops producing output is marked as stalled instead of looking like a frozen prompt.
/// <para>
/// Nothing here touches a console or a render cycle: the row's timing rules are decided from a
/// monotonic clock, which is what makes them testable without a terminal.
/// </para>
/// </remarks>
public sealed class ActivityStatus
{
    /// <summary>Optional threshold for hosts that delay detailed metrics.</summary>
    public static readonly TimeSpan RevealAfter = TimeSpan.FromSeconds(2);

    /// <summary>How long the row may go without progress before it reads as stalled.</summary>
    public static readonly TimeSpan StallAfter = TimeSpan.FromSeconds(10);

    private const int CharactersPerToken = 4;
    private const double TokenEase = 0.35;
    private const double TokenMinimumStep = 3;

    private readonly Func<TimeSpan> _clock;
    private TimeSpan _startedAt;
    private TimeSpan _lastProgressAt;
    private long _characters;
    private double _displayedTokens;
    private long? _reportedTokens;
    private long _inputTokens;
    private TimeSpan? _thoughtStartedAt;
    private TimeSpan _thoughtElapsed;

    /// <summary>Creates the tracker.</summary>
    /// <param name="clock">
    /// Monotonic elapsed-time source. Defaults to a running <see cref="System.Diagnostics.Stopwatch"/>;
    /// tests pass a clock they can advance by hand.
    /// </param>
    public ActivityStatus(Func<TimeSpan>? clock = null) => _clock = clock ?? RunningClock();

    /// <summary>Gets or sets what the agent is doing, in the present tense.</summary>
    public string Label { get; set; } = "Thinking";

    /// <summary>Gets or sets whether the described wait can still be stopped with Escape.</summary>
    public bool Interruptible { get; set; }

    /// <summary>Starts a new wait, clearing elapsed time and the token count.</summary>
    public void Begin(string? label = null)
    {
        var now = _clock();
        _startedAt = now;
        _lastProgressAt = now;
        _characters = 0;
        _displayedTokens = 0;
        _reportedTokens = null;
        _inputTokens = 0;
        _thoughtStartedAt = null;
        _thoughtElapsed = TimeSpan.Zero;
        Label = string.IsNullOrWhiteSpace(label) ? "Thinking" : label;
    }

    /// <summary>Records that the agent is still moving, even when it produced no output of its own.</summary>
    public void Pulse() => _lastProgressAt = _clock();

    /// <summary>Records streamed output so the token count follows what the model actually produced.</summary>
    public void Observe(int characters)
    {
        if (characters <= 0)
        {
            return;
        }

        _characters += characters;
        SetReasoning(false);
        Pulse();
    }

    /// <summary>Records a provider usage report without estimating or easing its counts.</summary>
    public void ReportUsage(TokenUsage usage)
    {
        if (usage.HasValue)
        {
            _reportedTokens = Math.Max(0, usage.OutputTokens);
            _inputTokens = Math.Max(0, usage.InputTokens);
            Pulse();
        }
    }

    /// <summary>Starts or stops timing a provider-reported reasoning phase.</summary>
    public void SetReasoning(bool reasoning)
    {
        if (reasoning)
        {
            _thoughtStartedAt ??= _clock();
        }
        else if (_thoughtStartedAt is { } started)
        {
            _thoughtElapsed += _clock() - started;
            _thoughtStartedAt = null;
        }
    }

    /// <summary>Clears the token count without restarting the wait.</summary>
    /// <remarks>A new response inside the same turn starts from zero output again.</remarks>
    public void ClearOutput()
    {
        _characters = 0;
        _reportedTokens = null;
        _inputTokens = 0;
    }

    /// <summary>Captures the row as it should be drawn now, easing the token count toward its target.</summary>
    public ActivitySnapshot Snapshot()
    {
        var now = _clock();
        var target = _characters / CharactersPerToken;
        if (_displayedTokens < target)
        {
            _displayedTokens = Math.Min(target,
                _displayedTokens + Math.Max(TokenMinimumStep, (target - _displayedTokens) * TokenEase));
        }
        else if (_displayedTokens > target)
        {
            _displayedTokens = target;
        }

        var elapsed = now - _startedAt;
        return new ActivitySnapshot(
            Label,
            elapsed,
            _reportedTokens ?? (long)Math.Round(_displayedTokens),
            elapsed >= RevealAfter,
            Interruptible,
            now - _lastProgressAt >= StallAfter)
        {
            TokensEstimated = _reportedTokens is null,
            InputTokens = _inputTokens,
            ThoughtElapsed = _thoughtElapsed + (_thoughtStartedAt is { } started ? now - started : TimeSpan.Zero),
        };
    }

    /// <summary>Formats a wait as <c>12s</c>, <c>1m 04s</c>, or <c>1h 02m</c>.</summary>
    public static string FormatElapsed(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        return elapsed.TotalSeconds < 60
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)elapsed.TotalSeconds}s")
            : elapsed.TotalMinutes < 60
                ? string.Create(CultureInfo.InvariantCulture, $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds:00}s")
                : string.Create(CultureInfo.InvariantCulture, $"{(int)elapsed.TotalHours}h {elapsed.Minutes:00}m");
    }

    /// <summary>Formats a token count as <c>840</c>, <c>1.2k</c>, or <c>3.4M</c>.</summary>
    public static string FormatTokens(long tokens) => tokens switch
    {
        < 1000 => tokens.ToString(CultureInfo.InvariantCulture),
        < 1_000_000 => string.Create(CultureInfo.InvariantCulture, $"{tokens / 1000d:0.0}k"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{tokens / 1_000_000d:0.0}M"),
    };

    private static Func<TimeSpan> RunningClock()
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        return () => stopwatch.Elapsed;
    }
}
