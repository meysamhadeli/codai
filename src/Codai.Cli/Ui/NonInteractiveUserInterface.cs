using Codai.Core.Abstractions;
using Codai.Core.Tokens;

namespace Codai.Cli.Ui;

internal sealed class NonInteractiveUserInterface(IConsoleOutput output) : IUserInterface
{
    public Task<string?> ReadInputAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<string?>(null);
    }

    public Task<bool> ConfirmAsync(string question, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        output.WriteError($"Approval unavailable in non-interactive mode: {question}");
        return Task.FromResult(false);
    }

    public void WriteLine(string message) => output.WriteError(message);

    public void WriteError(string message) => output.WriteError(message);

    public void WriteSuccess(string message) => output.WriteError(message);

    public void WriteInfo(string message) => output.WriteError(message);

    public void WriteMarkdown(string markdown) => output.Write(markdown);

    public Task<T> RunWithProgressAsync<T>(
        string label,
        Func<CancellationToken, Task<T>> work,
        CancellationToken cancellationToken = default) => work(cancellationToken);

    public void ShowTokens(string model, TokenUsage session, TokenUsage last, decimal cost) =>
        output.WriteError(FormattableString.Invariant($"Usage ({model}): {last.InputTokens} input, {last.OutputTokens} output; session cost {cost:C}."));

    public void Clear()
    {
    }
}
