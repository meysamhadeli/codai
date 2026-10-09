using Codai.Core.Chat;
using Codai.Core.Context;
using Codai.Core.Tokens;
using Codai.Core.Tools;
using Codai.Session.Tests.Fakes;
using FluentAssertions;

namespace Codai.Session.Tests;

/// <summary>
/// Covers the decision to compact before a request rather than after the provider has
/// refused it, and what happens when compacting itself keeps failing.
/// </summary>
public class AutoCompactorTests
{
    /// <summary>A model with the smallest window in the table, so the threshold is easy to cross.</summary>
    private const string SmallWindowModel = "gpt-4";

    private static async Task<(AutoCompactor Compactor, ScriptedChatCompletionService Chat, ChatHistory History, FakeUserInterface Ui, CompactionPolicy Policy)>
        BuildAsync(bool autoCompact = true)
    {
        await Task.CompletedTask;

        var options = new SessionOptions
        {
            ProjectRoot = @"C:\project",
            Provider = "openai",
            Model = SmallWindowModel,
            AutoCompact = autoCompact,
        };

        var chat = new ScriptedChatCompletionService();
        var history = new ChatHistory();
        var tokens = new TokenTracker();
        var ui = new FakeUserInterface();
        var policy = new CompactionPolicy();
        var compactor = new HistoryCompactor(chat, history, tokens, new ToolAccessPolicy(), ui);

        return (new AutoCompactor(options, history, compactor, policy, ui), chat, history, ui, policy);
    }

    private static void FillHistory(ChatHistory history, int turns = 3)
    {
        for (var i = 0; i < turns; i++)
        {
            history.Add(new ChatTurn(DateTimeOffset.UtcNow, $"question {i}", $"answer {i}"));
        }
    }

    [Fact]
    public async Task A_conversation_that_cannot_fit_is_summarised_before_the_request_is_sent()
    {
        var (compactor, chat, history, ui, policy) = await BuildAsync();
        FillHistory(history);
        chat.Enqueue("the summary");

        var compacted = await compactor.CompactIfNeededAsync("a new question");

        compacted.Should().BeTrue();
        history.Turns.Should().ContainSingle();
        history.Turns[0].AiResponse.Should().Be("the summary");
        policy.ConsecutiveFailures.Should().Be(0);

        ui.Infos.Should().ContainSingle()
            .Which.Should().Contain("Summarising it first").And.Contain(SmallWindowModel);
    }

    [Fact]
    public async Task Nothing_happens_when_the_switch_is_off()
    {
        var (compactor, chat, history, _, _) = await BuildAsync(autoCompact: false);
        FillHistory(history);
        chat.Enqueue("the summary");

        (await compactor.CompactIfNeededAsync("a new question")).Should().BeFalse();

        history.Turns.Should().HaveCount(3);
        chat.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task A_conversation_with_room_to_spare_is_left_alone()
    {
        // The same history with a roomy window must not be compacted: the decision is the
        // measurement, not the fact that the model has a name in the window table.
        var options = new SessionOptions
        {
            ProjectRoot = @"C:\project",
            Provider = "openai",
            Model = "gpt-4.1",
        };

        var chat = new ScriptedChatCompletionService();
        var history = new ChatHistory();
        var ui = new FakeUserInterface();

        var compactor = new AutoCompactor(
            options,
            history,
            new HistoryCompactor(chat, history, new TokenTracker(), new ToolAccessPolicy(), ui),
            new CompactionPolicy(),
            ui);

        FillHistory(history);

        (await compactor.CompactIfNeededAsync("a new question")).Should().BeFalse();

        history.Turns.Should().HaveCount(3);
        chat.Requests.Should().BeEmpty();
        ui.Infos.Should().BeEmpty();
    }

    [Fact]
    public async Task A_failed_summary_is_not_a_reason_to_lose_the_conversation()
    {
        var (compactor, _, history, ui, policy) = await BuildAsync();
        FillHistory(history);

        // No scripted response means an empty summary, which the compactor refuses.
        (await compactor.CompactIfNeededAsync("a new question")).Should().BeFalse();

        history.Turns.Should().HaveCount(3);
        policy.ConsecutiveFailures.Should().Be(1);
        ui.Errors.Should().ContainSingle().Which.Should().Contain("empty summary");
    }

    [Fact]
    public async Task After_repeated_failures_compaction_gives_up_and_the_session_carries_on()
    {
        var (compactor, chat, history, ui, policy) = await BuildAsync();
        FillHistory(history);

        for (var attempt = 0; attempt < CompactionPolicy.DefaultMaxFailures; attempt++)
        {
            await compactor.CompactIfNeededAsync("a new question");
        }

        policy.IsTripped.Should().BeTrue();
        ui.Errors.Should().Contain(message => message.Contains("will not be attempted again", StringComparison.Ordinal));

        var requestsBefore = chat.Requests.Count;

        // Nothing more is asked of the model for compaction: the session answers with the
        // long history instead of spending its budget on a step that keeps failing.
        (await compactor.CompactIfNeededAsync("another question")).Should().BeFalse();
        chat.Requests.Should().HaveCount(requestsBefore);
    }
}
