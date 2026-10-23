using System.Text.Json;
using Codai.Core.Abstractions;
using Codai.Core.Tools;

namespace Codai.Tools.Tests;

/// <summary>A registry entry with a scripted outcome, used to drive <see cref="ToolRunner"/>.</summary>
internal sealed class FakeTool : ITool
{
    public FakeTool(string name, bool isReadOnly = true)
    {
        Name = name;
        IsReadOnly = isReadOnly;
    }

    public string Name { get; }

    public string Description => $"A fake '{Name}' tool.";

    public bool IsReadOnly { get; }

    public string ParametersSchema => """{"type":"object","properties":{}}""";

    public System.Text.Json.JsonElement Schema => JsonSchema.Parse(ParametersSchema);

    public int Invocations { get; private set; }

    public Func<ToolResult> Result { get; set; } = () => ToolResult.Success("ok");

    public Task<ToolResult> InvokeAsync(
        JsonElement arguments,
        ToolContext context,
        CancellationToken cancellationToken = default)
    {
        Invocations++;
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Result());
    }
}

/// <summary>Records the observer callbacks a runner makes.</summary>
internal sealed class RecordingObserver : IToolObserver
{
    public List<string> Events { get; } = [];

    public List<(string Tool, JsonElement Arguments, ToolResult Result)> Results { get; } = [];

    public bool ThrowOnResult { get; init; }

    public void OnToolStarted(string toolName, string summary) => Events.Add($"started:{toolName}");

    public void OnToolCompleted(string toolName, bool succeeded, string detail) =>
        Events.Add($"completed:{toolName}:{succeeded}");

    public void OnToolDenied(string toolName, string summary) => Events.Add($"denied:{toolName}");

    public void OnToolResult(string toolName, JsonElement arguments, ToolResult result)
    {
        if (ThrowOnResult)
        {
            throw new InvalidOperationException("Preview rendering failed.");
        }
        Results.Add((toolName, arguments.Clone(), result));
    }
}

/// <summary>An approver with a fixed answer.</summary>
internal sealed class StubApprover : IToolApprover
{
    private readonly bool _approve;
    private readonly bool _throw;

    public StubApprover(bool approve, bool @throw = false)
    {
        _approve = approve;
        _throw = @throw;
    }

    public int Calls { get; private set; }

    public ToolApprovalRequest LastRequest { get; private set; }

    public Task<bool> ApproveAsync(
        ToolApprovalRequest request,
        CancellationToken cancellationToken = default)
    {
        Calls++;
        LastRequest = request;

        return _throw
            ? throw new InvalidOperationException("no way to ask")
            : Task.FromResult(_approve);
    }
}

/// <summary>
/// A process runner that records the command line it was asked to run and returns a
/// scripted result. Real process tests live with the runner itself.
/// </summary>
internal sealed class StubProcessRunner : IProcessRunner
{
    public string? CommandLine { get; private set; }

    public string? WorkingDirectory { get; private set; }

    public TimeSpan Timeout { get; private set; }

    public ProcessResult Result { get; set; } = new(0, "ok", string.Empty, false);

    public Task<ProcessResult> RunAsync(
        string commandLine,
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        CommandLine = commandLine;
        WorkingDirectory = workingDirectory;
        Timeout = timeout;

        return Task.FromResult(Result);
    }
}
