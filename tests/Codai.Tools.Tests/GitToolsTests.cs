using Codai.Core.Abstractions;
using Codai.Core.Tools;
using FluentAssertions;

namespace Codai.Tools.Tests;

public sealed class GitToolsTests
{
    [Theory]
    [InlineData("git_status", "git status --short --branch")]
    [InlineData("git_diff", "git diff --no-ext-diff --")]
    [InlineData("git_log", "git log --oneline --decorate -n 20")]
    public async Task Read_only_git_tools_run_fixed_commands(string name, string command)
    {
        var runner = new StubProcessRunner
        {
            Result = new ProcessResult(0, "result", string.Empty, false),
        };
        ITool tool = name switch
        {
            "git_status" => new GitStatusTool(runner),
            "git_diff" => new GitDiffTool(runner),
            "git_log" => new GitLogTool(runner),
            _ => throw new InvalidOperationException(name),
        };

        var result = await tool.InvokeAsync(
            ToolTestHost.Args("{}"),
            ToolTestHost.Context(ToolTestHost.NewProject()));

        tool.IsReadOnly.Should().BeTrue();
        tool.Name.Should().Be(name);
        runner.CommandLine.Should().Be(command);
        result.IsSuccess.Should().BeTrue();
        result.Content.Should().Be("result");
    }

    [Fact]
    public async Task Git_failure_is_returned_as_a_tool_failure()
    {
        var runner = new StubProcessRunner
        {
            Result = new ProcessResult(128, string.Empty, "not a repository", false),
        };

        var result = await new GitStatusTool(runner).InvokeAsync(
            ToolTestHost.Args("{}"),
            ToolTestHost.Context(ToolTestHost.NewProject()));

        result.IsSuccess.Should().BeFalse();
        result.Content.Should().Contain("not a repository");
    }
}