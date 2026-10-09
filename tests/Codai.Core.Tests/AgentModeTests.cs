using Codai.Core.Agents;
using Codai.Core.Tests.Fakes;
using FluentAssertions;

namespace Codai.Core.Tests;

/// <summary>
/// Covers the agent modes: how a mode is spelled, what it is allowed to call, and what
/// the user and the model are told about it.
/// </summary>
public class AgentModeTests
{
    [Theory]
    [InlineData("ask", AgentMode.Ask)]
    [InlineData("ASK", AgentMode.Ask)]
    [InlineData(" plan ", AgentMode.Plan)]
    [InlineData("Plan", AgentMode.Plan)]
    [InlineData("agent", AgentMode.Agent)]
    [InlineData("AGENT", AgentMode.Agent)]
    [InlineData("edit", AgentMode.Agent)]
    public void Parses_the_accepted_spellings(string input, AgentMode expected)
    {
        AgentModeParser.TryParse(input, out var mode).Should().BeTrue();
        mode.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("yolo")]
    public void Rejects_anything_else_without_throwing(string? input)
    {
        AgentModeParser.TryParse(input, out var mode).Should().BeFalse();
        mode.Should().Be(AgentModeParser.Default);
    }

    [Fact]
    public void The_default_mode_can_finish_the_task_but_still_asks_before_every_write()
    {
        // Writing is the point of the tool, and every write is approved individually,
        // so the default is the mode that is allowed to write. The read-only modes are
        // the deliberate choice, which is why they have to be asked for.
        AgentModeParser.Default.Should().Be(AgentMode.Agent);
        AgentModeParser.ParseOrDefault("nonsense").Should().Be(AgentMode.Agent);
    }

    [Theory]
    [InlineData(AgentMode.Ask)]
    [InlineData(AgentMode.Plan)]
    [InlineData(AgentMode.Agent)]
    public void A_mode_round_trips_through_its_name(AgentMode mode) =>
        AgentModeParser.ParseOrDefault(AgentModeParser.ToName(mode)).Should().Be(mode);

    [Fact]
    public void The_read_only_modes_are_offered_only_the_read_only_tools()
    {
        IReadOnlyList<Core.Tools.ITool> tools =
        [
            new StubTool("read_file"),
            new StubTool("write_file", isReadOnly: false),
            new StubTool("run_command", isReadOnly: false),
        ];

        AgentModePolicy.AllowedTools(AgentMode.Ask, tools).Should().Equal("read_file");
        AgentModePolicy.AllowedTools(AgentMode.Plan, tools).Should().Equal("read_file");
    }

    [Fact]
    public void Agent_mode_is_offered_every_tool() =>
        AgentModePolicy
            .AllowedTools(
                AgentMode.Agent,
                [new StubTool("read_file"), new StubTool("write_file", isReadOnly: false)])
            .Should().Equal("read_file", "write_file");

    [Fact]
    public void A_new_mutating_tool_is_excluded_from_the_read_only_modes_automatically()
    {
        // The rule is the IsReadOnly flag, not a list of names, so declaring a tool as
        // mutating is the only step needed to keep it out of ask and plan.
        var tools = new List<Core.Tools.ITool> { new StubTool("brand_new_tool", isReadOnly: false) };

        AgentModePolicy.AllowedTools(AgentMode.Ask, tools).Should().BeEmpty();
        AgentModePolicy.AllowedTools(AgentMode.Plan, tools).Should().BeEmpty();
        AgentModePolicy.AllowedTools(AgentMode.Agent, tools).Should().Equal("brand_new_tool");
    }

    [Fact]
    public void Only_agent_mode_may_write()
    {
        AgentModePolicy.AllowsWrites(AgentMode.Ask).Should().BeFalse();
        AgentModePolicy.AllowsWrites(AgentMode.Plan).Should().BeFalse();
        AgentModePolicy.AllowsWrites(AgentMode.Agent).Should().BeTrue();
    }

    [Fact]
    public void Every_mode_gets_its_own_guidance_and_its_own_summary()
    {
        var guidance = new[] { AgentMode.Ask, AgentMode.Plan, AgentMode.Agent }
            .Select(AgentModePolicy.Guidance)
            .ToArray();

        guidance.Should().OnlyHaveUniqueItems();
        guidance.Should().OnlyContain(text => text.Contains("How to work in this session", StringComparison.Ordinal));

        var summaries = new[] { AgentMode.Ask, AgentMode.Plan, AgentMode.Agent }
            .Select(AgentModePolicy.Summary)
            .ToArray();

        summaries.Should().OnlyHaveUniqueItems();
        summaries.Should().OnlyContain(text => text.Length > 0);
    }

    [Fact]
    public void The_mode_holder_starts_at_the_default_and_can_be_changed_in_place()
    {
        var state = new AgentModeState();

        state.Mode.Should().Be(AgentModeParser.Default);

        state.Mode = AgentMode.Ask;

        state.Mode.Should().Be(AgentMode.Ask);

        new AgentModeState(AgentMode.Plan).Mode.Should().Be(AgentMode.Plan);
    }
}
