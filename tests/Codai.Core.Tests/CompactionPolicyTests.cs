using Codai.Core.Context;
using FluentAssertions;

namespace Codai.Core.Tests;

/// <summary>
/// Covers the compaction threshold and its circuit breaker.
/// </summary>
public class CompactionPolicyTests
{
    [Fact]
    public void The_budget_keeps_the_reserve_free()
    {
        var budget = new ContextBudget(100_000, 128_000, 13_000);

        budget.ThresholdTokens.Should().Be(115_000);
        budget.RemainingTokens.Should().Be(15_000);
        budget.IsOverThreshold.Should().BeFalse();
        budget.Utilisation.Should().BeApproximately(100_000d / 115_000d, 0.000001);
    }

    [Fact]
    public void Eating_into_the_reserve_is_what_counts_as_over_threshold()
    {
        new ContextBudget(115_000, 128_000, 13_000).IsOverThreshold.Should().BeFalse();
        new ContextBudget(115_001, 128_000, 13_000).IsOverThreshold.Should().BeTrue();
    }

    [Fact]
    public void A_window_smaller_than_the_reserve_never_reports_room_to_spare()
    {
        var budget = new ContextBudget(1, 1_000, 13_000);

        budget.ThresholdTokens.Should().Be(0);
        budget.RemainingTokens.Should().Be(0);
        budget.IsOverThreshold.Should().BeTrue();
        budget.Utilisation.Should().Be(1d);
    }

    [Fact]
    public void Measures_against_the_window_but_never_below_the_floor()
    {
        var policy = new CompactionPolicy();

        policy.Measure(10, 128_000).ContextWindow.Should().Be(128_000);
        policy.Measure(10, 1).ContextWindow.Should().Be(ContextWindow.MinimumTokens);
    }

    [Fact]
    public void Compaction_is_asked_for_only_when_the_request_would_not_fit()
    {
        var policy = new CompactionPolicy();

        policy.ShouldCompact(policy.Measure(10_000, 128_000)).Should().BeFalse();
        policy.ShouldCompact(policy.Measure(200_000, 128_000)).Should().BeTrue();
    }

    [Fact]
    public void Failing_repeatedly_gives_up_instead_of_retrying_forever()
    {
        var policy = new CompactionPolicy();

        var over = policy.Measure(200_000, 128_000);
        policy.ShouldCompact(over).Should().BeTrue();

        // The breaker counts consecutive failures: below the limit compaction is still
        // worth attempting, at the limit the session chooses a long conversation over a
        // stuck one.
        for (var attempt = 1; attempt < CompactionPolicy.DefaultMaxFailures; attempt++)
        {
            policy.RecordFailure();
            policy.ConsecutiveFailures.Should().Be(attempt);
            policy.IsTripped.Should().BeFalse();
            policy.ShouldCompact(over).Should().BeTrue();
        }

        policy.RecordFailure();

        policy.IsTripped.Should().BeTrue();
        policy.ShouldCompact(over).Should().BeFalse();
    }

    [Fact]
    public void One_success_clears_the_breaker()
    {
        var policy = new CompactionPolicy();
        var buffer = new ContextBudget(200_000, 128_000, CompactionPolicy.DefaultReserveTokens);

        policy.RecordFailure();
        policy.RecordFailure();
        policy.RecordSuccess();

        policy.ConsecutiveFailures.Should().Be(0);
        policy.IsTripped.Should().BeFalse();
        policy.ShouldCompact(buffer).Should().BeTrue();
    }

    [Fact]
    public void The_breaker_length_can_be_configured()
    {
        var policy = new CompactionPolicy { MaxFailures = 1 };

        policy.RecordFailure();

        policy.IsTripped.Should().BeTrue();
    }
}
