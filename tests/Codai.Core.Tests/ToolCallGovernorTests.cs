using System.Text.Json;
using Codai.Core.Tools;
using FluentAssertions;

namespace Codai.Core.Tests;

/// <summary>
/// Covers the two guards that keep one request's tool use from running away: a total
/// call limit and a repeat limit.
/// </summary>
public class ToolCallGovernorTests
{
    private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void The_first_calls_of_a_request_are_allowed()
    {
        var governor = new ToolCallGovernor();

        governor.Review("read_file", Args("""{"path":"a.cs"}""")).Allowed.Should().BeTrue();
        governor.Calls.Should().Be(1);
    }

    [Fact]
    public void Reading_many_different_files_is_never_treated_as_a_loop()
    {
        var governor = new ToolCallGovernor();

        for (var i = 0; i < 20; i++)
        {
            governor.Review("read_file", Args($$"""{"path":"file{{i}}.cs"}""")).Allowed.Should().BeTrue();
        }

        governor.Calls.Should().Be(20);
    }

    [Fact]
    public void Repeating_the_same_call_is_refused_and_the_message_says_why()
    {
        var governor = new ToolCallGovernor();
        var arguments = Args("""{"path":"a.cs"}""");

        for (var i = 0; i < ToolCallGovernor.DefaultMaxRepeats; i++)
        {
            governor.Review("read_file", arguments).Allowed.Should().BeTrue();
        }

        var decision = governor.Review("read_file", arguments);

        decision.Allowed.Should().BeFalse();
        decision.Reason.Should().Contain("cannot return anything new");
        decision.Reason.Should().Contain("read_file");

        // A refused call is not counted, so a model that keeps trying cannot use the
        // refusal itself to exhaust the request's budget.
        governor.Calls.Should().Be(ToolCallGovernor.DefaultMaxRepeats);
    }

    [Fact]
    public void The_same_arguments_in_a_different_order_are_the_same_call()
    {
        var governor = new ToolCallGovernor();

        governor.Review("edit_file", Args("""{"path":"a.cs","old_string":"x"}"""));
        governor.Review("edit_file", Args("""{"old_string":"x","path":"a.cs"}"""));

        governor.Review("edit_file", Args("""{"path":"a.cs","old_string":"x"}"""))
            .Allowed.Should().BeFalse();
    }

    [Fact]
    public void Different_arguments_are_a_different_call()
    {
        var governor = new ToolCallGovernor();

        governor.Review("read_file", Args("""{"path":"a.cs"}"""));
        governor.Review("read_file", Args("""{"path":"b.cs"}"""));
        governor.Review("read_file", Args("""{"path":"c.cs"}""")).Allowed.Should().BeTrue();
    }

    [Fact]
    public void The_total_call_limit_stops_the_request()
    {
        var governor = new ToolCallGovernor { MaxCalls = 3 };

        for (var i = 0; i < 3; i++)
        {
            governor.Review("read_file", Args($$"""{"path":"file{{i}}.cs"}""")).Allowed.Should().BeTrue();
        }

        var decision = governor.Review("read_file", Args("""{"path":"other.cs"}"""));

        decision.Allowed.Should().BeFalse();
        decision.Reason.Should().Contain("Stop calling tools");
    }

    [Fact]
    public void Consecutive_failures_stop_at_the_configured_limit()
    {
        var governor = new ToolCallGovernor { MaxConsecutiveFailures = 2 };

        governor.RecordOutcome(ToolResult.Failure("missing file")).StopsExecution.Should().BeFalse();
        var result = governor.RecordOutcome(ToolResult.Failure("bad path"));

        result.IsSuccess.Should().BeFalse();
        result.StopsExecution.Should().BeTrue();
        result.Content.Should().Contain("bad path").And.Contain("2 consecutive failures");
        governor.Review("read_file", Args("{}")).Allowed.Should().BeFalse();
    }

    [Fact]
    public void Success_resets_consecutive_failure_count()
    {
        var governor = new ToolCallGovernor { MaxConsecutiveFailures = 2 };

        governor.RecordOutcome(ToolResult.Failure("missing file"));
        governor.RecordOutcome(ToolResult.Success("found file"));

        governor.RecordOutcome(ToolResult.Failure("bad path")).StopsExecution.Should().BeFalse();
    }

    [Fact]
    public void Failure_limit_preserves_changes_that_already_reached_disk()
    {
        var governor = new ToolCallGovernor { MaxConsecutiveFailures = 1 };
        var change = new ToolFileChange("landed.txt", 2, 1, "-old\n+new\n+more");

        var result = governor.RecordOutcome(ToolResult.Failure("later operation failed") with { FileChanges = [change] });

        result.StopsExecution.Should().BeTrue();
        result.IsSuccess.Should().BeFalse();
        result.FileChanges.Should().ContainSingle().Which.Should().Be(change);
    }

    [Fact]
    public void A_new_request_resets_the_failure_limit()
    {
        var governor = new ToolCallGovernor { MaxConsecutiveFailures = 1 };
        governor.RecordOutcome(ToolResult.Failure("missing file")).StopsExecution.Should().BeTrue();

        governor.BeginRequest();

        governor.Review("read_file", Args("{}")).Allowed.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Failure_limit_must_be_positive(int limit)
    {
        Action act = () => _ = new ToolCallGovernor { MaxConsecutiveFailures = limit };

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void A_new_request_starts_with_a_clean_budget()
    {
        var governor = new ToolCallGovernor { MaxCalls = 1 };

        governor.Review("read_file", Args("""{"path":"a.cs"}"""));
        governor.Review("read_file", Args("""{"path":"b.cs"}""")).Allowed.Should().BeFalse();

        governor.BeginRequest();

        governor.Calls.Should().Be(0);
        governor.Review("read_file", Args("""{"path":"a.cs"}""")).Allowed.Should().BeTrue();
    }
}
