using System.Text;
using Codai.Core.Abstractions;
using Codai.Core.Assets;
using Codai.Core.Chat;
using Codai.Core.Tokens;
using Codai.Core.Tools;

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
    private readonly ToolAccessPolicy _access;
    private readonly IUserInterface _ui;

    /// <summary>Creates the compactor.</summary>
    public HistoryCompactor(
        IChatCompletionService chatCompletion,
        IChatHistory history,
        ITokenTracker tokenTracker,
        ToolAccessPolicy access,
        IUserInterface ui)
    {
        ArgumentNullException.ThrowIfNull(chatCompletion);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(tokenTracker);
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(ui);

        _chatCompletion = chatCompletion;
        _history = history;
        _tokenTracker = tokenTracker;
        _access = access;
        _ui = ui;
    }

    /// <summary>
    /// Summarises the history and replaces it with the summary.
    /// </summary>
    /// <param name="instructions">
    /// What the user asked the summary to focus on, as typed after <c>:compact</c>, or
    /// null to summarise on the summariser's own judgement.
    /// </param>
    /// <param name="cancellationToken">Cancels the summarisation.</param>
    /// <returns>True when the history was replaced.</returns>
    public async Task<bool> CompactAsync(
        string? instructions = null,
        CancellationToken cancellationToken = default)
    {
        // Snapshot: the live collection is cleared once the summary comes back.
        var turns = _history.Turns.ToArray();

        if (turns.Length < MinimumTurnsToCompact)
        {
            _ui.WriteError("Nothing to compact yet.");
            return false;
        }

        // No tools: summarising is not a task the model should start work for, and a
        // tool call it made here would be spent on a conversation that is about to be
        // replaced. Publishing the empty set is what the executor enforces.
        var request = new ChatRequest(
            EmbeddedResources.CompactHistoryPrompt,
            CompactionRequest(turns, instructions),
            AllowedTools: []);

        _access.BeginRequest(request.AllowedTools);

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
    /// Builds the user message: the conversation, plus the user's focus when they gave
    /// one. The focus is phrased as something to weight, not as a replacement, because a
    /// summary that drops what the user did not think to mention is worse than a long one.
    /// </summary>
    private static string CompactionRequest(ChatTurn[] turns, string? instructions)
    {
        var builder = new StringBuilder()
            .Append("## Here is the conversation to compact\n\n")
            .Append(Render(turns));

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
