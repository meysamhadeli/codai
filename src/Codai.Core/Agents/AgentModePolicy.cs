using Codai.Core.Tools;

namespace Codai.Core.Agents;

/// <summary>
/// Decides what each <see cref="AgentMode"/> is allowed to do.
/// </summary>
/// <remarks>
/// Two rules keep this honest:
/// <list type="bullet">
/// <item>Tools are selected by <see cref="ITool.IsReadOnly"/>, never by a list of names,
/// so a new mutating tool is excluded from Plan mode the moment it declares
/// itself as mutating rather than when someone remembers to update a table.</item>
/// <item>The prompt guidance and the tool set are produced from the same mode, so the
/// assistant is never told it may edit while being given no tool that can.</item>
/// </list>
/// </remarks>
public static class AgentModePolicy
{
    /// <summary>
    /// Names of the tools <paramref name="mode"/> may call.
    /// </summary>
    public static IReadOnlyCollection<string> AllowedTools(
        AgentMode mode,
        IReadOnlyList<ITool> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);

        return mode == AgentMode.Agent
            ? [.. tools.Select(tool => tool.Name)]
            : [.. tools.Where(tool => tool.IsReadOnly).Select(tool => tool.Name)];
    }

    /// <summary>True when the mode may change the project.</summary>
    public static bool AllowsWrites(AgentMode mode) => mode == AgentMode.Agent;

    /// <summary>
    /// The request-scoped response and work guidance appended to the system prompt for
    /// <paramref name="mode"/>.
    /// </summary>
    public static string Guidance(AgentMode mode) => ResponseGuidance + (mode switch
    {
        AgentMode.Plan => PlanGuidance,
        AgentMode.Agent => AgentGuidance,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown agent mode."),
    });

    /// <summary>
    /// A one-sentence description of <paramref name="mode" /> for the user, as opposed to
    /// <see cref="Guidance"/> which addresses the model.
    /// </summary>
    public static string Summary(AgentMode mode) => mode switch
    {
        AgentMode.Plan => "I won't change anything; I'll answer with a plan you can review.",
        AgentMode.Agent => "I can change files and run commands, and I'll ask before each one.",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown agent mode."),
    };

    private const string ResponseGuidance =
        "## Respond to the actual request\n\n" +
        "- Be concise and direct by default. Scale detail to the task, not the amount of project context.\n" +
        "- For greetings and casual conversation, default to one short, natural sentence unless the user asks for more detail.\n" +
        "  Do not introduce unrelated project details, capability lists, or suggested tasks.\n" +
        "- Do not call tools for greetings or acknowledgements. Use tools only when the request needs their evidence or actions.\n" +
        "- Project context is reference material, not a request to work on it. Apply project rules when their conditions are met; do not recite them.\n" +
        "- Answer direct questions directly. Do not force a plan, progress report, or file summary onto a simple answer.\n" +
        "- When essential user input is missing, use `ask_question` if available instead of asking a blocking question only in prose.\n" +
        "  Offer concise choices for yes/no or option questions; allow a custom answer. Clarification is never permission to write or execute.\n" +
        "- Ground factual claims in available evidence. Never invent file contents, actions, verification results, or provider capabilities.\n\n";

    private const string PlanGuidance =
        "## How to work in this session\n\n" +
        "For implementation requests, investigate the project and produce a plan.\n\n" +
        "- You must not change any file. Use read tools only when needed to answer the request.\n" +
        "- If the tool list below offers `task`, use it for any search that would need a\n" +
        "  dozen files read to answer one question: the sub-agent explores on its own and\n" +
        "  reports back a summary.\n" +
        "- For implementation requests, answer with a numbered plan. Each step names the files it touches and what it\n" +
        "  changes, in the order the changes must be made.\n" +
        "- Base every step on something you actually read, not on what a project of this\n" +
        "  kind usually looks like.\n" +
        "- End implementation plans with the risks, the unknowns, and anything you could not verify.\n";

    private const string AgentGuidance =
        "## How to work in this session\n\n" +
        "For implementation requests, work on this project and finish the requested task.\n\n" +
        "- Read the files involved before you change them, and change the smallest thing\n" +
        "  that solves the problem.\n" +
        "- If the tool list below offers `task`, use it when finding something out would\n" +
        "  mean reading far more of the project than the answer is worth holding onto: the\n" +
        "  sub-agent looks around, reports back, and its reading never enters this\n" +
        "  conversation.\n" +
        "- Keep going until the task is done. A tool call that failed is information, not a\n" +
        "  reason to stop: correct it or take another route.\n" +
        "- After changes, run the smallest relevant verification. Report checks you could not run.\n" +
        "- After changing files, briefly summarize what changed and what you verified. Do not list unaffected files.\n" +
        "- Ask the user only when the decision cannot be made from the project itself.\n";
}
