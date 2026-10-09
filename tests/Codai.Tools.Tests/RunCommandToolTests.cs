using Codai.Core.Abstractions;
using Codai.Core.Tools;
using FluentAssertions;

namespace Codai.Tools.Tests;

/// <summary>
/// Covers <c>run_command</c> against a scripted runner. The runner itself is tested with
/// real processes; what matters here is what the tool tells the model.
/// </summary>
public class RunCommandToolTests
{
    private static ToolContext Context(IFileSystem fileSystem) => ToolTestHost.Context(fileSystem);

    private static IFileSystem FileSystem() => ToolTestHost.NewProject();

    [Fact]
    public void Running_a_command_is_a_mutating_operation()
    {
        // The whole approval gate depends on this: a tool that reports itself as
        // read-only would run without ever asking.
        new RunCommandTool(new StubProcessRunner()).IsReadOnly.Should().BeFalse();
    }

    [Fact]
    public async Task Runs_the_command_from_the_project_root()
    {
        var runner = new StubProcessRunner();
        var fileSystem = FileSystem();

        await new RunCommandTool(runner)
            .InvokeAsync(ToolTestHost.Args("""{"command":"dotnet build"}"""), Context(fileSystem));

        runner.CommandLine.Should().Be("dotnet build");
        runner.WorkingDirectory.Should().Be(ToolTestHost.Root);
    }

    [Fact]
    public async Task Reports_the_exit_code_and_both_streams()
    {
        var runner = new StubProcessRunner
        {
            Result = new ProcessResult(0, "build output", "a warning", false),
        };

        var result = await new RunCommandTool(runner)
            .InvokeAsync(ToolTestHost.Args("""{"command":"dotnet build"}"""), Context(FileSystem()));

        result.IsSuccess.Should().BeTrue();
        result.Content.Should().Contain("$ dotnet build");
        result.Content.Should().Contain("Exit code 0 (success)");
        result.Content.Should().Contain("stdout:");
        result.Content.Should().Contain("build output");
        result.Content.Should().Contain("stderr:");
        result.Content.Should().Contain("a warning");
    }

    [Fact]
    public async Task A_non_zero_exit_code_is_reported_as_a_failure_of_the_command_not_the_tool()
    {
        // The tool itself worked, so it is a success for the model; the command failed,
        // and saying so is what stops the model from assuming the build passed.
        var runner = new StubProcessRunner
        {
            Result = new ProcessResult(1, string.Empty, "error CS1002", false),
        };

        var result = await new RunCommandTool(runner)
            .InvokeAsync(ToolTestHost.Args("""{"command":"dotnet build"}"""), Context(FileSystem()));

        result.IsSuccess.Should().BeTrue();
        result.Content.Should().Contain("Exit code 1 (failure)");
        result.Content.Should().Contain("error CS1002");
    }

    [Fact]
    public async Task A_timeout_is_reported_as_a_timeout_and_not_as_an_exit_code()
    {
        var runner = new StubProcessRunner
        {
            Result = new ProcessResult(-1, string.Empty, string.Empty, true),
        };

        var result = await new RunCommandTool(runner)
            .InvokeAsync(
                ToolTestHost.Args("""{"command":"dotnet test","timeout_seconds":30}"""),
                Context(FileSystem()));

        result.Content.Should().Contain("Killed after 30 second(s)");
    }

    [Fact]
    public async Task A_command_with_no_output_says_so_rather_than_showing_a_blank_result()
    {
        var runner = new StubProcessRunner
        {
            Result = new ProcessResult(0, string.Empty, string.Empty, false),
        };

        var result = await new RunCommandTool(runner)
            .InvokeAsync(ToolTestHost.Args("""{"command":"true"}"""), Context(FileSystem()));

        result.Content.Should().Contain("(no output)");
    }

    [Fact]
    public async Task The_default_timeout_is_applied_when_none_is_asked_for()
    {
        var runner = new StubProcessRunner();

        await new RunCommandTool(runner)
            .InvokeAsync(ToolTestHost.Args("""{"command":"dotnet build"}"""), Context(FileSystem()));

        runner.Timeout.Should().Be(TimeSpan.FromSeconds(RunCommandTool.DefaultTimeoutSeconds));
    }

    [Fact]
    public async Task An_absurd_timeout_is_clamped_to_the_maximum()
    {
        var runner = new StubProcessRunner();

        await new RunCommandTool(runner)
            .InvokeAsync(
                ToolTestHost.Args("""{"command":"dotnet build","timeout_seconds":999999}"""),
                Context(FileSystem()));

        runner.Timeout.Should().Be(TimeSpan.FromSeconds(RunCommandTool.MaxTimeoutSeconds));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task A_timeout_that_is_not_a_duration_falls_back_to_a_usable_one(int seconds)
    {
        var runner = new StubProcessRunner();

        await new RunCommandTool(runner)
            .InvokeAsync(
                ToolTestHost.Args($$"""{"command":"dotnet build","timeout_seconds":{{seconds}}}"""),
                Context(FileSystem()));

        runner.Timeout.Should().Be(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Refuses_a_missing_command()
    {
        var result = await new RunCommandTool(new StubProcessRunner())
            .InvokeAsync(ToolTestHost.Args("""{}"""), Context(FileSystem()));

        result.IsSuccess.Should().BeFalse();
        result.Content.Should().Contain("command");
    }
}
