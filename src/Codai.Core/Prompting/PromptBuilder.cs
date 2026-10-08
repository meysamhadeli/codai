using System.Text;
using Codai.Core.Abstractions;
using Codai.Core.Models;

namespace Codai.Core.Prompting;

/// <summary>
/// Builds the provider request. The layout is kept byte-compatible with the
/// original Go implementation so prompts stay stable across the rewrite.
/// </summary>
public sealed class PromptBuilder : IPromptBuilder
{
    private const string HistorySeparator = "\n---------\n\n";

    private readonly string _fullContextTemplate;

    public PromptBuilder(string fullContextTemplate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullContextTemplate);
        _fullContextTemplate = fullContextTemplate;
    }

    /// <inheritdoc />
    public ChatRequest Build(
        ProjectContext context,
        IReadOnlyList<ChatTurn> history,
        string userInput,
        string? requestedContext = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(userInput);

        var code = string.Join("\n---------\n\n", context.RawCodes);

        var prompt = new StringBuilder();
        prompt.Append("## Here is the summary of context of project\n\n").Append(code).Append("\n\n______\n");
        prompt.Append("## Here is the general template prompt for using AI\n\n").Append(_fullContextTemplate).Append("\n\n______\n");

        if (!string.IsNullOrEmpty(requestedContext))
        {
            prompt.Append("## Here are the requsted full context files for using in your task\n\n")
                .Append(requestedContext)
                .Append("______\n");
        }

        var historyPrompt = new StringBuilder();
        historyPrompt.Append("## Here is the history of chats\n\n")
            .Append(string.Join(HistorySeparator, history.Select(RenderTurn)));

        var systemPrompt = new StringBuilder()
            .Append(historyPrompt)
            .Append("\n\n______\n\n")
            .Append(prompt)
            .ToString();

        var userMessage = $"## Here is user request\n{userInput}";

        return new ChatRequest(systemPrompt, userMessage);
    }

    private static string RenderTurn(ChatTurn turn) =>
        $"### History time:\n\n{turn.Timestamp}\n---------\n\n" +
        $"### Here is user request:\n\n{turn.UserRequest}\n---------\n\n" +
        $"### Here is the the response from using AI:\n\n{turn.AiResponse}";
}
