using System.Diagnostics;
using Codai.Core.Abstractions;

namespace Codai.Core.IO;

/// <summary>
/// Runs command lines in a shell, with a timeout and bounded output.
/// </summary>
/// <remarks>
/// <para>
/// This is the only place in Codai that starts a process. Two properties matter for
/// safety and are therefore enforced here rather than left to callers:
/// </para>
/// <list type="bullet">
/// <item><b>Standard input is never redirected to the parent's.</b> A command that waits
/// for input must not be able to consume keystrokes the user meant for the session.</item>
/// <item><b>Output is bounded.</b> A command that prints indefinitely is killed by the
/// timeout, but a command that prints a hundred megabytes and then exits is not, so the
/// captured text is capped before it reaches the context window.</item>
/// </list>
/// <para>
/// There is no sandbox here: a command runs with the user's own privileges. That is why
/// the tool built on this runner is opt-in and always asks for approval, and why this
/// type does not try to guess which command lines are acceptable.
/// </para>
/// </remarks>
public sealed class ProcessRunner : IProcessRunner
{
    /// <summary>Characters of standard output and error kept, each.</summary>
    public const int DefaultMaxOutputChars = 20_000;

    /// <summary>How long a killed process is given to exit before being abandoned.</summary>
    private static readonly TimeSpan GracePeriod = TimeSpan.FromSeconds(5);

    private readonly int _maxOutputChars;

    /// <summary>Creates a runner.</summary>
    public ProcessRunner(int maxOutputChars = DefaultMaxOutputChars)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxOutputChars);
        _maxOutputChars = maxOutputChars;
    }

    /// <inheritdoc />
    public async Task<ProcessResult> RunAsync(
        string commandLine,
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandLine);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        using var process = new Process
        {
            StartInfo = CreateStartInfo(commandLine, workingDirectory),
        };

        if (!process.Start())
        {
            return new ProcessResult(-1, string.Empty, "The process could not be started.", false);
        }

        // Read both pipes concurrently: a process that fills one while the parent waits on
        // the other deadlocks.
        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);

        using var timeoutSource = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutSource.Token);

        var timedOut = false;

        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
        {
            timedOut = true;
            await KillAsync(process).ConfigureAwait(false);
        }

        // The caller's own cancellation is not a result: it means the session is ending.
        cancellationToken.ThrowIfCancellationRequested();

        return new ProcessResult(
            timedOut ? -1 : process.ExitCode,
            Cap(await SafeAwait(standardOutput).ConfigureAwait(false)),
            Cap(await SafeAwait(standardError).ConfigureAwait(false)),
            timedOut);
    }

    private static ProcessStartInfo CreateStartInfo(string commandLine, string workingDirectory)
    {
        var info = new ProcessStartInfo
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (OperatingSystem.IsWindows())
        {
            info.FileName = "cmd.exe";
            info.Arguments = $"/d /s /c \"{commandLine}\"";
        }
        else
        {
            info.FileName = "/bin/sh";
            info.Arguments = $"-c \"{commandLine.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
        }

        return info;
    }

    private static async Task KillAsync(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            // The process exited between the timeout and the kill.
            return;
        }

        try
        {
            using var grace = new CancellationTokenSource(GracePeriod);
            await process.WaitForExitAsync(grace.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Abandoned; the process tree was already asked to stop.
        }
    }

    /// <summary>
    /// Waits for a pipe read that was still in flight when the process was killed. A read
    /// on a killed process can throw, and the output collected so far is worth more than
    /// the exception.
    /// </summary>
    private static async Task<string> SafeAwait(Task<string> read)
    {
        try
        {
            return await read.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
        {
            return string.Empty;
        }
    }

    private string Cap(string text)
    {
        if (text.Length <= _maxOutputChars)
        {
            return text;
        }

        return string.Concat(
            text.AsSpan(0, _maxOutputChars),
            $"\n\n[output truncated after {_maxOutputChars} characters]");
    }
}
