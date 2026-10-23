using System.Text;
using System.Text.Json;
using Codai.Core.Abstractions;
using Codai.Core.Agents;

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
    private readonly ToolAccessPolicy _access;
    private readonly IFileSystem _fileSystem;
    private readonly string _projectRoot;
    private readonly int _maxResultChars;
    private readonly AgentBudget _budget;
    private readonly IAgentHooks _hooks;
    private readonly IToolGuidance? _guidance;
    private readonly IWorkspaceCheckpointStore? _checkpoints;
    private readonly AgentTaskContext? _taskContext;

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
    /// <param name="access">
    /// The tools the request in flight may call. Defaults to a fresh unrestricted policy,
    /// and must be the same instance the caller uses to begin each request, or the
    /// restriction would be recorded somewhere this runner never reads.
    /// </param>
    /// <param name="budget">Shared per-task token, cost, and tool-call limits.</param>
    /// <param name="hooks">Optional lifecycle policy; failures stop execution without replay.</param>
    /// <param name="guidance">Optional pre-execution guidance requiring a new model decision.</param>
    /// <param name="checkpoints">Optional workspace checkpoint store for mutating tool calls.</param>
    /// <param name="taskContext">Optional active task identity used by checkpoint capture.</param>
    public ToolRunner(
        IToolRegistry registry,
        IToolApprover approver,
        IToolObserver observer,
        IFileSystem fileSystem,
        string projectRoot,
        int maxResultChars = DefaultMaxResultChars,
        ToolCallGovernor? governor = null,
        ToolAccessPolicy? access = null,
        AgentBudget? budget = null,
        IAgentHooks? hooks = null,
        IToolGuidance? guidance = null,
        IWorkspaceCheckpointStore? checkpoints = null,
        AgentTaskContext? taskContext = null)
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
        _access = access ?? new ToolAccessPolicy();
        _fileSystem = fileSystem;
        _projectRoot = projectRoot;
        _maxResultChars = maxResultChars;
        _budget = budget ?? new AgentBudget();
        _hooks = hooks ?? NullAgentHooks.Instance;
        _guidance = guidance;
        _checkpoints = checkpoints;
        _taskContext = taskContext;
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

        // A tool the request does not offer is refused before anything else, including
        // before the budget: "this mode cannot write" is a better answer than "you have
        // used your calls", and it does not depend on the provider having honoured the
        // shorter tool list it was given.
        if (!_access.IsAllowed(toolName))
        {
            var denial = _access.DescribeDenial(toolName);
            _observer.OnToolDenied(toolName, denial);
            return ToolResult.Failure(denial);
        }

        // Limits come before approval: a call that may not run at all should not cost the
        // user a prompt, and a model that has hit the limit needs to be told to answer.
        var decision = _governor.Review(toolName, arguments);

        if (!decision.Allowed)
        {
            _observer.OnToolDenied(toolName, decision.Reason);
            return ToolResult.Failure(decision.Reason);
        }

        if (!_budget.TryConsumeToolCall(out var budgetReason))
        {
            _observer.OnToolDenied(toolName, budgetReason);
            return ToolResult.Failure(budgetReason);
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (_guidance?.GetGuidance(tool, arguments) is { } guidance)
            {
                if (guidance.Length > _maxResultChars)
                {
                    throw new InvalidOperationException("Tool guidance exceeds the result limit; tool was not executed.");
                }

                return ToolResult.Success(guidance) with { RequiresModelReview = true };
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            var reason = $"Tool guidance blocked '{toolName}': {exception.Message}";
            _observer.OnToolDenied(toolName, reason);
            return _governor.RecordOutcome(ToolResult.Failure(reason)) with { StopsExecution = true };
        }

        if (tool.RequiresApproval && !await IsApprovedAsync(tool, summary, arguments, cancellationToken).ConfigureAwait(false))
        {
            _observer.OnToolDenied(toolName, summary);
            return ToolResult.Failure(
                $"The user declined to run '{toolName}'. Do not retry it. " +
                "Explain what you wanted to do and ask how to proceed.");
        }

        var before = await InvokeHookAsync(new AgentHookContext(AgentHookEvent.PreToolUse, _projectRoot)
        {
            ToolName = toolName,
            Arguments = arguments,
        }, cancellationToken).ConfigureAwait(false);
        if (!before.Allowed)
        {
            return RejectHook(toolName, before, "PreToolUse");
        }

        _observer.OnToolStarted(toolName, summary, arguments);

        var context = new ToolContext(_projectRoot, _fileSystem, _observer);

        ToolResult result;
        try
        {
            result = await tool.InvokeAsync(arguments, context, cancellationToken).ConfigureAwait(false);
            var content = Truncate(result.Content);

            _observer.OnToolCompleted(
                toolName,
                result.IsSuccess,
                result.IsSuccess ? summary : FirstLine(content));

            result = result with { Content = content };
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
            result = ToolResult.Failure($"Tool '{toolName}' threw {ex.GetType().Name}: {ex.Message}");
        }

        result = _governor.RecordOutcome(result);
        var after = await InvokeHookAsync(new AgentHookContext(AgentHookEvent.PostToolUse, _projectRoot)
        {
            ToolName = toolName,
            Arguments = arguments,
            Result = result,
        }, cancellationToken).ConfigureAwait(false);
        if (!after.Allowed)
        {
            return RejectHook(toolName, after, "PostToolUse (tool already ran)");
        }

        if (!tool.IsReadOnly && result.IsSuccess && _checkpoints is not null && _taskContext?.TaskId is { } taskId)
        {
            try
            {
                await _checkpoints.CaptureAsync(_projectRoot, taskId, $"After tool: {toolName}", cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return ToolResult.Failure(
                    $"Tool '{toolName}' completed, but its checkpoint could not be saved: {ex.Message}") with
                {
                    StopsExecution = true,
                };
            }
        }

        try
        {
            _observer.OnToolResult(toolName, arguments, result);
        }
        catch
        {
        }
        return result;
    }

    private ToolResult RejectHook(string toolName, AgentHookDecision decision, string stage)
    {
        var reason = $"{stage} hook blocked '{toolName}': {decision.Reason ?? "No reason supplied."}";
        _observer.OnToolDenied(toolName, reason);
        return ToolResult.Failure(reason) with { StopsExecution = true };
    }

    private async Task<AgentHookDecision> InvokeHookAsync(AgentHookContext context, CancellationToken cancellationToken)
    {
        try
        {
            return await _hooks.InvokeAsync(context, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return AgentHookDecision.Block($"Hook threw {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Asks the approver. A cancellation propagates; any other failure is treated as a
    /// refusal, because being unable to ask is not consent.
    /// </summary>
    private async Task<bool> IsApprovedAsync(
        ITool tool,
        string summary,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var request = ToolApprovalRequest.Create(tool.Name, tool.Description, summary, arguments);

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
