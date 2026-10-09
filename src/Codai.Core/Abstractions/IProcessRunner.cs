namespace Codai.Core.Abstractions;

/// <summary>
/// Runs a command line and captures its output.
/// </summary>
/// <remarks>
/// Abstracted so that the tool which exposes command execution can be tested without
/// spawning processes. The interface takes a whole command line rather than a program
/// and an argument list because the decision of which shell to hand it to differs per
/// platform, and that decision belongs in one implementation rather than in the tool.
/// </remarks>
public interface IProcessRunner
{
    /// <summary>
    /// Runs <paramref name="commandLine"/> with <paramref name="workingDirectory"/> as the
    /// current directory.
    /// </summary>
    /// <param name="commandLine">The command line, interpreted by the platform's shell.</param>
    /// <param name="workingDirectory">Directory the command runs in.</param>
    /// <param name="timeout">
    /// How long to wait before killing the process. A killed process is reported as
    /// <see cref="ProcessResult.TimedOut"/> rather than as a failure, because the output
    /// it produced before being killed is still useful.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the wait and the command. Cancellation is not a timeout: it propagates as
    /// an <see cref="OperationCanceledException"/>, because it means the session is ending.
    /// </param>
    Task<ProcessResult> RunAsync(
        string commandLine,
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}
