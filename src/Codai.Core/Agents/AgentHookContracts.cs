using System.Text.Json;
using Codai.Core.Tools;

namespace Codai.Core.Agents;

public enum AgentHookEvent
{
    TaskStarted,
    UserPromptSubmitted,
    PreToolUse,
    PostToolUse,
    PreCompact,
    PostCompact,
    TaskCompleted,
    TaskFailed,
}

public sealed record AgentHookContext(AgentHookEvent Event, string ProjectRoot)
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public string? TaskId { get; init; }
    public string? Prompt { get; init; }
    public string? ToolName { get; init; }
    public JsonElement? Arguments { get; init; }
    public ToolResult? Result { get; init; }
}

public sealed record AgentHookDecision(bool Allowed, string? Reason = null)
{
    public static AgentHookDecision Continue { get; } = new(true);

    public static AgentHookDecision Block(string reason) => new(false, reason);
}

public interface IAgentHooks
{
    Task<AgentHookDecision> InvokeAsync(AgentHookContext context, CancellationToken cancellationToken = default);
}

/// <summary>Optional lifecycle hook implementation supplied by a plugin.</summary>
public interface IPluginAgentHook
{
    /// <summary>Handles one agent lifecycle event.</summary>
    Task<AgentHookDecision> InvokeAsync(
        AgentHookContext context,
        CancellationToken cancellationToken = default);
}

public sealed class NullAgentHooks : IAgentHooks
{
    public static NullAgentHooks Instance { get; } = new();

    private NullAgentHooks() { }

    public Task<AgentHookDecision> InvokeAsync(AgentHookContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(AgentHookDecision.Continue);
    }
}

public sealed class AgentHookRejectedException(string message) : Exception(message);
