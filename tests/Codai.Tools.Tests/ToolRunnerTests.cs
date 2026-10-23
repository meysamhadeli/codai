using Codai.Core.Abstractions;
using Codai.Core.Tools;
using FluentAssertions;

namespace Codai.Tools.Tests;

public class ToolRunnerTests
{
    [Fact]
    public async Task Result_observer_receives_bounded_result_and_structured_arguments()
    {
        var observer = new RecordingObserver();
        var change = new ToolFileChange("sample.txt", 40, 0, "+preview");
        var tool = new FakeTool("read_file")
        {
            Result = () => ToolResult.Success(new string('x', 500)) with { FileChanges = [change] },
        };
        var runner = Runner(new ToolRegistry([tool]), observer: observer, maxResultChars: 100);

        var result = await runner.ExecuteAsync("read_file", ToolTestHost.Args("{\"path\":\"sample.txt\"}"));

        observer.Results.Should().ContainSingle();
        observer.Results[0].Result.Should().Be(result);
        observer.Results[0].Arguments.GetProperty("path").GetString().Should().Be("sample.txt");
        result.Content.Length.Should().BeLessThan(500);
        result.FileChanges.Should().ContainSingle().Which.Should().Be(change);
    }

    [Fact]
    public async Task Preview_observer_failure_does_not_change_success_or_repeat_execution()
    {
        var observer = new RecordingObserver { ThrowOnResult = true };
        var tool = new FakeTool("read_file");
        var result = await Runner(new ToolRegistry([tool]), observer: observer).ExecuteAsync("read_file", ToolTestHost.Args("{}"));

        result.IsSuccess.Should().BeTrue();
        tool.Invocations.Should().Be(1);
    }

    [Fact]
    public async Task Denied_tool_has_no_result_preview()
    {
        var observer = new RecordingObserver();
        var tool = new FakeTool("write_file", isReadOnly: false);
        var result = await Runner(new ToolRegistry([tool]), DenyAllToolApprover.Instance, observer)
            .ExecuteAsync("write_file", ToolTestHost.Args("{}"));

        result.IsSuccess.Should().BeFalse();
        observer.Results.Should().BeEmpty();
        tool.Invocations.Should().Be(0);
    }

    [Fact]
    public async Task Clarification_preserves_real_answer_without_granting_consent()
    {
        var handler = new RecordingClarificationHandler("Use PostgreSQL.");
        var tool = new AskQuestionTool(handler);
        var result = await Runner(new ToolRegistry([tool]), DenyAllToolApprover.Instance)
            .ExecuteAsync(tool.Name, ToolTestHost.Args("{\"question\":\"Which database?\",\"options\":[\"SQLite\",\"PostgreSQL\"]}"));

        result.IsSuccess.Should().BeTrue();
        result.StopsExecution.Should().BeFalse();
        result.Content.Should().Be("Use PostgreSQL.");
        tool.RequiresUserInput.Should().BeTrue();
        handler.Options.Should().Equal("SQLite", "PostgreSQL");
    }

    [Fact]
    public async Task Missing_clarification_answer_stops_instead_of_assuming()
    {
        var tool = new AskQuestionTool(UnavailableClarificationHandler.Instance);
        var result = await Runner(new ToolRegistry([tool])).ExecuteAsync(tool.Name,
            ToolTestHost.Args("{\"question\":\"Which database?\"}"));

        result.IsSuccess.Should().BeFalse();
        result.StopsExecution.Should().BeTrue();
        result.Content.Should().Contain("Which database?");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"question\":\"  \"}")]
    [InlineData("{\"question\":42}")]
    [InlineData("{\"question\":\"Which?\",\"options\":{}}")]
    [InlineData("{\"question\":\"Which?\",\"options\":[42]}")]
    [InlineData("{\"question\":\"Which?\",\"options\":[\"\"]}")]
    [InlineData("{\"question\":\"Which?\",\"options\":[\"1\",\"2\",\"3\",\"4\",\"5\",\"6\"]}")]
    public async Task Invalid_clarification_does_not_consume_user_input(string args)
    {
        var handler = new RecordingClarificationHandler("answer");
        var tool = new AskQuestionTool(handler);

        var result = await Runner(new ToolRegistry([tool])).ExecuteAsync(tool.Name, ToolTestHost.Args(args));

        result.IsSuccess.Should().BeFalse();
        result.StopsExecution.Should().BeFalse();
        handler.Calls.Should().Be(0);
    }

    private sealed class RecordingClarificationHandler(string? answer) : IClarificationHandler
    {
        public int Calls { get; private set; }
        public IReadOnlyList<string> Options { get; private set; } = [];
        public bool Throws { get; init; }

        public Task<string?> AskAsync(string question, IReadOnlyList<string> options, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            if (Throws)
            {
                throw new IOException("Input unavailable.");
            }

            Options = options;
            return Task.FromResult(answer);
        }
    }

    [Fact]
    public async Task Clarification_handler_failure_stops_instead_of_assuming_answer()
    {
        var tool = new AskQuestionTool(new RecordingClarificationHandler(null) { Throws = true });

        var result = await Runner(new ToolRegistry([tool])).ExecuteAsync(tool.Name,
            ToolTestHost.Args("{\"question\":\"Which database?\"}"));

        result.IsSuccess.Should().BeFalse();
        result.StopsExecution.Should().BeTrue();
        result.Content.Should().Contain("Which database?").And.Contain("Input handler failed");
    }

    [Fact]
    public async Task Final_submission_is_read_only_and_preserves_final_text()
    {
        var tool = new SubmitTaskTool();
        var runner = Runner(new ToolRegistry([tool]), DenyAllToolApprover.Instance);

        var result = await runner.ExecuteAsync(tool.Name, ToolTestHost.Args("{\"result\":\"Finished.\\nNo edits applied.\"}"));

        tool.IsReadOnly.Should().BeTrue();
        result.IsSuccess.Should().BeTrue();
        result.StopsExecution.Should().BeTrue();
        result.Content.Should().Be("Finished.\nNo edits applied.");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"result\":\"\"}")]
    [InlineData("{\"result\":\"  \"}")]
    [InlineData("{\"result\":42}")]
    public async Task Invalid_final_submission_does_not_complete_task(string args)
    {
        var tool = new SubmitTaskTool();
        var result = await Runner(new ToolRegistry([tool])).ExecuteAsync(tool.Name, ToolTestHost.Args(args));

        result.IsSuccess.Should().BeFalse();
        result.StopsExecution.Should().BeFalse();
    }

    [Fact]
    public async Task Oversized_final_submission_is_rejected_instead_of_silently_completed()
    {
        var tool = new SubmitTaskTool();
        var args = System.Text.Json.JsonSerializer.Serialize(new { result = new string('x', SubmitTaskTool.MaxResultChars + 1) });

        var result = await Runner(new ToolRegistry([tool])).ExecuteAsync(tool.Name, ToolTestHost.Args(args));

        result.IsSuccess.Should().BeFalse();
        result.StopsExecution.Should().BeFalse();
    }

    private static ToolRunner Runner(
        IToolRegistry registry,
        IToolApprover? approver = null,
        IToolObserver? observer = null,
        int maxResultChars = ToolRunner.DefaultMaxResultChars) =>
        new(
            registry,
            approver ?? AlwaysApproveToolApprover.Instance,
            observer ?? NullToolObserver.Instance,
            ToolTestHost.NewProject(),
            ToolTestHost.Root,
            maxResultChars);

    [Fact]
    public async Task An_unknown_tool_fails_and_lists_what_exists()
    {
        var runner = Runner(new ToolRegistry([new FakeTool("read_file")]));

        var result = await runner.ExecuteAsync("nope", ToolTestHost.Args("{}"));

        result.IsSuccess.Should().BeFalse();
        result.Content.Should().Contain("Unknown tool 'nope'").And.Contain("read_file");
    }

    [Fact]
    public async Task A_read_only_tool_runs_without_asking_for_approval()
    {
        var approver = new StubApprover(approve: false);
        var tool = new FakeTool("read_file");
        var runner = Runner(new ToolRegistry([tool]), approver);

        var result = await runner.ExecuteAsync("read_file", ToolTestHost.Args("{}"));

        result.IsSuccess.Should().BeTrue();
        tool.Invocations.Should().Be(1);
        approver.Calls.Should().Be(0);
    }

    [Fact]
    public async Task A_writing_tool_is_denied_by_the_safe_default_approver()
    {
        var tool = new FakeTool("write_file", isReadOnly: false);
        var observer = new RecordingObserver();
        var runner = Runner(new ToolRegistry([tool]), DenyAllToolApprover.Instance, observer);

        var result = await runner.ExecuteAsync("write_file", ToolTestHost.Args("{}"));

        result.IsSuccess.Should().BeFalse();
        result.Content.Should().Contain("declined");
        tool.Invocations.Should().Be(0);
        observer.Events.Should().Equal("denied:write_file");
    }

    [Fact]
    public async Task A_writing_tool_runs_once_the_approver_agrees()
    {
        var tool = new FakeTool("write_file", isReadOnly: false);
        var approver = new StubApprover(approve: true);
        var runner = Runner(new ToolRegistry([tool]), approver);

        var result = await runner.ExecuteAsync("write_file", ToolTestHost.Args("""{"path":"a.txt"}"""));

        result.IsSuccess.Should().BeTrue();
        tool.Invocations.Should().Be(1);
        approver.LastRequest.ToolName.Should().Be("write_file");
        approver.LastRequest.Summary.Should().Be("write_file(path=a.txt)");
    }

    [Fact]
    public async Task An_approver_that_cannot_be_asked_is_treated_as_a_refusal()
    {
        var tool = new FakeTool("write_file", isReadOnly: false);
        var runner = Runner(new ToolRegistry([tool]), new StubApprover(approve: true, @throw: true));

        var result = await runner.ExecuteAsync("write_file", ToolTestHost.Args("{}"));

        result.IsSuccess.Should().BeFalse();
        tool.Invocations.Should().Be(0);
    }

    [Fact]
    public async Task A_tool_that_throws_becomes_a_failed_result()
    {
        var tool = new FakeTool("read_file")
        {
            Result = () => throw new InvalidOperationException("boom"),
        };
        var observer = new RecordingObserver();
        var runner = Runner(new ToolRegistry([tool]), observer: observer);

        var result = await runner.ExecuteAsync("read_file", ToolTestHost.Args("{}"));

        result.IsSuccess.Should().BeFalse();
        result.Content.Should().Contain("InvalidOperationException").And.Contain("boom");
        observer.Events.Should().Equal("started:read_file", "completed:read_file:False");
    }

    [Fact]
    public async Task A_failing_tool_result_is_reported_as_unsuccessful()
    {
        var tool = new FakeTool("read_file") { Result = () => ToolResult.Failure("no such file") };
        var observer = new RecordingObserver();
        var runner = Runner(new ToolRegistry([tool]), observer: observer);

        var result = await runner.ExecuteAsync("read_file", ToolTestHost.Args("{}"));

        result.IsSuccess.Should().BeFalse();
        result.Content.Should().Be("no such file");
        observer.Events.Should().Equal("started:read_file", "completed:read_file:False");
    }

    [Fact]
    public async Task Consecutive_tool_failures_stop_further_invocations()
    {
        var tool = new FakeTool("read_file") { Result = () => ToolResult.Failure("missing file") };
        var runner = Runner(new ToolRegistry([tool]));

        for (var index = 0; index < ToolCallGovernor.DefaultMaxConsecutiveFailures; index++)
        {
            var result = await runner.ExecuteAsync("read_file", ToolTestHost.Args($$"""{"path":"{{index}}"}"""));
            result.StopsExecution.Should().Be(index == ToolCallGovernor.DefaultMaxConsecutiveFailures - 1);
        }

        var denied = await runner.ExecuteAsync("read_file", ToolTestHost.Args("{}"));

        denied.IsSuccess.Should().BeFalse();
        tool.Invocations.Should().Be(ToolCallGovernor.DefaultMaxConsecutiveFailures);
    }

    [Fact]
    public async Task Tool_exceptions_count_toward_the_failure_limit()
    {
        var tool = new FakeTool("read_file") { Result = () => throw new IOException("read failed") };
        var runner = Runner(new ToolRegistry([tool]));
        ToolResult result = default;

        for (var index = 0; index < ToolCallGovernor.DefaultMaxConsecutiveFailures; index++)
        {
            result = await runner.ExecuteAsync("read_file", ToolTestHost.Args($$"""{"path":"{{index}}"}"""));
        }

        result.StopsExecution.Should().BeTrue();
        result.Content.Should().Contain("IOException").And.Contain("failure limit reached");
    }

    [Fact]
    public async Task Approval_denials_are_not_executed_tool_failures()
    {
        var write = new FakeTool("write_file", isReadOnly: false);
        var read = new FakeTool("read_file") { Result = () => ToolResult.Failure("missing file") };
        var runner = Runner(new ToolRegistry([write, read]), DenyAllToolApprover.Instance);

        for (var index = 0; index < 4; index++)
        {
            await runner.ExecuteAsync("write_file", ToolTestHost.Args($$"""{"path":"{{index}}"}"""));
        }

        var result = await runner.ExecuteAsync("read_file", ToolTestHost.Args("{}"));

        result.StopsExecution.Should().BeFalse();
        write.Invocations.Should().Be(0);
        read.Invocations.Should().Be(1);
    }

    [Fact]
    public async Task Oversized_output_is_truncated()
    {
        var tool = new FakeTool("read_file") { Result = () => ToolResult.Success(new string('x', 500)) };
        var runner = Runner(new ToolRegistry([tool]), maxResultChars: 100);

        var result = await runner.ExecuteAsync("read_file", ToolTestHost.Args("{}"));

        result.Content.Should().StartWith(new string('x', 100));
        result.Content.Should().Contain("truncated after 100 characters");
    }

    [Fact]
    public async Task Output_within_the_limit_is_untouched()
    {
        var tool = new FakeTool("read_file") { Result = () => ToolResult.Success("short") };
        var runner = Runner(new ToolRegistry([tool]), maxResultChars: 100);

        (await runner.ExecuteAsync("read_file", ToolTestHost.Args("{}")))
            .Content.Should().Be("short");
    }

    [Fact]
    public async Task Cancellation_propagates()
    {
        var tool = new FakeTool("read_file");
        var runner = Runner(new ToolRegistry([tool]));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () => await runner.ExecuteAsync("read_file", ToolTestHost.Args("{}"), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void Tools_exposes_the_registry_contents()
    {
        var runner = Runner(new ToolRegistry([new FakeTool("read_file"), new FakeTool("grep")]));

        runner.Tools.Select(tool => tool.Name).Should().Equal("read_file", "grep");
    }

    [Fact]
    public void A_duplicate_tool_name_is_rejected() =>
        FluentActions
            .Invoking(() => new ToolRegistry([new FakeTool("grep"), new FakeTool("grep")]))
            .Should().Throw<ArgumentException>();

    [Fact]
    public void Lookup_by_name_ignores_case()
    {
        var registry = new ToolRegistry([new FakeTool("read_file")]);

        registry.TryGet("Read_File", out var tool).Should().BeTrue();
        tool!.Name.Should().Be("read_file");
    }

    [Fact]
    public void An_empty_registry_has_no_tools()
    {
        ToolRegistry.Empty.Tools.Should().BeEmpty();
        ToolRegistry.Empty.TryGet("read_file", out _).Should().BeFalse();
    }
}
