using System.Text.Json;

namespace Codai.Core.Tools;

/// <summary>
/// Keeps one request's tool use inside bounds.
/// </summary>
/// <remarks>
/// <para>
/// An agent that calls tools in a loop can spend a whole context window and a whole
/// budget without ever answering. Two cheap guards catch the two ways that happens:
/// </para>
/// <list type="bullet">
/// <item>A total call limit, which bounds the worst case of a model that keeps finding
/// one more thing to check.</item>
/// <item>A repeat limit, which catches the loop where a model re-issues the same call
/// with the same arguments. That call cannot produce new information, so the answer is
/// refused and the refusal says so, which is what makes the model change approach
/// instead of retrying.</item>
/// </list>
/// <para>
/// Repeated <em>different</em> calls are not restricted: reading twenty files in a row is
/// exactly what exploring a project looks like.
/// </para>
/// <para>
/// The governor is stateful and scoped to a single request, so it is cleared by
/// <see cref="BeginRequest"/> before the model is asked anything.
/// </para>
/// </remarks>
public sealed class ToolCallGovernor
{
    /// <summary>Calls one user request may make before the model is told to wrap up.</summary>
    public const int DefaultMaxCalls = 60;

    /// <summary>Times one identical call may be repeated before it is refused.</summary>
    public const int DefaultMaxRepeats = 2;

    /// <summary>Consecutive tool failures allowed before execution stops.</summary>
    public const int DefaultMaxConsecutiveFailures = 3;

    private readonly Dictionary<string, int> _repeats = new(StringComparer.Ordinal);

    private int _calls;
    private int _consecutiveFailures;
    private int _maxConsecutiveFailures = DefaultMaxConsecutiveFailures;

    /// <summary>Total tool calls allowed in one request.</summary>
    public int MaxCalls { get; init; } = DefaultMaxCalls;

    /// <summary>Times one identical call is allowed before it is refused.</summary>
    public int MaxRepeats { get; init; } = DefaultMaxRepeats;

    /// <summary>Calls made since the last <see cref="BeginRequest"/>.</summary>
    public int Calls => _calls;

    /// <summary>Maximum consecutive executed-tool failures in one request.</summary>
    public int MaxConsecutiveFailures
    {
        get => _maxConsecutiveFailures;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            _maxConsecutiveFailures = value;
        }
    }

    /// <summary>Clears the per-request counters. Called once per user request.</summary>
    public void BeginRequest()
    {
        _calls = 0;
        _consecutiveFailures = 0;
        _repeats.Clear();
    }

    /// <summary>Records an executed tool outcome, allowing recovery only within the failure limit.</summary>
    public ToolResult RecordOutcome(ToolResult result)
    {
        _consecutiveFailures = result.IsSuccess ? 0 : _consecutiveFailures + 1;
        return _consecutiveFailures >= MaxConsecutiveFailures
            ? result with { Content = $"{result.Content}\n\n{FailureLimitReason()}", StopsExecution = true }
            : result;
    }

    private string FailureLimitReason() =>
        $"Tool failure limit reached ({MaxConsecutiveFailures} consecutive failures). " +
        "Execution stopped. Review the failures before starting another request.";

    /// <summary>Reviews a call about to be made.</summary>
    public ToolCallDecision Review(string toolName, JsonElement arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);

        if (_consecutiveFailures >= MaxConsecutiveFailures)
        {
            return ToolCallDecision.Deny(FailureLimitReason());
        }

        if (_calls >= MaxCalls)
        {
            return ToolCallDecision.Deny(
                $"Tool call limit reached ({MaxCalls} calls in one request). Stop calling tools " +
                "and answer with what you have found so far, saying what is still unverified.");
        }

        var fingerprint = Fingerprint(toolName, arguments);
        _repeats.TryGetValue(fingerprint, out var repeats);

        if (repeats >= MaxRepeats)
        {
            return ToolCallDecision.Deny(
                $"This exact call to '{toolName}' already ran {repeats} time(s) with the same " +
                "arguments, so it cannot return anything new. Change the arguments, use a " +
                "different tool, or answer with what you already have.");
        }

        _calls++;
        _repeats[fingerprint] = repeats + 1;

        return ToolCallDecision.Allow;
    }

    /// <summary>
    /// Identity of a call: its name plus its arguments. Property order is not part of
    /// identity — a model that re-emits the same arguments in a different order has still
    /// asked the same question.
    /// </summary>
    private static string Fingerprint(string toolName, JsonElement arguments)
    {
        if (arguments.ValueKind != JsonValueKind.Object)
        {
            return toolName;
        }

        var properties = arguments
            .EnumerateObject()
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .Select(property => $"{property.Name}={property.Value.GetRawText()}");

        return $"{toolName}({string.Join(",", properties)})";
    }
}
