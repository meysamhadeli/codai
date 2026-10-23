using Codai.Core.Context;
using FluentAssertions;

namespace Codai.Core.Tests;

/// <summary>
/// Covers the compaction threshold and its circuit breaker.
/// </summary>
public class CompactionPolicyTests
{
    /// <summary>Window of the models these tests measure against.</summary>
    private const int Window = 128_000;

    [Fact]
    public void The_reserve_and_the_margin_are_taken_off_before_the_conversation_is_measured()
    {
        var policy = new CompactionPolicy();

        var pressure = policy.Measure(10_000, Window);

        pressure.WindowTokens.Should().Be(Window);
        pressure.ReserveTokens.Should().Be(CompactionPolicy.DefaultReserveTokens);
        pressure.BaseBudget.Should().Be(Window - CompactionPolicy.DefaultReserveTokens);
        pressure.MessageBudget.Should().Be(103_500);
        pressure.RemainingTokens.Should().Be(93_500);
    }

    [Fact]
    public void The_tool_definitions_come_off_the_conversation_budget()
    {
        var policy = new CompactionPolicy();

        var without = policy.Measure(10_000, Window);
        var with = policy.Measure(10_000, Window, toolTokens: 40_000);

        with.ToolTokens.Should().Be(40_000);
        with.MessageBudget.Should().Be(67_500);
        without.MessageBudget.Should().Be(103_500);
    }

    [Fact]
    public void A_window_smaller_than_the_reserve_has_no_room_to_spare()
    {
        var pressure = new CompactionPolicy().Measure(1, 1_000);

        pressure.BaseBudget.Should().Be(1);
        pressure.MessageBudget.Should().Be(1);
        pressure.RemainingTokens.Should().Be(0);
        pressure.Level.Should().Be(ContextPressureLevel.Exceeded);
        pressure.IsUrgent.Should().BeTrue();
    }

    [Fact]
    public void Measures_against_the_window_but_never_below_the_floor()
    {
        var policy = new CompactionPolicy();

        policy.Measure(10, Window).WindowTokens.Should().Be(Window);
        policy.Measure(10, 1).WindowTokens.Should().Be(ContextWindow.MinimumTokens);
    }

    [Fact]
    public void A_conversation_with_room_to_spare_is_left_alone()
    {
        var policy = new CompactionPolicy();

        var pressure = policy.Measure(70_000, Window);

        pressure.Ratio.Should().BeApproximately(0.6763, 0.0001);
        pressure.Level.Should().Be(ContextPressureLevel.Healthy);
        policy.ShouldCompact(pressure).Should().BeFalse();
    }

    [Fact]
    public void Compacting_is_asked_for_before_the_window_is_full()
    {
        // The point of the ratio: at 87% of the budget there is still room to send the
        // request, so the summary can be produced without the turn being lost.
        var policy = new CompactionPolicy();

        var pressure = policy.Measure(90_000, Window);

        pressure.Level.Should().Be(ContextPressureLevel.Proactive);
        pressure.IsUrgent.Should().BeFalse();
        policy.ShouldCompact(pressure).Should().BeTrue();
    }

    [Fact]
    public void A_conversation_past_the_critical_ratio_cannot_be_sent()
    {
        var policy = new CompactionPolicy();

        var pressure = policy.Measure(99_000, Window);

        pressure.Ratio.Should().BeApproximately(0.9565, 0.0001);
        pressure.Level.Should().Be(ContextPressureLevel.Critical);
        pressure.IsUrgent.Should().BeTrue();
        policy.ShouldCompact(pressure).Should().BeTrue();
    }

    [Fact]
    public void Failing_repeatedly_gives_up_instead_of_retrying_forever()
    {
        var policy = new CompactionPolicy();

        var over = policy.Measure(200_000, Window);
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
        var over = policy.Measure(200_000, Window);

        policy.RecordFailure();
        policy.RecordFailure();
        policy.RecordSuccess();

        policy.ConsecutiveFailures.Should().Be(0);
        policy.IsTripped.Should().BeFalse();
        policy.ShouldCompact(over).Should().BeTrue();
    }

    [Fact]
    public void The_breaker_length_can_be_configured()
    {
        var policy = new CompactionPolicy { MaxFailures = 1 };

        policy.RecordFailure();

        policy.IsTripped.Should().BeTrue();
    }

    [Fact]
    public void An_unknown_model_is_measured_against_the_default_window()
    {
        var policy = new CompactionPolicy();

        policy.Measure(10, "a-model-that-does-not-exist").WindowTokens
            .Should()
            .Be(ContextWindow.DefaultTokens);
    }
}
