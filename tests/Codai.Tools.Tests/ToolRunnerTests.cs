using Codai.Core.Tools;
using FluentAssertions;

namespace Codai.Tools.Tests;

public class ToolRunnerTests
{
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
