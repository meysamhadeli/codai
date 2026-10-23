using Codai.Core.Agents;
using Codai.Core.Tokens;
using FluentAssertions;

namespace Codai.Core.Tests;

public sealed class AgentBudgetTests
{
    [Fact]
    public void Enforces_tool_call_limit()
    {
        var budget = new AgentBudget { MaxToolCalls = 1 };
        budget.BeginTask();

        budget.TryConsumeToolCall(out _).Should().BeTrue();
        budget.TryConsumeToolCall(out var reason).Should().BeFalse();
        reason.Should().Contain("tool budget");
    }

    [Fact]
    public void Enforces_token_and_cost_limits_atomically()
    {
        var budget = new AgentBudget { MaxTokens = 20, MaxCost = 0.05m };
        budget.BeginTask();

        budget.TryConsume(new TokenUsage(4, 4), 0.04m, out _).Should().BeTrue();
        budget.TryConsume(new TokenUsage(4, 3), 0.02m, out var reason).Should().BeFalse();
        reason.Should().Contain("cost budget");
        budget.Usage.TotalTokens.Should().Be(8);
        budget.Cost.Should().Be(0.04m);
    }

    [Fact]
    public void Begin_task_resets_all_consumption()
    {
        var budget = new AgentBudget { MaxToolCalls = 1 };
        budget.BeginTask();
        budget.TryConsumeToolCall(out _);
        budget.TryConsume(new TokenUsage(2, 2), 0.01m, out _);

        budget.BeginTask();

        budget.ToolCalls.Should().Be(0);
        budget.Usage.Should().Be(TokenUsage.Empty);
        budget.Cost.Should().Be(0);
        budget.TryConsumeToolCall(out _).Should().BeTrue();
    }
}