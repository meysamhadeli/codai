using System.Text.Json;
using Codai.Core.Abstractions;
using Codai.Core.Tokens;
using Codai.Core.Tools;

namespace Codai.Cli.Ui;

public sealed class ApprovalState(bool autoApprove = false)
{
    public bool AutoApprove { get; set; } = autoApprove;
}

internal sealed class AutoApprovingUserInterface(IUserInterface inner, ApprovalState state) : IUserInterface
{
    public Task<string?> ReadInputAsync(CancellationToken cancellationToken = default) =>
        inner.ReadInputAsync(cancellationToken);

    public Task<string?> AskQuestionAsync(string question, IReadOnlyList<string> options, CancellationToken cancellationToken = default) =>
        inner.AskQuestionAsync(question, options, cancellationToken);

    public Task<string?> ChooseAsync(string title, IReadOnlyList<SessionMenuItem> items, CancellationToken cancellationToken = default) =>
        inner.ChooseAsync(title, items, cancellationToken);

    public Task<string?> ReadValueAsync(string title, string initialValue = "", bool secret = false, CancellationToken cancellationToken = default) =>
        inner.ReadValueAsync(title, initialValue, secret, cancellationToken);

    public void SetTheme(string name) => inner.SetTheme(name);

    public void InsertInput(string text) => inner.InsertInput(text);

    public Task<bool> ConfirmAsync(string question, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!state.AutoApprove)
        {
            return inner.ConfirmAsync(question, cancellationToken);
        }

        inner.WriteInfo($"Auto-approved: {question}");
        return Task.FromResult(true);
    }

    public Task<ApprovalDecision> ApproveAsync(ToolApprovalRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!state.AutoApprove)
        {
            return inner.ApproveAsync(request, cancellationToken);
        }

        inner.WriteInfo($"Auto-approved: {request.ToolName}: {request.PreviewOrDefault}");
        return Task.FromResult(ApprovalDecision.AllowOnce);
    }

    public void WriteLine(string message) => inner.WriteLine(message);

    public void WriteError(string message) => inner.WriteError(message);

    public void WriteSuccess(string message) => inner.WriteSuccess(message);

    public void WriteInfo(string message) => inner.WriteInfo(message);

    public void WriteToolStarted(string toolName, string summary) => inner.WriteToolStarted(toolName, summary);

    public void WriteToolStarted(string toolName, string summary, JsonElement arguments) => inner.WriteToolStarted(toolName, summary, arguments);

    public void WriteToolCompleted(string toolName, bool succeeded, string detail) => inner.WriteToolCompleted(toolName, succeeded, detail);

    public void WriteToolDenied(string toolName, string summary) => inner.WriteToolDenied(toolName, summary);

    public void WriteToolOutput(string toolName, ProcessOutput output) => inner.WriteToolOutput(toolName, output);

    public void WriteToolResult(string toolName, JsonElement arguments, ToolResult result) => inner.WriteToolResult(toolName, arguments, result);

    public void WriteMarkdown(string markdown) => inner.WriteMarkdown(markdown);

    public Task<T> RunWithProgressAsync<T>(string label, Func<CancellationToken, Task<T>> work,
        CancellationToken cancellationToken = default) => inner.RunWithProgressAsync(label, work, cancellationToken);

    public Task<T> RunTurnAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken = default) =>
        inner.RunTurnAsync(work, cancellationToken);

    public void ShowTokens(string model, TokenUsage session, TokenUsage last, decimal cost) =>
        inner.ShowTokens(model, session, last, cost);

    public void Clear() => inner.Clear();
}
