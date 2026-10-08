using System.Text;
using Codai.Core.Abstractions;
using Codai.Core.Models;

namespace Codai.Session.Tests.Fakes;

/// <summary>
/// Scripted <see cref="IUserInterface"/>: inputs and confirmations are queued up
/// front and every write is captured for assertions.
/// </summary>
public sealed class FakeUserInterface : IUserInterface
{
    private readonly Queue<string?> _inputs = new();
    private readonly Queue<bool> _confirmations = new();

    public List<string> Lines { get; } = [];

    public List<string> Errors { get; } = [];

    public List<string> Successes { get; } = [];

    public List<string> Infos { get; } = [];

    public StringBuilder AssistantOutput { get; } = new();

    public List<string> ProgressLabels { get; } = [];

    public List<string> Prompts { get; } = [];

    public List<(string Provider, string Model, TokenUsage Session, TokenUsage Last, decimal Cost)> TokenDisplays { get; } = [];

    public int ClearCount { get; private set; }

    public List<string> ReadRequests { get; } = [];

    public void EnqueueInput(string? input) => _inputs.Enqueue(input);

    public void EnqueueConfirmation(bool accepted) => _confirmations.Enqueue(accepted);

    public Task<string?> ReadInputAsync(CancellationToken cancellationToken = default)
    {
        ReadRequests.Add("read");
        return Task.FromResult(_inputs.Count > 0 ? _inputs.Dequeue() : null);
    }

    public Task<bool> ConfirmAsync(string question, CancellationToken cancellationToken = default)
    {
        Prompts.Add(question);
        return Task.FromResult(_confirmations.Count > 0 && _confirmations.Dequeue());
    }

    public void WriteLine(string message) => Lines.Add(message);

    public void WriteError(string message) => Errors.Add(message);

    public void WriteSuccess(string message) => Successes.Add(message);

    public void WriteInfo(string message) => Infos.Add(message);

    public void WriteMarkdown(string markdown) => AssistantOutput.Append(markdown);

    public Task<T> RunWithProgressAsync<T>(
        string label,
        Func<CancellationToken, Task<T>> work,
        CancellationToken cancellationToken = default)
    {
        ProgressLabels.Add(label);
        return work(cancellationToken);
    }

    public void ShowTokens(string provider, string model, TokenUsage session, TokenUsage last, decimal cost) =>
        TokenDisplays.Add((provider, model, session, last, cost));

    public void Clear() => ClearCount++;
}
