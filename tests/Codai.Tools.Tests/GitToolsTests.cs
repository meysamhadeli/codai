using Codai.Core.Abstractions;
using Codai.Core.Tools;
using FluentAssertions;

namespace Codai.Tools.Tests;

public sealed class GitToolsTests
{
    [Theory]
    [InlineData("git_status", "git status --short --branch")]
    [InlineData("git_diff", "git --no-pager diff --no-ext-diff --no-textconv --color=never --")]
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

    [Theory]
    [InlineData("{\"source\":\"staged\"}", "--cached --")]
    [InlineData("{\"source\":\"commit\",\"target\":\"abc123\"}", "--color=never abc123 --")]
    [InlineData("{\"source\":\"branch\",\"target\":\"feature/my-branch\"}", "feature/my-branch...HEAD --")]
    public async Task Diff_supports_read_only_review_targets(string arguments, string expected)
    {
        var runner = new StubProcessRunner { Result = new ProcessResult(0, "diff", "", false) };
        var tool = new GitDiffTool(runner);

        var result = await tool.InvokeAsync(ToolTestHost.Args(arguments), ToolTestHost.Context(ToolTestHost.NewProject()));

        result.IsSuccess.Should().BeTrue();
        tool.IsReadOnly.Should().BeTrue();
        runner.CommandLine.Should().Contain(expected).And.Contain("--no-ext-diff --no-textconv");
    }

    [Theory]
    [InlineData("{\"source\":\"commit\",\"target\":\"HEAD;echo bad\"}")]
    [InlineData("{\"source\":\"branch\",\"target\":\"main&echo bad\"}")]
    [InlineData("{\"source\":\"branch\",\"target\":\"main$(id)\"}")]
    [InlineData("{\"source\":\"branch\",\"target\":\"%PATH%\"}")]
    [InlineData("{\"source\":\"branch\",\"target\":\"--output=file\"}")]
    [InlineData("{\"source\":\"branch\",\"target\":\"main..HEAD\"}")]
    [InlineData("{\"source\":\"commit\"}")]
    [InlineData("{\"source\":false}")]
    [InlineData("{\"source\":\"unknown\"}")]
    [InlineData("{\"source\":\"staged\",\"target\":\"HEAD\"}")]
    public async Task Diff_rejects_invalid_or_shell_injected_targets_before_process_execution(string arguments)
    {
        var runner = new StubProcessRunner();

        var result = await new GitDiffTool(runner).InvokeAsync(ToolTestHost.Args(arguments), ToolTestHost.Context(ToolTestHost.NewProject()));

        result.IsSuccess.Should().BeFalse();
        runner.CommandLine.Should().BeNull();
    }
}
