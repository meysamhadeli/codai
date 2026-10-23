using Codai.Core.Abstractions;
using Codai.Core.IO;
using FluentAssertions;

namespace Codai.Core.Tests;

/// <summary>
/// Covers the process runner against real commands. These tests start a shell, so they
/// stay on commands that exist on every platform the runner supports.
/// </summary>
public class ProcessRunnerTests
{
    [Fact]
    public async Task Live_output_arrives_before_exit_and_cancellation_still_stops_command()
    {
        var seen = new TaskCompletionSource<ProcessOutput>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        var separator = OperatingSystem.IsWindows() ? " & " : "; ";
        var run = new ProcessRunner().RunStreamingAsync("echo live-marker" + separator + Slow,
            WorkingDirectory, TimeSpan.FromSeconds(30), output => seen.TrySetResult(output), cancellation.Token);

        var output = await seen.Task.WaitAsync(TimeSpan.FromSeconds(5));
        output.Text.Should().Contain("live-marker");
        output.IsStandardError.Should().BeFalse();
        run.IsCompleted.Should().BeFalse();

        await cancellation.CancelAsync();
        var action = async () => await run;
        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Live_output_preserves_streams_and_is_bounded_like_captured_output()
    {
        var chunks = new List<ProcessOutput>();
        var separator = OperatingSystem.IsWindows() ? " & " : "; ";
        var result = await new ProcessRunner(maxOutputChars: 16).RunStreamingAsync(
            "echo aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" + separator + "echo bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb 1>&2",
            WorkingDirectory, TimeSpan.FromSeconds(5), chunks.Add);

        result.Succeeded.Should().BeTrue();
        string.Concat(chunks.Where(chunk => !chunk.IsStandardError).Select(chunk => chunk.Text))
            .Should().Be(result.StandardOutput);
        string.Concat(chunks.Where(chunk => chunk.IsStandardError).Select(chunk => chunk.Text))
            .Should().Be(result.StandardError);
        result.StandardOutput.Should().Contain("[output truncated after 16 characters]");
        result.StandardError.Should().Contain("[output truncated after 16 characters]");
        chunks.Sum(chunk => chunk.Text.Length).Should().BeLessThan(160);
    }

    [Fact]
    public async Task Live_output_timeout_retains_partial_capture_and_stops_draining()
    {
        var chunks = new List<ProcessOutput>();
        var separator = OperatingSystem.IsWindows() ? " & " : "; ";
        var result = await new ProcessRunner().RunStreamingAsync("echo before-timeout" + separator + Slow,
            WorkingDirectory, TimeSpan.FromMilliseconds(500), chunks.Add);

        result.TimedOut.Should().BeTrue();
        result.StandardOutput.Should().Contain("before-timeout");
        string.Concat(chunks.Where(chunk => !chunk.IsStandardError).Select(chunk => chunk.Text))
            .Should().Be(result.StandardOutput);
    }

    [Fact]
    public async Task Live_output_observer_failure_stops_owned_command_and_surfaces_error()
    {
        var separator = OperatingSystem.IsWindows() ? " & " : "; ";
        var started = DateTimeOffset.UtcNow;
        var action = () => new ProcessRunner().RunStreamingAsync("echo live-marker" + separator + Slow,
            WorkingDirectory, TimeSpan.FromSeconds(30), _ => throw new InvalidOperationException("observer failed"));

        var failure = await action.Should().ThrowAsync<IOException>().WithMessage("Command output observer failed.");
        failure.WithInnerException<InvalidOperationException>();
        (DateTimeOffset.UtcNow - started).Should().BeLessThan(TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task Explicit_stdin_is_delivered_and_closed_for_child()
    {
        var command = OperatingSystem.IsWindows() ? "findstr .*" : "cat";
        var result = await new ProcessRunner().RunWithInputAsync(command, WorkingDirectory,
            "hook-input-one\nhook-input-two\n", TimeSpan.FromSeconds(5));

        result.Succeeded.Should().BeTrue();
        result.StandardOutput.Should().Contain("hook-input-one").And.Contain("hook-input-two");
    }

    [Fact]
    public async Task Normal_commands_receive_eof_instead_of_session_stdin()
    {
        var command = OperatingSystem.IsWindows() ? "findstr .*" : "cat";
        var result = await new ProcessRunner().RunAsync(command, WorkingDirectory, TimeSpan.FromSeconds(5));

        result.TimedOut.Should().BeFalse();
        result.StandardOutput.Should().BeEmpty();
    }

    [Fact]
    public async Task Timeout_kills_child_that_never_reads_large_stdin_payload()
    {
        var started = DateTimeOffset.UtcNow;
        var result = await new ProcessRunner().RunWithInputAsync(Slow, WorkingDirectory,
            new string('x', 1_000_000), TimeSpan.FromMilliseconds(300));

        result.TimedOut.Should().BeTrue();
        (DateTimeOffset.UtcNow - started).Should().BeLessThan(TimeSpan.FromSeconds(15));
    }

    private static string WorkingDirectory => Path.GetTempPath();

    /// <summary>A command that finishes immediately with the given exit code.</summary>
    private static string Exit(int code) => $"exit {code}";

    /// <summary>A command that takes noticeably longer than the timeout used here.</summary>
    private static string Slow =>
        OperatingSystem.IsWindows() ? "ping -n 6 127.0.0.1 > nul" : "sleep 5";

    [Fact]
    public async Task Captures_the_output_and_a_zero_exit_code()
    {
        var result = await new ProcessRunner()
            .RunAsync("echo hello-runner", WorkingDirectory, TimeSpan.FromSeconds(30));

        result.Succeeded.Should().BeTrue();
        result.ExitCode.Should().Be(0);
        result.StandardOutput.Should().Contain("hello-runner");
        result.TimedOut.Should().BeFalse();
    }

    [Fact]
    public async Task Reports_a_failing_exit_code_without_throwing()
    {
        var result = await new ProcessRunner()
            .RunAsync(Exit(3), WorkingDirectory, TimeSpan.FromSeconds(30));

        result.Succeeded.Should().BeFalse();
        result.ExitCode.Should().Be(3);
        result.TimedOut.Should().BeFalse();
    }

    [Fact]
    public async Task Captures_standard_error_separately_from_standard_output()
    {
        var result = await new ProcessRunner()
            .RunAsync("echo to-stderr 1>&2", WorkingDirectory, TimeSpan.FromSeconds(30));

        result.StandardError.Should().Contain("to-stderr");
        result.StandardOutput.Should().NotContain("to-stderr");
    }

    [Fact]
    public async Task Kills_a_command_that_outlives_its_timeout()
    {
        var result = await new ProcessRunner()
            .RunAsync(Slow, WorkingDirectory, TimeSpan.FromMilliseconds(300));

        result.TimedOut.Should().BeTrue();
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task A_command_that_was_killed_does_not_hang_the_caller()
    {
        // The point of killing the whole tree is that the runner still returns promptly
        // rather than waiting for a child that ignored the request.
        var started = DateTimeOffset.UtcNow;

        await new ProcessRunner().RunAsync(Slow, WorkingDirectory, TimeSpan.FromMilliseconds(300));

        (DateTimeOffset.UtcNow - started).Should().BeLessThan(TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task Truncates_output_rather_than_flooding_the_context()
    {
        var result = await new ProcessRunner(maxOutputChars: 20)
            .RunAsync("echo aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", WorkingDirectory, TimeSpan.FromSeconds(30));

        result.StandardOutput.Should().Contain("[output truncated after 20 characters]");
        result.StandardOutput.Length.Should().BeLessThan(100);
    }

    [Fact]
    public async Task A_command_with_no_output_reports_empty_strings()
    {
        var result = await new ProcessRunner()
            .RunAsync(Exit(0), WorkingDirectory, TimeSpan.FromSeconds(30));

        result.StandardOutput.Should().BeEmpty();
        result.StandardError.Should().BeEmpty();
    }

    [Fact]
    public async Task Refuses_to_start_an_empty_command_line()
    {
        var act = async () => await new ProcessRunner()
            .RunAsync("   ", WorkingDirectory, TimeSpan.FromSeconds(5));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Cancellation_propagates_rather_than_being_reported_as_a_timeout()
    {
        using var cancellation = new CancellationTokenSource();
        var started = DateTimeOffset.UtcNow;

        var run = new ProcessRunner()
            .RunAsync(Slow, WorkingDirectory, TimeSpan.FromSeconds(30), cancellation.Token);
        await cancellation.CancelAsync();

        Func<Task> act = async () => await run;
        await act.Should().ThrowAsync<OperationCanceledException>();
        (DateTimeOffset.UtcNow - started).Should().BeLessThan(TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task Cancellation_kills_the_command_before_delayed_work_runs()
    {
        var marker = Path.Combine(Path.GetTempPath(), $"codai-process-{Guid.NewGuid():N}.txt");
        var quotedMarker = OperatingSystem.IsWindows()
            ? $"\"{marker}\""
            : $"'{marker.Replace("'", "'\\''", StringComparison.Ordinal)}'";
        var command = OperatingSystem.IsWindows()
            ? $"ping -n 4 127.0.0.1 > nul & echo orphan > {quotedMarker}"
            : $"sleep 2; echo orphan > {quotedMarker}";

        try
        {
            using var cancellation = new CancellationTokenSource();
            var run = new ProcessRunner()
                .RunAsync(command, WorkingDirectory, TimeSpan.FromSeconds(30), cancellation.Token);

            await Task.Delay(100);
            await cancellation.CancelAsync();
            Func<Task> act = async () => await run;
            await act.Should().ThrowAsync<OperationCanceledException>();

            await Task.Delay(TimeSpan.FromSeconds(3));
            File.Exists(marker).Should().BeFalse();
        }
        finally
        {
            File.Delete(marker);
        }
    }
}
