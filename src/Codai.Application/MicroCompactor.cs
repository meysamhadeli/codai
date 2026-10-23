using Codai.Core.Abstractions;
using Codai.Core.Context;

namespace Codai.Application;

/// <summary>
/// Shortens the conversation before a summary is paid for, by clearing the bulk of old tool
/// output.
/// </summary>
/// <remarks>
/// <para>
/// This is the tier below summarising, and running it first is the difference between a
/// session that compacts once every few minutes and one that compacts when it has to. A
/// summary is a model call that costs as much as an answer; clearing the middle of a build
/// log from twenty turns ago costs nothing at all, and is usually enough on its own.
/// </para>
/// <para>
/// Nothing is lost that the archive does not already hold: the conversation is written to the
/// project before a single turn is cleared, exactly as it is before a summary, so the model
/// can still read what was dropped on the rare occasion it needs it.
/// </para>
/// </remarks>
public sealed class MicroCompactor
{
    /// <summary>
    /// Ratio of the conversation budget that trimming aims for. Below the ratio that starts a
    /// compaction, because a conversation trimmed exactly to the line would be compacted on
    /// the next turn anyway.
    /// </summary>
    public const double TargetRatio = 0.75;

    private readonly IChatHistory _history;
    private readonly ITranscriptStore _transcripts;
    private readonly SessionOptions _options;
    private readonly IUserInterface _ui;

    /// <summary>Creates the compactor.</summary>
    /// <param name="history">Conversation to shorten in place.</param>
    /// <param name="transcripts">Archives the conversation before a turn is cleared.</param>
    /// <param name="options">Supplies the project root.</param>
    /// <param name="ui">Reports what was cleared.</param>
    public MicroCompactor(
        IChatHistory history,
        ITranscriptStore transcripts,
        SessionOptions options,
        IUserInterface ui)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(transcripts);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(ui);

        _history = history;
        _transcripts = transcripts;
        _options = options;
        _ui = ui;
    }

    /// <summary>
    /// Clears the bulk of the oldest turns, if that is enough to take the conversation back
    /// under the threshold that started the compaction.
    /// </summary>
    /// <param name="pressure">What the conversation is currently using.</param>
    /// <param name="cancellationToken">Cancels the archive write.</param>
    /// <returns>Tokens saved, or zero when nothing was worth clearing.</returns>
    public async Task<int> TrimAsync(
        ContextPressure pressure,
        CancellationToken cancellationToken = default)
    {
        var turns = _history.Turns.ToArray();
        var target = (int)Math.Floor(pressure.MessageBudget * TargetRatio);
        var plan = MicroCompaction.Plan(turns, target, pressure.UsedTokens);

        if (plan.IsEmpty)
        {
            return 0;
        }

        // Before the turns change, not after: what is cleared here is not recoverable from
        // the conversation, and the archive is the only place it will exist.
        var archive = await _transcripts
            .ArchiveAsync(_options.ProjectRoot, turns, cancellationToken)
            .ConfigureAwait(false);

        foreach (var edit in plan.Edits)
        {
            _history.Replace(edit.Index, edit.Replacement);
        }

        _ui.WriteInfo(
            $"🧹 Cleared {plan.SavedTokens.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)} " +
            "tokens of older tool output instead of summarising the conversation" +
            (archive is null ? "." : $", and archived what was cleared to {archive}."));

        return plan.SavedTokens;
    }
}
