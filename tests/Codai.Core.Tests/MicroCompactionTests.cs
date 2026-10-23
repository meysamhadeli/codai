using Codai.Core.Chat;
using Codai.Core.Context;
using FluentAssertions;

namespace Codai.Core.Tests;

/// <summary>
/// Covers the tier that shortens a conversation without asking a model: which turns it
/// clears, what it keeps of them, and when it stops.
/// </summary>
public class MicroCompactionTests
{
    private static readonly DateTimeOffset When = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_conversation_that_already_fits_is_left_alone()
    {
        var turns = new[] { Turn("read it", 20_000), Turn("and again", 20_000), Turn("answer", 200) };

        var plan = MicroCompaction.Plan(turns, targetTokens: int.MaxValue);

        plan.IsEmpty.Should().BeTrue();
        plan.SavedTokens.Should().Be(0);
    }

    [Fact]
    public void The_newest_turns_are_never_cleared()
    {
        var turns = new[] { Turn("a", 20_000), Turn("b", 20_000), Turn("c", 20_000) };

        var plan = MicroCompaction.Plan(turns, targetTokens: 0);

        plan.Edits.Should().OnlyContain(edit => edit.Index == 0);
    }

    [Fact]
    public void A_cleared_response_keeps_its_start_and_its_end()
    {
        var response = new string('a', 20_000) + "THE-ERROR-AT-THE-END";
        var turns = new[] { new ChatTurn(When, "run the tests", response), Turn("b", 10), Turn("c", 10) };

        var plan = MicroCompaction.Plan(turns, targetTokens: 0);
        var cleared = plan.Edits.Should().ContainSingle().Subject.Replacement.AiResponse;

        cleared.Should().StartWith(new string('a', MicroCompaction.HeadCharacters));
        cleared.Should().Contain(MicroCompaction.ElisionMarker);
        cleared.Should().EndWith("THE-ERROR-AT-THE-END");
        cleared.Length.Should().BeLessThan(response.Length);
        cleared.Should().NotContain(new string('a', MicroCompaction.HeadCharacters + 1));
    }

    [Fact]
    public void The_request_and_the_position_of_a_cleared_turn_are_untouched()
    {
        var turns = new[] { Turn("read the parser", 20_000), Turn("b", 10), Turn("c", 10) };

        var edit = MicroCompaction.Plan(turns, targetTokens: 0).Edits.Should().ContainSingle().Subject;

        edit.Index.Should().Be(0);
        edit.Replacement.UserRequest.Should().Be("read the parser");
        edit.Replacement.Timestamp.Should().Be(When);
        edit.Replacement.Kind.Should().Be(ChatTurnKind.Conversation);
    }

    [Fact]
    public void A_summary_is_never_cleared()
    {
        var summary = new ChatTurn(When, "summary", new string('s', 20_000), ChatTurnKind.CompactionSummary);
        var turns = new[] { summary, Turn("b", 10), Turn("c", 10) };

        MicroCompaction.Plan(turns, targetTokens: 0).IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void A_turn_that_costs_little_is_not_worth_clearing()
    {
        var turns = new[] { Turn("a", 4_000), Turn("b", 10), Turn("c", 10) };

        MicroCompaction.Plan(turns, targetTokens: 0).IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Clearing_stops_as_soon_as_the_target_is_reached()
    {
        var turns = Enumerable.Range(0, 6)
            .Select(index => Turn($"turn {index}", 20_000))
            .ToArray();
        var measured = MicroCompaction.Estimate(turns);

        var plan = MicroCompaction.Plan(turns, targetTokens: measured - 1, currentTokens: measured);

        plan.Edits.Should().ContainSingle();
        plan.SavedTokens.Should().BePositive();
        plan.ProjectedTokens.Should().Be(measured - plan.SavedTokens);
    }

    [Fact]
    public void The_plan_saves_what_the_cleared_turns_cost_less()
    {
        var turns = new[] { Turn("a", 20_000), Turn("b", 20_000), Turn("c", 10), Turn("d", 10) };
        var measured = MicroCompaction.Estimate(turns);

        var plan = MicroCompaction.Plan(turns, targetTokens: 0, currentTokens: measured);

        plan.Edits.Should().HaveCount(2);
        plan.ProjectedTokens.Should().BeLessThan(measured / 2);
        plan.SavedTokens.Should().Be(measured - plan.ProjectedTokens);
    }

    private static ChatTurn Turn(string request, int responseCharacters) =>
        new(When, request, new string('x', responseCharacters));
}
