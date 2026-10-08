namespace Codai.Cli;

/// <summary>
/// Small console-input abstraction so the interactive loop can be exercised without
/// reading from the real terminal.
/// </summary>
public interface IConsoleInput
{
    /// <summary>
    /// Reads one line, or returns <c>null</c> at end of input (Ctrl+D, closed stdin).
    /// </summary>
    string? ReadLine();
}

/// <summary>Reads from the process's standard input.</summary>
public sealed class SystemConsoleInput : IConsoleInput
{
    /// <summary>The shared instance.</summary>
    public static SystemConsoleInput Instance { get; } = new();

    /// <inheritdoc />
    public string? ReadLine() => Console.ReadLine();
}
