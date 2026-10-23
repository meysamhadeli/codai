using System.Text.Json;
using Codai.Core.Abstractions;
using Codai.Core.Chat;
using Codai.Core.Tools;

namespace Codai.Application;

/// <summary>
/// Notes down what a session actually touched, so a compaction can put it back.
/// </summary>
/// <remarks>
/// <para>
/// The record has to be kept as the tools run rather than worked out from the conversation
/// afterwards, because the conversation is exactly what a compaction is allowed to delete.
/// Reading it back from the turns that survive would describe the tail of the session and
/// nothing else — which is the part that never needed restoring.
/// </para>
/// <para>
/// Only successful calls count. A read that failed touched nothing, and restoring the path it
/// failed on would spend the budget teaching the model the same failure again.
/// </para>
/// </remarks>
public sealed class FootprintObserver : IToolObserver
{
    private const string ReadFileTool = "read_file";
    private const string WriteFileTool = "write_file";
    private const string EditFileTool = "edit_file";
    private const string ReadSkillTool = "read_skill";

    private readonly IToolObserver _inner;
    private readonly SessionFootprint _footprint;

    /// <summary>Creates the observer.</summary>
    /// <param name="inner">Observer that reports to the user, or a null observer.</param>
    /// <param name="footprint">Record to write to.</param>
    public FootprintObserver(IToolObserver inner, SessionFootprint footprint)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(footprint);

        _inner = inner;
        _footprint = footprint;
    }

    /// <summary>The observer this one forwards to.</summary>
    /// <remarks>
    /// Exposed so a host can check what it composed — the decorator hides the reporting
    /// implementation from everything that resolves <see cref="IToolObserver"/>, and a test
    /// that wants to know which one it wrapped would otherwise have to assume.
    /// </remarks>
    public IToolObserver Inner => _inner;

    /// <summary>The record this observer writes to.</summary>
    /// <remarks>
    /// Shared with the session: the same instance has to reach the compactor, or a restore
    /// would read a record nothing ever wrote to.
    /// </remarks>
    public SessionFootprint Footprint => _footprint;

    /// <inheritdoc />
    public void OnToolStarted(string toolName, string summary) =>
        _inner.OnToolStarted(toolName, summary);

    /// <inheritdoc />
    public void OnToolStarted(string toolName, string summary, JsonElement arguments) =>
        _inner.OnToolStarted(toolName, summary, arguments);

    /// <inheritdoc />
    public void OnToolCompleted(string toolName, bool succeeded, string detail) =>
        _inner.OnToolCompleted(toolName, succeeded, detail);

    /// <inheritdoc />
    public void OnToolOutput(string toolName, ProcessOutput output) =>
        _inner.OnToolOutput(toolName, output);

    /// <inheritdoc />
    public void OnToolDenied(string toolName, string summary) =>
        _inner.OnToolDenied(toolName, summary);

    /// <inheritdoc />
    public void OnToolResult(string toolName, JsonElement arguments, ToolResult result)
    {
        Record(toolName, arguments, result);
        _inner.OnToolResult(toolName, arguments, result);
    }

    private void Record(string toolName, JsonElement arguments, ToolResult result)
    {
        if (!result.IsSuccess || arguments.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        switch (toolName)
        {
            case ReadFileTool:
            case WriteFileTool:
            case EditFileTool:
                RecordFiles(arguments);
                break;

            case ReadSkillTool:
                if (ToolArguments.TryGetString(arguments, "name", out var name, out _))
                {
                    _footprint.RecordSkill(name);
                }

                break;
        }
    }

    private void RecordFiles(JsonElement arguments)
    {
        if (ToolArguments.TryGetString(arguments, "path", out var path, out _))
        {
            _footprint.RecordFile(path);
            return;
        }

        if (!arguments.TryGetProperty("paths", out var paths) || paths.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        // Forwards: each path is inserted at the front, so the file the batch ended on is the
        // most recently touched one, and a bounded restore reads the list from the front.
        foreach (var item in paths.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                _footprint.RecordFile(item.GetString());
            }
        }
    }
}
