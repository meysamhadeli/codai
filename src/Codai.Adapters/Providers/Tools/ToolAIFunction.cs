using System.Text.Json;
using Codai.Core.Tools;
using Microsoft.Extensions.AI;

namespace Codai.Providers.Tools;

/// <summary>
/// Presents an <see cref="ITool"/> to Microsoft.Extensions.AI so that
/// <see cref="FunctionInvokingChatClient"/> can call it and feed the result back to the
/// model.
/// </summary>
/// <remarks>
/// Written by hand rather than produced by <see cref="AIFunctionFactory"/> because the
/// advertised schema has to be exactly <see cref="ITool.ParametersSchema"/>: a schema
/// inferred from a delegate signature cannot carry the per-parameter descriptions the
/// model needs in order to fill the arguments in correctly.
/// </remarks>
internal sealed class ToolAIFunction : AIFunction
{
    private readonly ITool _tool;
    private readonly IToolExecutor _executor;
    private readonly Action<ToolResult>? _onStopped;
    private readonly ToolReviewState? _review;

    public ToolAIFunction(ITool tool, IToolExecutor executor, Action<ToolResult>? onStopped = null,
        ToolReviewState? review = null)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(executor);

        _tool = tool;
        _executor = executor;
        _onStopped = onStopped;
        _review = review;
    }

    /// <inheritdoc />
    public override string Name => _tool.Name;

    /// <inheritdoc />
    public override string Description => _tool.Description;

    /// <inheritdoc />
    public override JsonElement JsonSchema => _tool.Schema;

    /// <inheritdoc />
    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var iteration = FunctionInvokingChatClient.CurrentContext?.Iteration ?? 0;
        if (_review?.PausedIteration == iteration)
        {
            return "Tool not executed: review the newly activated rules and reconsider this call in a new model round.";
        }

        var result = await _executor
            .ExecuteAsync(_tool.Name, ToJson(arguments), cancellationToken)
            .ConfigureAwait(false);

        if (result.RequiresModelReview && _review is not null)
        {
            _review.PausedIteration = iteration;
        }

        if (result.StopsExecution)
        {
            _onStopped?.Invoke(result);
            if (FunctionInvokingChatClient.CurrentContext is { } context)
            {
                context.Terminate = true;
            }
        }

        // The model is told about failures as data rather than as an exception, so it can
        // correct itself instead of the request failing.
        return result.Content;
    }

    /// <summary>
    /// Copies the arguments into a plain dictionary before serializing: the raw
    /// <see cref="AIFunctionArguments"/> carries binder state alongside the values, and
    /// only the values belong in the tool's JSON.
    /// </summary>
    private static JsonElement ToJson(AIFunctionArguments arguments)
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var argument in arguments)
        {
            payload[argument.Key] = argument.Value;
        }

        return JsonSerializer.SerializeToElement(payload, AIJsonUtilities.DefaultOptions);
    }
}

internal sealed class ToolReviewState
{
    public int PausedIteration { get; set; } = -1;
}
