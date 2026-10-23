namespace Codai.Core.Tools;

/// <summary>
/// Decides which tools the request in flight may call.
/// </summary>
/// <remarks>
/// <para>
/// The set belongs to the request, not to the session:
/// <see cref="Codai.Core.Chat.ChatRequest.AllowedTools"/> is filled in by the prompt
/// builder from the session's <see cref="Codai.Core.Agents.AgentMode"/>, and this type is
/// what makes it mean something. Without it a read-only mode would only be <em>told</em>
/// not to write, which is a request rather than a rule.
/// </para>
/// <para>
/// A request that names no tools is unrestricted, so a host that never calls
/// <see cref="BeginRequest"/> keeps the behaviour it had before modes existed.
/// </para>
/// <para>
/// One instance belongs to one agent loop, because it holds the set of the request in
/// flight. A loop that runs <em>inside</em> another loop — a sub-agent invoked from a tool
/// — must have its own instance, or the inner request would narrow the outer one.
/// </para>
/// </remarks>
public sealed class ToolAccessPolicy
{
    private IReadOnlyCollection<string>? _allowedTools;

    /// <summary>
    /// Replaces the tool set for the request that is about to be sent.
    /// </summary>
    /// <param name="allowedTools">
    /// Names the request may call, or <c>null</c> when it is not restricted. The collection
    /// is held rather than copied, so callers pass a freshly built one and do not mutate
    /// it afterwards.
    /// </param>
    public void BeginRequest(IReadOnlyCollection<string>? allowedTools) =>
        _allowedTools = allowedTools;

    /// <summary>True when the request in flight may call <paramref name="toolName"/>.</summary>
    public bool IsAllowed(string toolName) =>
        _allowedTools is null ||
        (toolName is not null && _allowedTools.Contains(toolName, StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// What the model is told when it calls a tool this request does not offer. The
    /// available names are listed because a model handed a shorter tool list than it
    /// remembers still has to know what it does have.
    /// </summary>
    public string DescribeDenial(string toolName)
    {
        var available = _allowedTools is { Count: > 0 }
            ? string.Join(", ", _allowedTools)
            : "none";

        return $"Tool '{toolName}' is not available for this request. " +
            $"Available tools: {available}.";
    }
}
