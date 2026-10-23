using Codai.Core.Abstractions;
using Codai.Core.Tools;

namespace Codai.Application.Interaction;

/// <summary>
/// Asks the user before a tool that can change something is allowed to run.
/// </summary>
/// <remarks>
/// Only reached for tools that report <see cref="ITool.RequiresApproval"/>; the runner never asks
/// about reads. The answer defaults to no: declining, timing out or being unable to prompt all mean
/// the tool does not run. A user who answers "don't ask again" is taken at their word for the rest
/// of the session, but only for the tool they were asked about — approving <c>run_command</c> is not
/// approving a file write, and nothing here can lift the approval policy themselves.
/// </remarks>
public sealed class InteractiveToolApprover : IToolApprover
{
    private readonly IUserInterface _ui;
    private readonly HashSet<string> _remembered = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    /// <summary>Creates the approver.</summary>
    public InteractiveToolApprover(IUserInterface ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        _ui = ui;
    }

    /// <inheritdoc />
    public async Task<bool> ApproveAsync(
        ToolApprovalRequest request,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_remembered.Contains(request.ToolName))
            {
                return true;
            }
        }

        var decision = await _ui.ApproveAsync(request, cancellationToken).ConfigureAwait(false);
        if (decision == ApprovalDecision.AllowAlways)
        {
            lock (_gate)
            {
                _remembered.Add(request.ToolName);
            }
        }

        return decision != ApprovalDecision.Deny;
    }
}
