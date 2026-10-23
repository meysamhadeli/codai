using Codai.Core.Abstractions;
using Codai.Core.Agents;
using Codai.Core.Tokens;

namespace Codai.Application.Acp;

internal sealed class AcpJsonLinesUserInterface(
    Action<object> emit,
    Func<string, CancellationToken, Task<bool>> confirm) : IUserInterface, IAgentProgressSink
{
    public Task<string?> ReadInputAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);

    public Task<bool> ConfirmAsync(string question, CancellationToken cancellationToken = default) =>
        confirm(question, cancellationToken);

    public void WriteLine(string message) => Emit("notice", message);
    public void WriteError(string message) => Emit("error", message);
    public void WriteSuccess(string message) => Emit("success", message);
    public void WriteInfo(string message) => Emit("info", message);
    public void WriteMarkdown(string markdown) => Emit("text", markdown);

    public Task<T> RunWithProgressAsync<T>(string label, Func<CancellationToken, Task<T>> work,
        CancellationToken cancellationToken = default)
    {
        Emit("progress", label);
        return work(cancellationToken);
    }

    public void ShowTokens(string model, TokenUsage session, TokenUsage last, decimal cost) =>
        emit(new { type = "usage", model, session, last, cost });

    public void Clear() => Emit("clear", null);

    public ValueTask PublishAsync(AgentProgressEvent progress, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (progress.Kind is AgentProgressEventKind.CommandStandardOutput or AgentProgressEventKind.CommandStandardError)
        {
            emit(new
            {
                type = "terminal_output",
                stream = progress.Kind == AgentProgressEventKind.CommandStandardError ? "stderr" : "stdout",
                taskId = progress.TaskId,
                text = progress.Message,
                occurredAt = progress.OccurredAt,
            });
            return ValueTask.CompletedTask;
        }

        emit(new
        {
            type = "progress",
            taskId = progress.TaskId,
            description = progress.Description,
            kind = progress.Kind.ToString(),
            message = progress.Message,
            occurredAt = progress.OccurredAt,
        });
        return ValueTask.CompletedTask;
    }

    private void Emit(string type, string? text) => emit(new { type, text });
}