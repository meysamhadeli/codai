using Codai.Core.Abstractions;
using Codai.Core.Tools;
using FluentAssertions;

namespace Codai.Tools.Tests;

public sealed class DotnetWorkflowToolTests
{
    [Theory]
    [InlineData("dotnet_test", "dotnet test --nologo")]
    [InlineData("dotnet_build", "dotnet build --nologo")]
    public async Task Workflow_tools_are_mutating_and_run_fixed_commands(string name, string command)
    {
        var runner = new StubProcessRunner
        {
            Result = new ProcessResult(0, "passed", string.Empty, false),
        };
        ITool tool = name == "dotnet_test"
            ? new DotnetTestTool(runner)
            : new DotnetBuildTool(runner);

        var result = await tool.InvokeAsync(
            ToolTestHost.Args("{}"),
            ToolTestHost.Context(ToolTestHost.NewProject()));

        tool.IsReadOnly.Should().BeFalse();
        runner.CommandLine.Should().Be(command);
        runner.Timeout.Should().Be(TimeSpan.FromMinutes(10));
        result.IsSuccess.Should().BeTrue();
        result.Content.Should().Be("passed");
    }
}