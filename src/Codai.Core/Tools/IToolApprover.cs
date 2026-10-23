namespace Codai.Core.Tools;

/// <summary>
/// Decides whether a tool that can change state is allowed to run.
/// </summary>
/// <remarks>
/// Only consulted for tools whose <see cref="ITool.IsReadOnly"/> is false. A read-only
/// tool reads the user's own project, which they already asked Codai to work on, so it
/// does not prompt.
/// </remarks>
public interface IToolApprover
{
    /// <summary>
    /// Returns true to allow the call. Implementations must return false rather than
    /// throwing when approval cannot be obtained, so a failure to ask is never
    /// mistaken for consent.
    /// </summary>
    Task<bool> ApproveAsync(
        ToolApprovalRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Approves every request. For non-interactive use and tests.</summary>
public sealed class AlwaysApproveToolApprover : IToolApprover
{
    /// <summary>The shared instance.</summary>
    public static AlwaysApproveToolApprover Instance { get; } = new();

    /// <inheritdoc />
    public Task<bool> ApproveAsync(
        ToolApprovalRequest request,
        CancellationToken cancellationToken = default) => Task.FromResult(true);
}

/// <summary>
/// Refuses every request. The safe default: if nothing has wired up a real prompt, no
/// state-changing tool runs.
/// </summary>
public sealed class DenyAllToolApprover : IToolApprover
{
    /// <summary>The shared instance.</summary>
    public static DenyAllToolApprover Instance { get; } = new();

    /// <inheritdoc />
    public Task<bool> ApproveAsync(
        ToolApprovalRequest request,
        CancellationToken cancellationToken = default) => Task.FromResult(false);
}
