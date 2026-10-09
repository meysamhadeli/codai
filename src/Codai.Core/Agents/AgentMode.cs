namespace Codai.Core.Agents;

/// <summary>
/// How much freedom the assistant has in a session.
/// </summary>
/// <remarks>
/// The mode is a property of the assistant, not of the provider: it decides which tools
/// are offered and what the prompt asks for. Every provider therefore gets the same
/// behaviour, including the hand-written Anthropic and Gemini clients.
/// </remarks>
public enum AgentMode
{
    /// <summary>
    /// Answer questions, read-only. The assistant may look at the project but must not
    /// change it; a needed change is described instead of applied.
    /// </summary>
    Ask,

    /// <summary>
    /// Investigate and propose a plan, read-only. Same tools as <see cref="Ask"/>, but
    /// the answer is a step-by-step plan rather than an explanation.
    /// </summary>
    Plan,

    /// <summary>
    /// Work autonomously until the task is done. The assistant may change files, and
    /// every state-changing call still has to pass the approver.
    /// </summary>
    Agent,
}
