using System.Text.Json;
using Codai.Core.Abstractions;
using Codai.Core.Agents;
using Codai.Core.Tools;

namespace Codai.Application.Interaction;

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
    private readonly IAgentProgressSink _progress;

    /// <summary>Creates the observer.</summary>
    public InteractiveToolObserver(IUserInterface ui, IAgentProgressSink? progress = null)
    {
        ArgumentNullException.ThrowIfNull(ui);
        _ui = ui;
        _progress = progress ?? NullAgentProgressSink.Instance;
    }

    /// <inheritdoc />
    public void OnToolStarted(string toolName, string summary)
    {
        _ui.WriteToolStarted(toolName, summary);
        Publish(AgentProgressEventKind.ToolStarted, toolName, summary);
    }

    public void OnToolStarted(string toolName, string summary, JsonElement arguments)
    {
        _ui.WriteToolStarted(toolName, summary, arguments);
        Publish(AgentProgressEventKind.ToolStarted, toolName, summary);
    }

    /// <inheritdoc />
    public void OnToolCompleted(string toolName, bool succeeded, string detail)
    {
        _ui.WriteToolCompleted(toolName, succeeded, detail);
        Publish(AgentProgressEventKind.ToolCompleted, toolName, detail);
    }

    /// <inheritdoc />
    public void OnToolDenied(string toolName, string summary)
    {
        _ui.WriteToolDenied(toolName, summary);
        Publish(AgentProgressEventKind.ToolDenied, toolName, summary);
    }

    /// <inheritdoc />
    public void OnToolOutput(string toolName, ProcessOutput output)
    {
        if (string.IsNullOrEmpty(output.Text))
        {
            return;
        }

        _ui.WriteToolOutput(toolName, output);
        Publish(output.IsStandardError ? AgentProgressEventKind.CommandStandardError : AgentProgressEventKind.CommandStandardOutput,
            toolName, output.Text);
    }

    private void Publish(AgentProgressEventKind kind, string description, string? message)
    {
        try
        {
            var notification = _progress.PublishAsync(new AgentProgressEvent(
                description,
                description,
                kind,
                message,
                DateTimeOffset.UtcNow));

            if (!notification.IsCompletedSuccessfully)
            {
                _ = ObserveAsync(notification);
            }
        }
        catch
        {
        }
    }

    public void OnToolResult(string toolName, JsonElement arguments, ToolResult result) =>
        _ui.WriteToolResult(toolName, arguments, result);

    private static async Task ObserveAsync(ValueTask notification)
    {
        try
        {
            await notification.ConfigureAwait(false);
        }
        catch
        {
        }
    }
}
