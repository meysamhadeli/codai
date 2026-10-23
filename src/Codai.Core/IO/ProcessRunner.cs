using System.Diagnostics;
using System.Text;
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
    public Task<ProcessResult> RunAsync(
        string commandLine,
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken = default) =>
        RunCoreAsync(commandLine, workingDirectory, null, timeout, null, cancellationToken);

    /// <inheritdoc />
    public Task<ProcessResult> RunStreamingAsync(string commandLine, string workingDirectory, TimeSpan timeout,
        Action<ProcessOutput> onOutput, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(onOutput);
        return RunCoreAsync(commandLine, workingDirectory, null, timeout, onOutput, cancellationToken);
    }

    /// <inheritdoc />
    public Task<ProcessResult> RunWithInputAsync(string commandLine, string workingDirectory, string standardInput,
        TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(standardInput);
        return RunCoreAsync(commandLine, workingDirectory, standardInput, timeout, null, cancellationToken);
    }

    private async Task<ProcessResult> RunCoreAsync(string commandLine, string workingDirectory, string? standardInput,
        TimeSpan timeout, Action<ProcessOutput>? onOutput, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandLine);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        cancellationToken.ThrowIfCancellationRequested();
        using var timeoutSource = new CancellationTokenSource(timeout);
        using var observerFailure = new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token, observerFailure.Token);
        var observerGate = new object();
        Exception? observerError = null;

        void Publish(ProcessOutput output)
        {
            lock (observerGate)
            {
                if (observerError is not null)
                {
                    return;
                }

                try
                {
                    onOutput?.Invoke(output);
                }
                catch (Exception exception)
                {
                    observerError = exception;
                    observerFailure.Cancel();
                }
            }
        }

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
        var standardOutput = ReadPipeAsync(process.StandardOutput, false, onOutput is null ? null : Publish, linked.Token);
        var standardError = ReadPipeAsync(process.StandardError, true, onOutput is null ? null : Publish, linked.Token);

        var timedOut = false;

        try
        {
            try
            {
                if (standardInput is not null)
                {
                    await process.StandardInput.WriteAsync(standardInput.AsMemory(), linked.Token).ConfigureAwait(false);
                    await process.StandardInput.FlushAsync(linked.Token).ConfigureAwait(false);
                }

                process.StandardInput.Close();
            }
            catch (IOException)
            {
            }

            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await KillAsync(process).ConfigureAwait(false);
            await SafeAwait(standardOutput).ConfigureAwait(false);
            await SafeAwait(standardError).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (observerError is null && !timeoutSource.IsCancellationRequested)
            {
                throw;
            }

            timedOut = timeoutSource.IsCancellationRequested;
        }

        var output = await SafeAwait(standardOutput).ConfigureAwait(false);
        var error = await SafeAwait(standardError).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (observerError is not null)
        {
            throw new IOException("Command output observer failed.", observerError);
        }

        if (timeoutSource.IsCancellationRequested && !timedOut)
        {
            timedOut = true;
            await KillAsync(process).ConfigureAwait(false);
        }

        return new ProcessResult(
            timedOut ? -1 : process.ExitCode,
            output,
            error,
            timedOut);
    }

    private static ProcessStartInfo CreateStartInfo(string commandLine, string workingDirectory)
    {
        var info = new ProcessStartInfo
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardInputEncoding = new UTF8Encoding(false),
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

    private async Task<string> ReadPipeAsync(StreamReader reader, bool isStandardError,
        Action<ProcessOutput>? onOutput, CancellationToken cancellationToken)
    {
        var captured = new StringBuilder();
        var buffer = new char[4096];
        var truncated = false;
        while (!cancellationToken.IsCancellationRequested)
        {
            int count;
            try
            {
                count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (count == 0)
            {
                break;
            }

            var retained = Math.Min(count, _maxOutputChars - captured.Length);
            captured.Append(buffer, 0, retained);
            if (retained > 0)
            {
                onOutput?.Invoke(new ProcessOutput(new string(buffer, 0, retained), isStandardError));
            }

            if (retained < count && !truncated)
            {
                truncated = true;
                onOutput?.Invoke(new ProcessOutput($"\n\n[output truncated after {_maxOutputChars} characters]", isStandardError));
            }
        }

        if (truncated)
        {
            captured.Append($"\n\n[output truncated after {_maxOutputChars} characters]");
        }

        return captured.ToString();
    }
}
