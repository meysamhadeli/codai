using Codai.Cli.Ui;
using FluentAssertions;

namespace Codai.Session.Tests;

public class ActivityStatusTests
{
    [Fact]
    public void Begin_starts_a_fresh_wait_without_metrics()
    {
        var clock = new ManualClock();
        var status = new ActivityStatus(clock.Now);

        status.Begin("Thinking");
        var snapshot = status.Snapshot();

        snapshot.Label.Should().Be("Thinking");
        snapshot.Elapsed.Should().Be(TimeSpan.Zero);
        snapshot.Tokens.Should().Be(0);
        snapshot.ShowMetrics.Should().BeFalse();
    }

    [Fact]
    public void Metrics_appear_once_the_wait_is_long_enough_to_explain()
    {
        var clock = new ManualClock();
        var status = new ActivityStatus(clock.Now);
        status.Begin("Thinking");

        clock.Advance(ActivityStatus.RevealAfter - TimeSpan.FromMilliseconds(1));
        status.Snapshot().ShowMetrics.Should().BeFalse();

        clock.Advance(TimeSpan.FromMilliseconds(1));
        status.Snapshot().ShowMetrics.Should().BeTrue();
    }

    [Fact]
    public void Token_count_eases_towards_streamed_output_instead_of_jumping()
    {
        var clock = new ManualClock();
        var status = new ActivityStatus(clock.Now);
        status.Begin("Responding");
        status.Observe(4_000);

        var first = status.Snapshot();
        first.Tokens.Should().BePositive();
        first.Tokens.Should().BeLessThan(1_000);

        for (var frame = 0; frame < 60; frame++)
        {
            clock.Advance(TimeSpan.FromMilliseconds(60));
            status.Snapshot();
        }

        status.Snapshot().Tokens.Should().Be(1_000);
    }

    [Fact]
    public void Cleared_output_drops_the_count_without_restarting_the_wait()
    {
        var clock = new ManualClock();
        var status = new ActivityStatus(clock.Now);
        status.Begin("Responding");
        status.Observe(4_000);
        clock.Advance(TimeSpan.FromSeconds(5));
        status.Snapshot();

        status.ClearOutput();
        var snapshot = status.Snapshot();

        snapshot.Tokens.Should().Be(0);
        snapshot.Elapsed.Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void A_wait_without_progress_is_reported_as_stalled_until_it_moves_again()
    {
        var clock = new ManualClock();
        var status = new ActivityStatus(clock.Now);
        status.Begin("Thinking");

        clock.Advance(ActivityStatus.StallAfter + TimeSpan.FromSeconds(1));
        status.Snapshot().Stalled.Should().BeTrue();

        status.Pulse();
        status.Snapshot().Stalled.Should().BeFalse();
    }

    [Fact]
    public void Streamed_output_counts_as_progress()
    {
        var clock = new ManualClock();
        var status = new ActivityStatus(clock.Now);
        status.Begin("Responding");

        clock.Advance(ActivityStatus.StallAfter - TimeSpan.FromSeconds(1));
        status.Observe(120);
        clock.Advance(TimeSpan.FromSeconds(2));

        status.Snapshot().Stalled.Should().BeFalse();
    }

    [Fact]
    public void Interruptible_travels_with_the_snapshot()
    {
        var status = new ActivityStatus(() => TimeSpan.Zero);
        status.Begin("Thinking");

        status.Interruptible = true;

        status.Snapshot().Interruptible.Should().BeTrue();
    }

    [Theory]
    [InlineData(0, "0s")]
    [InlineData(12, "12s")]
    [InlineData(59, "59s")]
    [InlineData(64, "1m 04s")]
    [InlineData(3_720, "1h 02m")]
    public void Elapsed_is_formatted_for_reading(int seconds, string expected) =>
        ActivityStatus.FormatElapsed(TimeSpan.FromSeconds(seconds)).Should().Be(expected);

    [Theory]
    [InlineData(0, "0")]
    [InlineData(840, "840")]
    [InlineData(1_200, "1.2k")]
    [InlineData(12_450, "12.5k")]
    [InlineData(3_400_000, "3.4M")]
    public void Tokens_are_formatted_compactly(long tokens, string expected) =>
        ActivityStatus.FormatTokens(tokens).Should().Be(expected);

    private sealed class ManualClock
    {
        private TimeSpan _elapsed;

        public TimeSpan Now() => _elapsed;

        public void Advance(TimeSpan delta) => _elapsed += delta;
    }
}
