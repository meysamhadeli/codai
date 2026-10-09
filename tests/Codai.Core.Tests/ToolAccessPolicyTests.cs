using Codai.Core.Tools;
using FluentAssertions;

namespace Codai.Core.Tests;

/// <summary>
/// Covers the rule that a request may only call the tools it was built with. The set is
/// what keeps a read-only mode read-only when the provider is willing to call anything.
/// </summary>
public class ToolAccessPolicyTests
{
    [Fact]
    public void An_unrestricted_request_may_call_anything()
    {
        var policy = new ToolAccessPolicy();

        policy.IsAllowed("write_file").Should().BeTrue();
        policy.IsAllowed("anything_at_all").Should().BeTrue();
    }

    [Fact]
    public void A_restricted_request_may_only_call_the_tools_it_named()
    {
        var policy = new ToolAccessPolicy();
        policy.BeginRequest(["read_file", "grep"]);

        policy.IsAllowed("read_file").Should().BeTrue();
        policy.IsAllowed("grep").Should().BeTrue();
        policy.IsAllowed("write_file").Should().BeFalse();
        policy.IsAllowed("run_command").Should().BeFalse();
    }

    [Fact]
    public void Tool_names_are_matched_ignoring_case()
    {
        var policy = new ToolAccessPolicy();
        policy.BeginRequest(["Read_File"]);

        policy.IsAllowed("read_file").Should().BeTrue();
        policy.IsAllowed("READ_FILE").Should().BeTrue();
    }

    [Fact]
    public void A_request_that_allows_nothing_allows_nothing()
    {
        var policy = new ToolAccessPolicy();
        policy.BeginRequest([]);

        policy.IsAllowed("read_file").Should().BeFalse();
        policy.IsAllowed("write_file").Should().BeFalse();
    }

    [Fact]
    public void The_next_request_replaces_the_previous_restriction()
    {
        var policy = new ToolAccessPolicy();

        policy.BeginRequest(["read_file"]);
        policy.IsAllowed("write_file").Should().BeFalse();

        // The user changed the mode, or the request is not a mode-scoped one at all.
        policy.BeginRequest(null);
        policy.IsAllowed("write_file").Should().BeTrue();

        policy.BeginRequest(["read_file"]);
        policy.IsAllowed("write_file").Should().BeFalse();
    }

    [Fact]
    public void A_denial_tells_the_model_what_it_does_have()
    {
        var policy = new ToolAccessPolicy();
        policy.BeginRequest(["read_file", "grep"]);

        var denial = policy.DescribeDenial("write_file");

        denial.Should().Contain("write_file").And.Contain("not available");
        denial.Should().Contain("read_file, grep");
    }

    [Fact]
    public void A_denial_for_an_empty_set_does_not_list_nothing_as_a_tool()
    {
        var policy = new ToolAccessPolicy();
        policy.BeginRequest([]);

        policy.DescribeDenial("write_file").Should().Contain("Available tools: none.");
    }
}
