
using System.Text.Json;
using Codai.Core.Tokens;
using Codai.Core.Tools;
namespace Codai.Core.Abstractions;

/// <summary>
/// Presentation surface used by the interactive session. Implemented by the CLI
/// (Spectre.Console) and by a test double in unit tests.
/// </summary>
public interface IUserInterface
{
    /// <summary>Reads one line of user input, or <c>null</c> at end of input.</summary>
    Task<string?> ReadInputAsync(CancellationToken cancellationToken = default);

    async Task<string?> AskQuestionAsync(string question, IReadOnlyList<string> options, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        WriteLine(question);
        for (var index = 0; index < options.Count; index++)
        {
            WriteLine($"{index + 1}. {options[index]}");
        }
        var answer = await ReadInputAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return int.TryParse(answer, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var selected)
            && selected > 0 && selected <= options.Count ? options[selected - 1] : answer;
    }

    /// <summary>Asks a yes/no question.</summary>
    Task<bool> ConfirmAsync(string question, CancellationToken cancellationToken = default);

    /// <summary>Asks whether a state-changing tool call may run.</summary>
    /// <remarks>
    /// Separate from <see cref="ConfirmAsync"/> because a tool call needs more than a question:
    /// the exact command, the path, or the patch being approved has to be on screen, and the answer
    /// can be "not this time" or "not again for this tool". Hosts that cannot show that render a
    /// plain confirmation, which is why the default implementation does exactly that.
    /// </remarks>
    async Task<ApprovalDecision> ApproveAsync(ToolApprovalRequest request, CancellationToken cancellationToken = default) =>
        await ConfirmAsync($"Allow {request.ToolName}? {request.DetailOrDefault}", cancellationToken).ConfigureAwait(false)
            ? ApprovalDecision.AllowOnce
            : ApprovalDecision.Deny;

    Task<string?> ChooseAsync(string title, IReadOnlyList<SessionMenuItem> items, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);

    Task<string?> ReadValueAsync(string title, string initialValue = "", bool secret = false, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);

    void SetTheme(string name) { }

    void InsertInput(string text) { }

    void WriteLine(string message);

    void WriteError(string message);

    void WriteSuccess(string message);

    void WriteInfo(string message);

    void WriteToolStarted(string toolName, string summary) => WriteInfo($"  • {summary}");

    void WriteToolStarted(string toolName, string summary, JsonElement arguments) => WriteToolStarted(toolName, summary);

    void WriteToolCompleted(string toolName, bool succeeded, string detail) =>
        WriteInfo(succeeded ? $"  ✓ {toolName}" : $"  ✗ {toolName}: {detail}");

    void WriteToolDenied(string toolName, string summary) => WriteInfo($"  ✗ {toolName} declined");

    void WriteToolOutput(string toolName, ProcessOutput output) =>
        WriteInfo($"[{toolName} {(output.IsStandardError ? "stderr" : "stdout")}] {output.Text.TrimEnd()}");

    void WriteToolResult(string toolName, JsonElement arguments, ToolResult result) { }

    /// <summary>Renders a streamed markdown chunk (code fences toggle syntax colouring).</summary>
    void WriteMarkdown(string markdown);

    /// <summary>Runs <paramref name="work"/> while displaying a spinner.</summary>
    Task<T> RunWithProgressAsync<T>(
        string label,
        Func<CancellationToken, Task<T>> work,
        CancellationToken cancellationToken = default);

    Task<T> RunTurnAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken = default) =>
        work(cancellationToken);

    /// <summary>Displays accumulated token usage and cost.</summary>
    void ShowTokens(string model, TokenUsage session, TokenUsage last, decimal cost);

    /// <summary>Clears the screen.</summary>
    void Clear();
}

public sealed record SessionMenuItem(string Label, string Description, string Value);
