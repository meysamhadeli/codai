namespace Codai.Core.Abstractions;

public interface IClarificationHandler
{
    Task<string?> AskAsync(string question, IReadOnlyList<string> options, CancellationToken cancellationToken = default);
}

public sealed class UnavailableClarificationHandler : IClarificationHandler
{
    public static UnavailableClarificationHandler Instance { get; } = new();

    private UnavailableClarificationHandler() { }

    public Task<string?> AskAsync(string question, IReadOnlyList<string> options, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<string?>(null);
    }
}
