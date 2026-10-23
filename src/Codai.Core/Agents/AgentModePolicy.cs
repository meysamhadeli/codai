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
    /// The paragraph appended to the system prompt describing how to behave in
    /// <paramref name="mode"/>.
    /// </summary>
    public static string Guidance(AgentMode mode) => mode switch
    {
        AgentMode.Plan => PlanGuidance,
        AgentMode.Agent => AgentGuidance,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown agent mode."),
    };

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

    private const string PlanGuidance =
        "## How to work in this session\n\n" +
        "You are investigating this project and producing a plan.\n\n" +
        "- You must not change any file. Read whatever you need with the tools first.\n" +
        "- If the tool list below offers `task`, use it for any search that would need a\n" +
        "  dozen files read to answer one question: the sub-agent explores on its own and\n" +
        "  reports back a summary.\n" +
        "- Answer with a numbered plan. Each step names the files it touches and what it\n" +
        "  changes, in the order the changes must be made.\n" +
        "- Base every step on something you actually read, not on what a project of this\n" +
        "  kind usually looks like.\n" +
        "- End with the risks, the unknowns, and anything you could not verify.\n";

    private const string AgentGuidance =
        "## How to work in this session\n\n" +
        "You are working on this project and you are expected to finish the task.\n\n" +
        "- Read the files involved before you change them, and change the smallest thing\n" +
        "  that solves the problem.\n" +
        "- If the tool list below offers `task`, use it when finding something out would\n" +
        "  mean reading far more of the project than the answer is worth holding onto: the\n" +
        "  sub-agent looks around, reports back, and its reading never enters this\n" +
        "  conversation.\n" +
        "- Keep going until the task is done. A tool call that failed is information, not a\n" +
        "  reason to stop: correct it or take another route.\n" +
        "- After you change something, read it back to confirm the change is what you meant.\n" +
        "- When you are done, list the files you changed and what changed in each.\n" +
        "- Ask the user only when the decision cannot be made from the project itself.\n";
}
