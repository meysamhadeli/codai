using Codai.Core.Chat;
using Codai.Core.Tokens;
using FluentAssertions;

namespace Codai.Core.Tests;

public class ChatHistoryTests
{
    [Fact]
    public void Starts_empty()
    {
        new ChatHistory().Turns.Should().BeEmpty();
    }

    [Fact]
    public void Adds_and_clears_turns()
    {
        var history = new ChatHistory();
        history.Add(new ChatTurn(DateTimeOffset.UtcNow, "q", "a"));

        history.Turns.Should().ContainSingle();

        history.Clear();

        history.Turns.Should().BeEmpty();
    }

    [Fact]
    public void Preserves_insertion_order()
    {
        var history = new ChatHistory();
        history.Add(new ChatTurn(DateTimeOffset.UtcNow, "first", "1"));
        history.Add(new ChatTurn(DateTimeOffset.UtcNow, "second", "2"));

        history.Turns.Select(t => t.UserRequest).Should().ContainInOrder("first", "second");
    }
}

public class TokenTrackerTests
{
    [Fact]
    public void Starts_at_zero()
    {
        var tracker = new TokenTracker();

        tracker.SessionUsage.Should().Be(TokenUsage.Empty);
        tracker.LastUsage.Should().Be(TokenUsage.Empty);
    }

    [Fact]
    public void Accumulates_session_usage_and_keeps_last_request()
    {
        var tracker = new TokenTracker();

        tracker.Record(new TokenUsage(100, 50));
        tracker.Record(new TokenUsage(10, 5));

        tracker.LastUsage.Should().Be(new TokenUsage(10, 5));
        tracker.SessionUsage.Should().Be(new TokenUsage(110, 55));
        tracker.SessionUsage.TotalTokens.Should().Be(165);
    }

    [Fact]
    public void Clear_resets_everything()
    {
        var tracker = new TokenTracker();
        tracker.Record(new TokenUsage(100, 50));

        tracker.Clear();

        tracker.SessionUsage.Should().Be(TokenUsage.Empty);
        tracker.LastUsage.Should().Be(TokenUsage.Empty);
    }
}

public class TokenUsageTests
{
    [Fact]
    public void Add_sums_both_components()
    {
        new TokenUsage(3, 4).Add(new TokenUsage(5, 6)).Should().Be(new TokenUsage(8, 10));
    }

    [Fact]
    public void HasValue_reflects_total()
    {
        TokenUsage.Empty.HasValue.Should().BeFalse();
        new TokenUsage(0, 1).HasValue.Should().BeTrue();
    }
}
