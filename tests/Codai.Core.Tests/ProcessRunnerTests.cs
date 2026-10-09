using Codai.Core.IO;
using FluentAssertions;

namespace Codai.Core.Tests;

/// <summary>
/// Covers the process runner against real commands. These tests start a shell, so they
/// stay on commands that exist on every platform the runner supports.
/// </summary>
public class ProcessRunnerTests
{
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
        await cancellation.CancelAsync();

        var act = async () => await new ProcessRunner()
            .RunAsync(Slow, WorkingDirectory, TimeSpan.FromSeconds(30), cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
