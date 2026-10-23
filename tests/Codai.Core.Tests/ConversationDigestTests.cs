using Codai.Core.Chat;
using Codai.Core.Context;
using FluentAssertions;

namespace Codai.Core.Tests;

/// <summary>
/// Covers the shortening that happens without a model: what it keeps, what it drops, and
/// the promise that it never returns something the caller would mistake for progress when
/// there was none.
/// </summary>
/// <remarks>
/// These tests state properties rather than exact sizes wherever they can. How many turns
/// survive a budget is arithmetic over a token estimate, and a test that pinned the number
/// would break on a change to the estimate rather than on a change to the behaviour.
/// </remarks>
public class ConversationDigestTests
{
    private const string Archive = ".codai/transcripts/20240501-120000-000.md";

    /// <summary>Turns numbered in the order they happened, so which ones survived is testable.</summary>
    private static ChatTurn[] Turns(int count, int padding = 0)
    {
        var now = DateTimeOffset.UtcNow;

        return
        [
            .. Enumerable
                .Range(0, count)
                .Select(index => new ChatTurn(
                    now,
                    $"question {index} {new string('x', padding)}",
                    $"answer {index} {new string('x', padding)}")),
        ];
    }

    private static int Cost(IEnumerable<ChatTurn> turns) =>
        turns.Sum(turn => TokenEstimator.EstimateRequest(turn.UserRequest, turn.AiResponse));

    private static int NumberOf(ChatTurn turn) =>
        int.Parse(new string([.. turn.UserRequest.Skip("question ".Length).TakeWhile(char.IsDigit)]));

    [Fact]
    public void A_conversation_of_one_turn_is_not_worth_packing()
    {
        var turns = Turns(1);

        ConversationDigest.Pack(turns, 1_000).Should().BeSameAs(turns);
    }

    [Fact]
    public void A_conversation_that_already_fits_is_returned_unchanged()
    {
        // The caller has to be able to tell "packed" from "nothing to do": a pack that
        // saves nothing is not a compaction, and replacing the history with it would throw
        // the record of the failure away for nothing.
        var turns = Turns(4);

        ConversationDigest.Pack(turns, 100_000).Should().BeSameAs(turns);
    }

    [Fact]
    public void The_middle_goes_and_the_newest_turn_stays()
    {
        var turns = Turns(12, padding: 500);

        var packed = ConversationDigest.Pack(turns, 1_200);

        packed.Count.Should().BeLessThan(turns.Length);
        packed[0].UserRequest.Should().Be(ConversationDigest.DroppedTurnsLabel);
        NumberOf(packed[^1]).Should().Be(11);
    }
    [Fact]
    public void The_first_turn_is_kept_when_there_is_room_for_it()
    {
        // It carries what the work is for, and nothing else in the conversation restates
        // it. Reserved before the newest turns are chosen, so it survives even when the
        // room for the newest turns is what runs out first.
        var turns = Turns(12, padding: 500);

        var packed = ConversationDigest.Pack(turns, 1_200);

        NumberOf(packed[1]).Should().Be(0);
    }

    [Fact]
    public void The_notice_says_how_many_turns_went_and_where_they_are()
    {
        var turns = Turns(12, padding: 500);

        var packed = ConversationDigest.Pack(turns, 1_200, Archive);

        // The notice is itself the turn that stands in for the ones that went, so the count
        // it reports is the number of turns that are no longer in the conversation.
        var replaced = turns.Length - packed.Count + 1;

        packed[0].AiResponse.Should().Contain($"{replaced} earlier turn(s)");
        packed[0].AiResponse.Should().Contain(Archive);
    }

    [Fact]
    public void Without_an_archive_the_notice_does_not_pretend_one_exists()
    {
        var turns = Turns(12, padding: 500);

        var packed = ConversationDigest.Pack(turns, 1_200);

        packed[0].AiResponse.Should().Contain("no longer in the conversation");
        packed[0].AiResponse.Should().NotContain("archived at");
    }

    [Fact]
    public void A_pack_never_costs_more_than_the_conversation_it_replaces()
    {
        // The one invariant the fallback exists for. Whatever the shape of the history, the
        // result has to be smaller, or the caller has shortened nothing and lost turns.
        foreach (var padding in new[] { 0, 500, 2_000, 20_000 })
        {
            foreach (var budget in new[] { 1, 1_000, 2_000, 10_000, 50_000 })
            {
                var turns = Turns(10, padding);
                var packed = ConversationDigest.Pack(turns, budget, Archive);

                if (packed.Count == turns.Length)
                {
                    // Nothing was replaced, which is reported rather than applied.
                    continue;
                }

                Cost(packed).Should().BeLessThan(
                    Cost(turns),
                    $"a pack of {turns.Length} turns of {padding} characters must shrink it");
            }
        }
    }

    [Fact]
    public void A_pack_fits_its_budget_when_a_single_turn_can()
    {
        var turns = Turns(20, padding: 2_000);

        var packed = ConversationDigest.Pack(turns, 3_000);

        // The notice is charged to the same budget, so the allowance for it is included.
        Cost(packed).Should().BeLessThan(3_000 + 200);
    }

    [Fact]
    public void A_history_of_large_turns_still_leaves_the_newest_one_behind()
    {
        // Nothing fits beside the ends, so the newest turn is kept in its shortened form
        // rather than the conversation being replaced by a notice saying it is all gone.
        var turns = Turns(6, padding: 20_000);

        var packed = ConversationDigest.Pack(turns, 2_000);

        packed.Count.Should().BeLessThan(turns.Length);
        NumberOf(packed[^1]).Should().Be(5);
        packed[^1].UserRequest.Should().Contain("(truncated)");
    }

    [Fact]
    public void A_turn_over_the_cap_is_cut_down_rather_than_left_alone()
    {
        var turns = Turns(4, padding: ConversationDigest.MaxTurnCharacters);

        var packed = ConversationDigest.Pack(turns, 5_000);

        packed[^1].UserRequest.Should().Contain("(truncated)");
        packed[^1].UserRequest.Length.Should().Be(ConversationDigest.MaxTurnCharacters);
    }

    [Fact]
    public void A_shortened_conversation_with_nothing_missing_gets_no_notice()
    {
        // Every turn is still here, only shorter, so there is nothing for a notice to stand
        // in for. The truncation markers say what happened where it happened.
        var turns = Turns(4, padding: 20_000);

        var packed = ConversationDigest.Pack(turns, 100_000);

        packed.Should().HaveCount(4);
        packed[0].UserRequest.Should().NotBe(ConversationDigest.DroppedTurnsLabel);
        packed[^1].UserRequest.Should().Contain("(truncated)");
        Cost(packed).Should().BeLessThan(Cost(turns));
    }

    [Fact]
    public void A_budget_below_the_chosen_minimum_is_raised_to_it()
    {
        // A caller asking for less than the minimum gets the minimum. A conversation
        // reduced to a notice is a conversation that has to be started again.
        var turns = Turns(10, padding: 2_000);

        var packed = ConversationDigest.Pack(turns, 1);

        packed.Should().HaveCountGreaterThan(1);
        packed.Count.Should().BeLessThan(turns.Length);
    }

    [Fact]
    public void The_kept_turns_keep_the_order_they_happened_in()
    {
        var turns = Turns(20, padding: 400);

        var packed = ConversationDigest.Pack(turns, 4_000);

        var numbers = packed.Skip(1).Select(NumberOf).ToArray();

        numbers.Should().BeInAscendingOrder();
        numbers.Should().Contain(0, "the first turn is kept");
        numbers.Should().Contain(19, "the newest turn is what the next request is built on");
    }
}
