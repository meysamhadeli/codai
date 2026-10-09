using Codai.Core.Abstractions;
using FluentAssertions;

namespace Codai.Tools.Tests;

/// <summary>
/// Covers <c>task</c>: the tool that lets the model spend a separate context window on an
/// investigation instead of filling this one.
/// </summary>
public class TaskToolTests
{
    [Fact]
    public void Delegating_changes_nothing_itself()
    {
        // Read-only is what makes the tool safe in every mode: it cannot be the way a
        // read-only session ends up writing, because the sub-agent only ever gets
        // read-only tools.
        var tool = new TaskTool(new RecordingSubAgent());

        tool.Name.Should().Be(SubAgent.ToolName);
        tool.IsReadOnly.Should().BeTrue();
    }

    [Fact]
    public void The_schema_requires_both_the_label_and_the_question()
    {
        // "description" is what the user sees while it runs, so a call without one would
        // leave a progress line that says nothing.
        var tool = new TaskTool(new RecordingSubAgent());

        var schema = System.Text.Json.JsonDocument.Parse(tool.ParametersSchema).RootElement;

        schema.GetProperty("required")
            .EnumerateArray()
            .Select(element => element.GetString())
            .Should().BeEquivalentTo(["description", "prompt"]);
    }

    [Fact]
    public async Task Hands_the_question_to_the_sub_agent()
    {
        var subAgent = new RecordingSubAgent();
        var tool = new TaskTool(subAgent);

        await tool.InvokeAsync(
            ToolTestHost.Args("""{"description":"Where is the token validated?","prompt":"Look in src/Auth."}"""),
            ToolTestHost.Context(ToolTestHost.NewProject()));

        subAgent.Requests.Should().ContainSingle();
        subAgent.Requests[0].Description.Should().Be("Where is the token validated?");
        subAgent.Requests[0].Prompt.Should().Be("Look in src/Auth.");
    }

    [Fact]
    public async Task Returns_the_report_as_a_successful_result()
    {
        var tool = new TaskTool(new RecordingSubAgent { Report = "# Report from a sub-agent\n\nIt is in Program.cs." });

        var result = await tool.InvokeAsync(
            ToolTestHost.Args("""{"description":"d","prompt":"p"}"""),
            ToolTestHost.Context(ToolTestHost.NewProject()));

        result.IsSuccess.Should().BeTrue();
        result.Content.Should().Contain("It is in Program.cs.");
    }

    [Fact]
    public async Task A_failed_run_is_a_failed_call_the_model_can_read()
    {
        // Failure as data, not an exception: the parent's loop carries on and the model
        // decides what to do instead.
        var tool = new TaskTool(new RecordingSubAgent
        {
            Report = "The sub-agent could not run: overloaded.",
            Succeeded = false,
        });

        var result = await tool.InvokeAsync(
            ToolTestHost.Args("""{"description":"d","prompt":"p"}"""),
            ToolTestHost.Context(ToolTestHost.NewProject()));

        result.IsSuccess.Should().BeFalse();
        result.Content.Should().Contain("could not run");
    }

    [Fact]
    public async Task A_call_missing_an_argument_never_reaches_the_sub_agent()
    {
        var subAgent = new RecordingSubAgent();
        var tool = new TaskTool(subAgent);

        var result = await tool.InvokeAsync(
            ToolTestHost.Args("""{"description":"only a label"}"""),
            ToolTestHost.Context(ToolTestHost.NewProject()));

        result.IsSuccess.Should().BeFalse();
        subAgent.Requests.Should().BeEmpty();
    }
}

/// <summary>An <see cref="ISubAgent"/> that answers with whatever the test asked for.</summary>
internal sealed class RecordingSubAgent : ISubAgent
{
    public List<SubAgentRequest> Requests { get; } = [];

    public string Report { get; set; } = "Reported.";

    public bool Succeeded { get; set; } = true;

    public Task<SubAgentResult> RunAsync(
        SubAgentRequest request,
        CancellationToken cancellationToken = default)
    {
        Requests.Add(request);

        return Task.FromResult(
            Succeeded
                ? SubAgentResult.Success(Report)
                : SubAgentResult.Failure(Report));
    }
}
