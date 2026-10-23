namespace Codai.Core.Abstractions;

/// <summary>A bounded command-output chunk with its source stream.</summary>
public readonly record struct ProcessOutput(string Text, bool IsStandardError);

/// <summary>
/// What a finished command produced.
/// </summary>
/// <param name="ExitCode">Process exit code, or <c>-1</c> when it was killed.</param>
/// <param name="StandardOutput">Captured standard output, possibly truncated.</param>
/// <param name="StandardError">Captured standard error, possibly truncated.</param>
/// <param name="TimedOut">True when the process was killed for exceeding its timeout.</param>
public readonly record struct ProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut)
{
    /// <summary>A command that finished with exit code 0.</summary>
    public bool Succeeded => ExitCode == 0 && !TimedOut;
}
