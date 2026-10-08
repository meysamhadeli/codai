using Codai.Core.Abstractions;
using Codai.Core.Tools;

namespace Codai.Session.Tools;

/// <summary>
/// Reports tool activity to the user, so a call the model made is never invisible.
/// </summary>
/// <remarks>
/// Without this, a model that reads half the project looks like a model that stalled:
/// the answer arrives with no indication of what it did to get there.
/// </remarks>
public sealed class InteractiveToolObserver : IToolObserver
{
    private readonly IUserInterface _ui;

    /// <summary>Creates the observer.</summary>
    public InteractiveToolObserver(IUserInterface ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        _ui = ui;
    }

    /// <inheritdoc />
    public void OnToolStarted(string toolName, string summary) => _ui.WriteInfo($"  • {summary}");

    /// <inheritdoc />
    public void OnToolCompleted(string toolName, bool succeeded, string detail) =>
        _ui.WriteInfo(succeeded ? $"  ✓ {toolName}" : $"  ✗ {toolName}: {detail}");

    /// <inheritdoc />
    public void OnToolDenied(string toolName, string summary) =>
        _ui.WriteInfo($"  ✗ {toolName} declined");
}
