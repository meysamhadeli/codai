namespace Codai.Cli;

/// <summary>
/// Small console abstraction so the application can be exercised without writing to
/// the real terminal.
/// </summary>
public interface IConsoleOutput
{
    void Write(string message) => WriteLine(message);

    void WriteLine(string message);

    void WriteError(string message);
}

/// <summary>Writes straight to <see cref="Console"/>.</summary>
public sealed class SystemConsoleOutput : IConsoleOutput
{
    public static SystemConsoleOutput Instance { get; } = new();

    public void Write(string message) => Console.Write(message);

    /// <inheritdoc />
    public void WriteLine(string message) => Console.WriteLine(message);

    /// <inheritdoc />
    public void WriteError(string message)
    {
        var previous = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine(message);
        Console.ForegroundColor = previous;
    }
}
