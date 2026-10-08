using System.Text;
using Codai.Core.Abstractions;
using Codai.Core.Assets;
using Codai.Core.Chat;
using Codai.Core.Tokens;

namespace Codai.Session;

/// <summary>
/// Replaces the conversation so far with a summary of it, so a long session can keep
/// going without carrying every turn into every request.
/// </summary>
/// <remarks>
/// The summary is stored as a single history turn, which means nothing downstream needs
/// to know that compaction happened: the prompt builder renders it like any other turn.
/// </remarks>
public sealed class HistoryCompactor
{
    /// <summary>
    /// Fewer turns than this are not worth a summarisation request — the summary would
    /// cost more than the history it replaces.
    /// </summary>
    public const int MinimumTurnsToCompact = 2;

    /// <summary>Text used as the user request of the turn that holds the summary.</summary>
    public const string SummaryTurnLabel =
        "Summary of the conversation before it was compacted";

    private readonly IChatCompletionService _chatCompletion;
    private readonly IChatHistory _history;
    private readonly ITokenTracker _tokenTracker;
    private readonly IUserInterface _ui;

    /// <summary>Creates the compactor.</summary>
    public HistoryCompactor(
        IChatCompletionService chatCompletion,
        IChatHistory history,
        ITokenTracker tokenTracker,
        IUserInterface ui)
    {
        ArgumentNullException.ThrowIfNull(chatCompletion);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(tokenTracker);
        ArgumentNullException.ThrowIfNull(ui);

        _chatCompletion = chatCompletion;
        _history = history;
        _tokenTracker = tokenTracker;
        _ui = ui;
    }

    /// <summary>
    /// Summarises the history and replaces it with the summary.
    /// </summary>
    /// <returns>True when the history was replaced.</returns>
    public async Task<bool> CompactAsync(CancellationToken cancellationToken = default)
    {
        // Snapshot: the live collection is cleared once the summary comes back.
        var turns = _history.Turns.ToArray();

        if (turns.Length < MinimumTurnsToCompact)
        {
            _ui.WriteError("Nothing to compact yet.");
            return false;
        }

        var request = new ChatRequest(
            EmbeddedResources.CompactHistoryPrompt,
            $"## Here is the conversation to compact\n\n{Render(turns)}");

        var summary = new StringBuilder();
        var usage = TokenUsage.Empty;

        await _ui
            .RunWithProgressAsync(
                "Compacting history...",
                async ct =>
                {
                    await foreach (var update in _chatCompletion
                        .StreamAsync(request, ct)
                        .WithCancellation(ct)
                        .ConfigureAwait(false))
                    {
                        if (!string.IsNullOrEmpty(update.ContentDelta))
                        {
                            summary.Append(update.ContentDelta);
                        }

                        if (update.IsCompleted && update.Usage.HasValue)
                        {
                            usage = update.Usage;
                        }
                    }

                    return true;
                },
                cancellationToken)
            .ConfigureAwait(false);

        _tokenTracker.Record(usage);

        if (summary.Length == 0)
        {
            _ui.WriteError("The model returned an empty summary; the history was left alone.");
            return false;
        }

        _history.Clear();
        _history.Add(new ChatTurn(DateTimeOffset.UtcNow, SummaryTurnLabel, summary.ToString()));

        _ui.WriteSuccess($"✔️ Compacted {turns.Length} turn(s) into one summary.");

        return true;
    }

    /// <summary>
    /// Renders the history for the summariser. Deliberately terser than the prompt
    /// format: the point of compaction is to send fewer characters than the turns it
    /// replaces.
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

            builder
                .Append("User: ").AppendLine(turns[i].UserRequest)
                .Append("Assistant: ").AppendLine(turns[i].AiResponse);
        }

        return builder.ToString();
    }
}
