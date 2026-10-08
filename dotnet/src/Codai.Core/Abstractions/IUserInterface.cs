using Codai.Core.Models;

namespace Codai.Core.Abstractions;

/// <summary>
/// Presentation surface used by the interactive session. Implemented by the CLI
/// (Spectre.Console) and by a test double in unit tests.
/// </summary>
public interface IUserInterface
{
    /// <summary>Reads one line of user input, or <c>null</c> at end of input.</summary>
    Task<string?> ReadInputAsync(CancellationToken cancellationToken = default);

    /// <summary>Asks a yes/no question.</summary>
    Task<bool> ConfirmAsync(string question, CancellationToken cancellationToken = default);

    void WriteLine(string message);

    void WriteError(string message);

    void WriteSuccess(string message);

    void WriteInfo(string message);

    /// <summary>Renders a streamed markdown chunk (code fences toggle syntax colouring).</summary>
    void WriteMarkdown(string markdown);

    /// <summary>Runs <paramref name="work"/> while displaying a spinner.</summary>
    Task<T> RunWithProgressAsync<T>(
        string label,
        Func<CancellationToken, Task<T>> work,
        CancellationToken cancellationToken = default);

    /// <summary>Displays accumulated token usage and cost.</summary>
    void ShowTokens(string provider, string model, TokenUsage session, TokenUsage last, decimal cost);

    /// <summary>Clears the screen.</summary>
    void Clear();
}
