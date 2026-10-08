using Codai.Core.Abstractions;
using Codai.Core.Tools;

namespace Codai.Session.Tools;

/// <summary>
/// Asks the user before a tool that can change something is allowed to run.
/// </summary>
/// <remarks>
/// Only reached for tools that report <see cref="ITool.IsReadOnly"/> as false; the
/// runner never asks about reads. The answer defaults to no: declining, timing out or
/// being unable to prompt all mean the tool does not run.
/// </remarks>
public sealed class InteractiveToolApprover : IToolApprover
{
    private readonly IUserInterface _ui;

    /// <summary>Creates the approver.</summary>
    public InteractiveToolApprover(IUserInterface ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        _ui = ui;
    }

    /// <inheritdoc />
    public Task<bool> ApproveAsync(
        ToolApprovalRequest request,
        CancellationToken cancellationToken = default) =>
        _ui.ConfirmAsync(
            $"Allow {request.ToolName}: {request.Summary}",
            cancellationToken);
}
