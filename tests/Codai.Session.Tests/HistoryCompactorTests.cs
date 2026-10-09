using Codai.Core.Chat;
using Codai.Core.Tokens;
using Codai.Session.Tests.Fakes;
using FluentAssertions;

namespace Codai.Session.Tests;

public class HistoryCompactorTests
{
    private static (HistoryCompactor Compactor, ScriptedChatCompletionService Chat, ChatHistory History, FakeUserInterface Ui)
        Create()
    {
        var chat = new ScriptedChatCompletionService();
        var history = new ChatHistory();
        var ui = new FakeUserInterface();
        var tokens = new TokenTracker();

        return (new HistoryCompactor(chat, history, tokens, ui), chat, history, ui);
    }

    private static void Seed(ChatHistory history, int turns)
    {
        for (var i = 0; i < turns; i++)
        {
            history.Add(new ChatTurn(DateTimeOffset.UtcNow, $"question {i}", $"answer {i}"));
        }
    }

    [Fact]
    public async Task Replaces_the_history_with_a_single_summary_turn()
    {
        var (compactor, chat, history, ui) = Create();
        Seed(history, 3);
        chat.Enqueue("everything that mattered");

        var compacted = await compactor.CompactAsync();

        compacted.Should().BeTrue();
        history.Turns.Should().ContainSingle();
        history.Turns[0].UserRequest.Should().Be(HistoryCompactor.SummaryTurnLabel);
        history.Turns[0].AiResponse.Should().Be("everything that mattered");
        ui.Successes.Should().Contain(message => message.Contains("3 turn", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Sends_the_conversation_to_the_model()
    {
        var (compactor, chat, history, _) = Create();
        Seed(history, 2);
        chat.Enqueue("summary");

        await compactor.CompactAsync();

        chat.Requests.Should().ContainSingle();
        chat.Requests[0].SystemPrompt.Should().Contain("compacting the conversation history");
        chat.Requests[0].UserMessage.Should().Contain("question 0").And.Contain("answer 1");
    }

    [Fact]
    public async Task A_focus_is_passed_on_as_something_to_weight()
    {
        var (compactor, chat, history, _) = Create();
        Seed(history, 2);
        chat.Enqueue("summary");

        await compactor.CompactAsync("  the migration file names  ");

        chat.Requests.Should().ContainSingle();

        // The focus steers the summary; it must not be allowed to replace the request, or
        // the conversation itself would never be sent. The trailing spaces are trimmed.
        chat.Requests[0].UserMessage
            .Should()
            .Contain("focus on")
            .And.Contain("the migration file names")
            .And.Contain("question 0")
            .And.NotContain("  the migration file names  ");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Without_a_focus_the_request_is_the_conversation_alone(string? instructions)
    {
        var (compactor, chat, history, _) = Create();
        Seed(history, 2);
        chat.Enqueue("summary");

        await compactor.CompactAsync(instructions);

        chat.Requests[0].UserMessage.Should().NotContain("focus on");
    }

    [Fact]
    public async Task Refuses_to_compact_an_almost_empty_history()
    {
        var (compactor, chat, history, ui) = Create();
        Seed(history, HistoryCompactor.MinimumTurnsToCompact - 1);

        var compacted = await compactor.CompactAsync();

        compacted.Should().BeFalse();
        chat.Requests.Should().BeEmpty();
        history.Turns.Should().HaveCount(HistoryCompactor.MinimumTurnsToCompact - 1);
        ui.Errors.Should().Contain(message => message.Contains("Nothing to compact", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Keeps_the_history_when_the_model_answers_nothing()
    {
        var (compactor, chat, history, ui) = Create();
        Seed(history, 2);
        chat.Enqueue(string.Empty);

        var compacted = await compactor.CompactAsync();

        compacted.Should().BeFalse();
        history.Turns.Should().HaveCount(2);
        ui.Errors.Should().Contain(message => message.Contains("empty summary", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Records_the_tokens_the_summary_cost()
    {
        var chat = new ScriptedChatCompletionService();
        var history = new ChatHistory();
        var ui = new FakeUserInterface();
        var tokens = new TokenTracker();
        Seed(history, 2);
        chat.Enqueue("summary", new TokenUsage(90, 10));

        await new HistoryCompactor(chat, history, tokens, ui).CompactAsync();

        tokens.SessionUsage.Should().Be(new TokenUsage(90, 10));
        ui.ProgressLabels.Should().Contain("Compacting history...");
    }
}
