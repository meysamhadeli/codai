using System.Text;
using System.Text.Json;
using Codai.Core.Abstractions;

namespace Codai.Core.Tools;

/// <summary>
/// Default <see cref="IToolExecutor"/>: gates state-changing tools behind
/// <see cref="IToolApprover"/>, reports progress through <see cref="IToolObserver"/>,
/// and turns any tool failure into a result the model can react to.
/// </summary>
public sealed class ToolRunner : IToolExecutor
{
    /// <summary>
    /// Upper bound on a single tool result, as a backstop against flooding the context
    /// window. Individual tools should truncate sensibly before reaching this.
    /// </summary>
    public const int DefaultMaxResultChars = 60_000;

    private readonly IToolRegistry _registry;
    private readonly IToolApprover _approver;
    private readonly IToolObserver _observer;
    private readonly ToolCallGovernor _governor;
    private readonly IFileSystem _fileSystem;
    private readonly string _projectRoot;
    private readonly int _maxResultChars;

    /// <summary>Creates a runner.</summary>
    /// <param name="registry">The tools the model may call.</param>
    /// <param name="approver">Asked before any tool that can change something.</param>
    /// <param name="observer">Told when a call starts, finishes or is refused.</param>
    /// <param name="fileSystem">File system the tools operate on.</param>
    /// <param name="projectRoot">
    /// Boundary every path is resolved inside, so no tool can reach outside the project.
    /// </param>
    /// <param name="maxResultChars">Upper bound on a single tool result.</param>
    /// <param name="governor">
    /// Per-request call limits. Defaults to a fresh governor, so a host that does not
    /// care about tool budgets still gets a bounded loop instead of an unbounded one.
    /// </param>
    public ToolRunner(
        IToolRegistry registry,
        IToolApprover approver,
        IToolObserver observer,
        IFileSystem fileSystem,
        string projectRoot,
        int maxResultChars = DefaultMaxResultChars,
        ToolCallGovernor? governor = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(approver);
        ArgumentNullException.ThrowIfNull(observer);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxResultChars);

        _registry = registry;
        _approver = approver;
        _observer = observer;
        _governor = governor ?? new ToolCallGovernor();
        _fileSystem = fileSystem;
        _projectRoot = projectRoot;
        _maxResultChars = maxResultChars;
    }

    /// <inheritdoc />
    public IReadOnlyList<ITool> Tools => _registry.Tools;

    /// <inheritdoc />
    public async Task<ToolResult> ExecuteAsync(
        string toolName,
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        if (!_registry.TryGet(toolName, out var tool))
        {
            var known = string.Join(", ", _registry.Tools.Select(t => t.Name));
            return ToolResult.Failure(
                $"Unknown tool '{toolName}'. Available tools: {known}.");
        }

        var summary = Summarize(toolName, arguments);

        // Limits come before approval: a call that may not run at all should not cost the
        // user a prompt, and a model that has hit the limit needs to be told to answer.
        var decision = _governor.Review(toolName, arguments);

        if (!decision.Allowed)
        {
            _observer.OnToolDenied(toolName, decision.Reason);
            return ToolResult.Failure(decision.Reason);
        }

        if (!tool.IsReadOnly && !await IsApprovedAsync(tool, summary, cancellationToken).ConfigureAwait(false))
        {
            _observer.OnToolDenied(toolName, summary);
            return ToolResult.Failure(
                $"The user declined to run '{toolName}'. Do not retry it. " +
                "Explain what you wanted to do and ask how to proceed.");
        }

        _observer.OnToolStarted(toolName, summary);

        var context = new ToolContext(_projectRoot, _fileSystem, _observer);

        try
        {
            var result = await tool.InvokeAsync(arguments, context, cancellationToken).ConfigureAwait(false);
            var content = Truncate(result.Content);

            _observer.OnToolCompleted(
                toolName,
                result.IsSuccess,
                result.IsSuccess ? summary : FirstLine(content));

            return result with { Content = content };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A buggy or badly-configured tool must not end the session; the model gets
            // told what went wrong and can adapt.
            _observer.OnToolCompleted(toolName, succeeded: false, ex.Message);
            return ToolResult.Failure($"Tool '{toolName}' threw {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Asks the approver. A cancellation propagates; any other failure is treated as a
    /// refusal, because being unable to ask is not consent.
    /// </summary>
    private async Task<bool> IsApprovedAsync(
        ITool tool,
        string summary,
        CancellationToken cancellationToken)
    {
        var request = new ToolApprovalRequest(tool.Name, tool.Description, summary);

        try
        {
            return await _approver.ApproveAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private string Truncate(string content)
    {
        if (content.Length <= _maxResultChars)
        {
            return content;
        }

        return string.Concat(
            content.AsSpan(0, _maxResultChars),
            $"\n\n[output truncated after {_maxResultChars} characters]");
    }

    private static string FirstLine(string text)
    {
        var newline = text.IndexOf('\n', StringComparison.Ordinal);
        var line = newline < 0 ? text : text[..newline];

        return line.Length <= 200 ? line : string.Concat(line.AsSpan(0, 197), "...");
    }

    /// <summary>Renders a call for display, e.g. <c>grep(pattern=TODO, include=*.cs)</c>.</summary>
    private static string Summarize(string toolName, JsonElement arguments)
    {
        if (arguments.ValueKind != JsonValueKind.Object)
        {
            return toolName;
        }

        var builder = new StringBuilder(toolName).Append('(');
        var first = true;

        foreach (var property in arguments.EnumerateObject())
        {
            if (!first)
            {
                builder.Append(", ");
            }

            first = false;

            var value = property.Value.ValueKind == JsonValueKind.String
                ? property.Value.GetString()
                : property.Value.ToString();

            builder.Append(property.Name).Append('=');

            if (value is null)
            {
                builder.Append("null");
            }
            else if (value.Length <= 80)
            {
                builder.Append(value);
            }
            else
            {
                builder.Append(value.AsSpan(0, 77)).Append("...");
            }
        }

        return builder.Append(')').ToString();
    }
}
