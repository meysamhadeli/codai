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
    /// Investigate and propose a step-by-step plan, read-only. The assistant may look at
    /// the project but must not change it.
    /// </summary>
    // Preserve stored task mode values after removing Ask (0).
    Plan = 1,

    /// <summary>
    /// Work autonomously until the task is done. The assistant may change files, and
    /// every state-changing call still has to pass the approver.
    /// </summary>
    Agent = 2,
}
