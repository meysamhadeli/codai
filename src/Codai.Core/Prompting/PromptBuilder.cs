using System.Globalization;
using System.Text;
using Codai.Core.Abstractions;
using Codai.Core.Agents;
using Codai.Core.Chat;
using Codai.Core.Customization;
using Codai.Core.Projects;
using Codai.Core.Tools;

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
    private const string ToolsHeader = "## Here are the tools you can call\n\n";
    private const string InstructionsHeader =
        "## Here are the project instructions you must follow\n\n";
    private const string SkillsHeader = "## Here are the skills available in this project\n\n";
    private const string AttachedContextHeader =
        "## Here are the files the user referenced in this request\n\n";
    private readonly string _fullContextTemplate;
    private readonly IReadOnlyList<ITool> _tools;

    /// <summary>Creates the builder from the general "how to answer" template.</summary>
    /// <param name="fullContextTemplate">The general "how to answer" template.</param>
    /// <param name="tools">
    /// Tools available for this session. When there are none, the prompt is exactly what
    /// it was before tools existed.
    /// </param>
    public PromptBuilder(string fullContextTemplate, IReadOnlyList<ITool>? tools = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullContextTemplate);
        _fullContextTemplate = fullContextTemplate;
        _tools = tools ?? [];
    }

    /// <inheritdoc />
    public ChatRequest Build(
        ProjectContext context,
        IReadOnlyList<ChatTurn> history,
        string userInput,
        PromptExtras? extras = null)
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

        // Mode guidance sits before the project instructions so that a project's own
        // rules are the last word on how to behave. A request with no mode is not part
        // of an agent session: it gets no guidance and every tool the builder holds.
        var mode = extras?.Mode;
        var allowedTools = mode is { } active
            ? AgentModePolicy.AllowedTools(active, _tools)
            : [.. _tools.Select(tool => tool.Name)];

        var offeredTools = _tools.Where(tool => allowedTools.Contains(tool.Name)).ToList();

        if (mode is { } guidanceFor)
        {
            systemPrompt
                .Append(AgentModePolicy.Guidance(guidanceFor))
                .Append("______\n");
        }

        if (!string.IsNullOrEmpty(extras?.Instructions))
        {
            systemPrompt
                .Append(InstructionsHeader)
                .Append(extras.Instructions)
                .Append("\n\n______\n");
        }

        if (offeredTools.Count > 0)
        {
            systemPrompt
                .Append(ToolsHeader)
                .Append(RenderTools(offeredTools))
                .Append("______\n");
        }

        if (extras?.Skills is { Count: > 0 } skills)
        {
            systemPrompt
                .Append(SkillsHeader)
                .Append(RenderSkills(skills))
                .Append("______\n");
        }

        if (!string.IsNullOrEmpty(extras?.AttachedContext))
        {
            systemPrompt
                .Append(AttachedContextHeader)
                .Append(extras.AttachedContext)
                .Append("______\n");
        }

        if (!string.IsNullOrEmpty(extras?.RequestedContext))
        {
            systemPrompt
                .Append(RequestedContextHeader)
                .Append(extras.RequestedContext)
                .Append("______\n");
        }

        return new ChatRequest(
            systemPrompt.ToString(),
            $"## Here is user request\n{userInput}",
            allowedTools);
    }

    /// <summary>
    /// Describes the tools offered for this request and how to use them. The project
    /// summary above can be stale or incomplete, so the model is told it may look at the
    /// real files instead of guessing at their contents.
    /// </summary>
    private static string RenderTools(IReadOnlyList<ITool> tools) =>
        new StringBuilder()
            .Append("You can read the project with these tools. Prefer calling a tool over ")
            .Append("assuming what a file contains, and base your answer on what a tool ")
            .AppendLine("actually returned rather than on the summary above.")
            .AppendLine()
            .Append(ToolCatalogText.Lines(tools))
            .AppendLine()
            .AppendLine("A call that reports a failure did not run: correct the arguments or ")
            .AppendLine("use another tool instead of repeating it unchanged.")
            .Append('\n')
            .ToString();

    /// <summary>
    /// Lists the skills by name and description only. A skill's body is fetched by the
    /// <c>read_skill</c> tool, so a project with fifty skills pays for the fifty it does
    /// not use with one line each instead of with their full text.
    /// </summary>
    private static string RenderSkills(IReadOnlyList<SkillDefinition> skills) =>
        new StringBuilder()
            .AppendLine("These skills are available. Read one with the read_skill tool when ")
            .AppendLine("its description matches what you are about to do, and follow it.")
            .AppendLine()
            .AppendJoin(
                '\n',
                skills.Select(skill => $"- {skill.Name}: {skill.Description}"))
            .Append('\n')
            .ToString();

    /// <summary>
    /// Renders one history entry. The timestamp is formatted with the invariant culture
    /// so the same conversation produces the same prompt on every machine.
    /// </summary>
    private static string RenderTurn(ChatTurn turn) =>
        $"### History time:\n\n{turn.Timestamp.ToString(CultureInfo.InvariantCulture)}\n---------\n\n" +
        $"### Here is user request:\n\n{turn.UserRequest}\n---------\n\n" +
        $"### Here is the the response from using AI:\n\n{turn.AiResponse}";
}
