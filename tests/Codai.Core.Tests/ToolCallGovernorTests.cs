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
