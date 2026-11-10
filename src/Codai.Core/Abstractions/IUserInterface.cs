
using System.Text.Json;
using Codai.Core.Chat;
using Codai.Core.Tokens;
using Codai.Core.Tools;
namespace Codai.Core.Abstractions;

/// <summary>
/// Presentation surface used by the interactive session. Implemented by the CLI
/// (Spectre.Console) and by a test double in unit tests.
/// </summary>
public interface IUserInterface
{
    /// <summary>Starts request-scoped model activity without exposing reasoning content.</summary>
    void ModelRequestStarted(Guid requestId) { }

    /// <summary>Reports streamed output, reasoning signals, and provider token usage for a request.</summary>
    void ModelRequestUpdated(Guid requestId, ChatStreamUpdate update) { }

    /// <summary>Ends request-scoped activity, including failed or cancelled requests.</summary>
    void ModelRequestCompleted(Guid requestId) { }

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

    /// <summary>Shows a searchable, grouped picker with optional keyboard actions.</summary>
    Task<string?> ChooseAsync(string title, IReadOnlyList<SessionMenuItem> items, SessionMenuOptions options, CancellationToken cancellationToken = default) =>
        ChooseAsync(title, items, cancellationToken);

    /// <summary>Shows working-tree and staged changes without modifying repository state.</summary>
    Task ShowDiffAsync(string workingTree, string staged, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        WriteLine("Working-tree changes");
        WriteLine(string.IsNullOrWhiteSpace(workingTree) ? "No diff!" : workingTree);
        WriteLine("Staged changes");
        WriteLine(string.IsNullOrWhiteSpace(staged) ? "No diff!" : staged);
        return Task.CompletedTask;
    }

    Task<string?> ReadValueAsync(string title, string initialValue = "", bool secret = false, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);

    void SetTheme(string name) { }

    /// <summary>Updates discovered skill names offered by slash-command completion, including unchecked skills.</summary>
    void SetSkills(IReadOnlyList<string> skillNames) { }

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

/// <summary>A selectable session row with optional grouping and state indicators.</summary>
public sealed record SessionMenuItem(string Label, string Description, string Value)
{
    /// <summary>Gets the nonselectable heading displayed before this group.</summary>
    public string Group { get; init; } = string.Empty;
    /// <summary>Gets the short tag aligned to the trailing edge of the row.</summary>
    public string RightLabel { get; init; } = string.Empty;
    /// <summary>Gets whether this row represents the currently active value.</summary>
    public bool IsCurrent { get; init; }
}

/// <summary>Presentation and keyboard behavior of a searchable session picker.</summary>
public sealed record SessionMenuOptions
{
    /// <summary>Gets whether left/right adjusts a single value instead of searching a list.</summary>
    public bool HorizontalSelection { get; init; }
    /// <summary>Gets explanatory text displayed above a horizontal value selector.</summary>
    public string Description { get; init; } = string.Empty;
    /// <summary>Gets the prompt shown in an empty search field.</summary>
    public string Placeholder { get; init; } = "Search";
    /// <summary>Gets the message shown when no rows match.</summary>
    public string EmptyText { get; init; } = "No results found";
    /// <summary>Gets an optional host hint; terminal palettes show only the standard Enter/Escape footer.</summary>
    public string Footer { get; init; } = string.Empty;
    /// <summary>Gets the value of the initially highlighted row.</summary>
    public string? InitialValue { get; init; }
    /// <summary>Gets the maximum desired width in terminal cells.</summary>
    public int Width { get; init; } = 76;
    /// <summary>Gets whether Space selects a row instead of adding a search character.</summary>
    public bool ToggleOnSpace { get; init; }
    /// <summary>Gets whether Enter toggles multiple rows and Escape returns their values as a JSON string array.</summary>
    public bool MultiSelect { get; init; }
    /// <summary>Gets Control-key actions supported by this picker.</summary>
    public IReadOnlyList<SessionMenuShortcut> Shortcuts { get; init; } = [];
}

/// <summary>A Control-key action that can include the highlighted item's value.</summary>
/// <param name="Key">The key pressed with Control.</param>
/// <param name="Result">Returned action identifier or prefix.</param>
/// <param name="IncludeSelection">Whether to append the highlighted row's value.</param>
public sealed record SessionMenuShortcut(ConsoleKey Key, string Result, bool IncludeSelection = false);
