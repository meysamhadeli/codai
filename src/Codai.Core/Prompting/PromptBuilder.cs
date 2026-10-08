using System.Globalization;
using System.Text;
using Codai.Core.Abstractions;
using Codai.Core.Chat;
using Codai.Core.Projects;

namespace Codai.Core.Prompting;

/// <summary>
/// Builds the provider request.
/// </summary>
/// <remarks>
/// The layout is a deliberate, byte-for-byte port of the original Go implementation so
/// prompts stay stable across the rewrite. That is what <c>PromptParityTests</c> pins
/// down: <em>do not</em> reflow the literal text, reorder the sections, or "fix" the
/// misspelling in "requsted" — each of those changes the prompt the model sees.
/// </remarks>
public sealed class PromptBuilder : IPromptBuilder
{
    /// <summary>Separator used both between history turns and between file summaries.</summary>
    private const string Separator = "\n---------\n\n";

    private const string HistoryHeader = "## Here is the history of chats\n\n";
    private const string ContextHeader = "## Here is the summary of context of project\n\n";
    private const string TemplateHeader = "## Here is the general template prompt for using AI\n\n";
    private const string RequestedContextHeader =
        "## Here are the requsted full context files for using in your task\n\n";

    private readonly string _fullContextTemplate;

    /// <summary>Creates the builder from the general "how to answer" template.</summary>
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

        var systemPrompt = new StringBuilder()
            .Append(HistoryHeader)
            .AppendJoin(Separator, history.Select(RenderTurn))
            .Append("\n\n______\n\n")
            .Append(ContextHeader)
            .AppendJoin(Separator, context.RawCodes)
            .Append("\n\n______\n")
            .Append(TemplateHeader)
            .Append(_fullContextTemplate)
            .Append("\n\n______\n");

        if (!string.IsNullOrEmpty(requestedContext))
        {
            systemPrompt
                .Append(RequestedContextHeader)
                .Append(requestedContext)
                .Append("______\n");
        }

        return new ChatRequest(systemPrompt.ToString(), $"## Here is user request\n{userInput}");
    }

    /// <summary>
    /// Renders one history entry. The timestamp is formatted with the invariant culture
    /// so the same conversation produces the same prompt on every machine.
    /// </summary>
    private static string RenderTurn(ChatTurn turn) =>
        $"### History time:\n\n{turn.Timestamp.ToString(CultureInfo.InvariantCulture)}\n---------\n\n" +
        $"### Here is user request:\n\n{turn.UserRequest}\n---------\n\n" +
        $"### Here is the the response from using AI:\n\n{turn.AiResponse}";
}
