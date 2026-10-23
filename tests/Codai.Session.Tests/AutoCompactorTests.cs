using Codai.Core.Agents;
using Codai.Core.Chat;
using Codai.Core.Context;
using Codai.Core.Tokens;
using Codai.Core.Tools;
using Codai.Session.Tests.Fakes;
using FluentAssertions;

namespace Codai.Session.Tests;

/// <summary>
/// Covers the two thresholds: the one that starts a summary in the background, and the one
/// that makes the next request wait for it.
/// </summary>
/// <remarks>
/// The point of both is what the user does not see. Below the first threshold nothing
/// happens at all; between the two a summary is written while an answer is being read, so
/// the turn that would have paid for it does not; above the second the request has no room
/// left and waits, which is the only case where a pause is the right answer.
/// </remarks>
public class AutoCompactorTests
{
    /// <summary>
    /// A model whose window is smaller than the default reserve, so any conversation at all
    /// measures as over budget. Useful for testing the urgent path without having to build a
    /// conversation that is genuinely enormous.
    /// </summary>
    private const string SmallWindowModel = "gpt-4";

    /// <summary>A model with a window large enough to be tuned to any budget a test wants.</summary>
    private const string RoomyModel = "gpt-4.1";

    private const string Root = @"C:\project";

    /// <summary>Characters per turn that put a three-turn conversation well over any budget.</summary>
    private const int OverBudgetPadding = 4_000;

    /// <summary>
    /// Characters per field in one turn, which is more than a digest can keep and more than
    /// a summary needs: it is the case where shortening without a summary can still help.
    /// </summary>
    private const int HugePadding = 60_000;

    /// <summary>Characters added per turn while a test grows a conversation to a ratio.</summary>
    private const int GrowthStep = 4_000;

    /// <summary>The tool the harness registers, so the deducted tool budget is a real one.</summary>
    private static readonly StubTool Tool = new("read_file");

    private static (
        AutoCompactor Compactor,
        BackgroundSummarizer Summarizer,
        ScriptedChatCompletionService Chat,
        ChatHistory History,
        FakeUserInterface Ui,
        CompactionPolicy Policy) Build(
        bool autoCompact = true,
        string model = SmallWindowModel,
        CompactionPolicy? policy = null,
        CompactionStrategy strategy = CompactionStrategy.Agentic,
        bool trim = false)
    {
        var options = new SessionOptions
        {
            ProjectRoot = Root,
            Provider = "openai",
            Model = model,
            Mode = AgentMode.Agent,
            AutoCompact = autoCompact,
            CompactionStrategy = strategy,
        };

        var chat = new ScriptedChatCompletionService();
        var history = new ChatHistory();
        var ui = new FakeUserInterface();
        var active = policy ?? new CompactionPolicy();
        var compactor = new HistoryCompactor(
            chat,
            history,
            new TokenTracker(),
            new ToolAccessPolicy(),
            ui,
            new TranscriptStore(new InMemoryFileSystem()),
            options);

        var summarizer = new BackgroundSummarizer(compactor);
        var transcripts = new TranscriptStore(new InMemoryFileSystem());
        var trimmer = trim ? new MicroCompactor(history, transcripts, options, ui) : null;

        return (
            new AutoCompactor(
                options,
                history,
                compactor,
                summarizer,
                active,
                ui,
                new AgentModeState(AgentMode.Agent),
                new StubToolRegistry(Tool),
                trimmer),
            summarizer,
            chat,
            history,
            ui,
            active);
    }

    /// <summary>
    /// A policy whose budget is <paramref name="budget"/> tokens, chosen by holding back
    /// everything else of a model's window.
    /// </summary>
    private static CompactionPolicy BudgetOf(int budget, string model = RoomyModel) =>
        new() { ReserveTokens = ContextWindow.For(model) - budget };

    private static void FillHistory(ChatHistory history, int turns = 3, int padding = 0)
    {
        for (var i = 0; i < turns; i++)
        {
            var filler = padding == 0 ? string.Empty : new string('y', padding);
            history.Add(new ChatTurn(DateTimeOffset.UtcNow, $"question {i}{filler}", $"answer {i}{filler}"));
        }
    }

    [Fact]
    public async Task Disabled_compaction_refuses_overflow_recovery_without_mutating_history()
    {
        var (compactor, _, chat, history, _, _) = Build(autoCompact: false);
        FillHistory(history, turns: 6, padding: HugePadding);
        var original = history.Turns.ToArray();

        (await compactor.RecoverFromOverflowAsync()).Should().BeFalse();

        history.Turns.Should().Equal(original);
        chat.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Basic_compaction_shortens_urgent_history_without_summary_model_call()
    {
        var (compactor, _, chat, history, _, _) = Build(strategy: CompactionStrategy.Basic);
        FillHistory(history, turns: 6, padding: HugePadding);
        var original = history.Turns.ToArray();

        await compactor.BeforeRequestAsync("task");

        chat.Requests.Should().BeEmpty();
        history.Turns.Should().NotEqual(original);
    }

    /// <summary>
    /// Grows the conversation one turn at a time until it measures as
    /// <paramref name="target"/>, or past it, the way the compactor measures it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The ratio depends on the window, the reserve and the tool budget, and asserting on it
    /// from a hand-written padding would pin the test to an arithmetic that is not what it is
    /// testing. Growing to the band and then asserting on the behaviour is.
    /// </para>
    /// <para>
    /// A turn is a large step, so a band narrower than one turn can be stepped over. Past the
    /// target counts as reaching it, because a conversation that is too long for the target
    /// to matter is not something a fixture should fail on.
    /// </para>
    /// </remarks>
    private static void GrowUntil(
        ChatHistory history,
        CompactionPolicy policy,
        string model,
        ContextPressureLevel target)
    {
        for (var turn = 0; turn < 200; turn++)
        {
            if (history.Turns.Count >= HistoryCompactor.MinimumTurnsToCompact &&
                (int)PressureOf(history, policy, model).Level >= (int)target)
            {
                return;
            }

            FillHistory(history, turns: 1, padding: GrowthStep);
        }

        throw new InvalidOperationException(
            $"A conversation of {history.Turns.Count} turns never measured as {target}.");
    }

    /// <summary>Measures a conversation the way the compactor does, tools included.</summary>
    private static ContextPressure PressureOf(ChatHistory history, CompactionPolicy policy, string model)
    {
        var tokens = 0;

        foreach (var turn in history.Turns)
        {
            tokens += TokenEstimator.EstimateRequest(turn.UserRequest, turn.AiResponse);
        }

        return policy.Measure(tokens, model, TokenEstimator.EstimateTools([Tool]));
    }

    [Fact]
    public async Task A_conversation_over_budget_is_trimmed_before_a_summary_is_paid_for()
    {
        var (compactor, _, chat, history, ui, _) = Build(model: RoomyModel, policy: BudgetOf(22_000), trim: true);
        FillWithBigAnswers(history, turns: 7, answerChars: 20_000);

        await compactor.BeforeRequestAsync("a new question");

        // The summary a compaction would have asked for is never sent: clearing the old tool
        // output was enough to put the conversation back inside its budget.
        chat.Requests.Should().BeEmpty();
        history.Turns.Count(turn => turn.AiResponse.Contains(MicroCompaction.ElisionMarker, StringComparison.Ordinal))
            .Should().BePositive();
        history.Turns[^1].AiResponse.Should().HaveLength(20_000);
        ui.Infos.Should().Contain(message => message.Contains("Cleared", StringComparison.Ordinal));
    }

    /// <summary>
    /// Grows a conversation whose bulk is in the answers, which is what a compaction can
    /// actually take away: the questions stay whatever the tier below summarising does.
    /// </summary>
    private static void FillWithBigAnswers(ChatHistory history, int turns, int answerChars)
    {
        for (var i = 0; i < turns; i++)
        {
            history.Add(new ChatTurn(DateTimeOffset.UtcNow, $"question {i}", new string('z', answerChars)));
        }
    }

    [Fact]
    public async Task A_conversation_that_cannot_fit_is_summarised_before_the_request_is_sent()
    {
        var (compactor, _, chat, history, ui, policy) = Build();
        FillHistory(history, padding: OverBudgetPadding);
        chat.Enqueue("the summary");

        await compactor.BeforeRequestAsync("a new question");

        // The summary stands in for the old turn and the two most recent turns are still
        // there word for word: a summary of the turn in progress would be a paraphrase of
        // what the next request is about.
        history.Turns.Should().HaveCount(3);
        history.Turns[0].AiResponse.Should().Be("the summary");
        history.Turns[^1].AiResponse.Should().StartWith("answer 2");
        policy.ConsecutiveFailures.Should().Be(0);
        policy.IsTripped.Should().BeFalse();

        ui.Infos.Should().ContainSingle(message => message.Contains("Shortening it before the request goes out", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_conversation_with_room_to_spare_never_waits_for_a_summary()
    {
        var (compactor, summarizer, chat, history, ui, _) = Build(model: RoomyModel);
        FillHistory(history, turns: 3, padding: 400);

        await compactor.BeforeRequestAsync("a new question");
        compactor.AfterResponse();

        PressureOf(history, new CompactionPolicy(), RoomyModel).Level.Should().Be(ContextPressureLevel.Healthy);

        history.Turns.Should().HaveCount(3);
        chat.Requests.Should().BeEmpty();
        summarizer.State.Should().Be(BackgroundSummarizationState.Idle);
        ui.Infos.Should().BeEmpty();
    }

    [Fact]
    public async Task A_turn_that_still_fits_is_answered_before_the_summary_is_written()
    {
        // Past the proactive ratio and short of the critical one. The request would be sent
        // either way, so the summary must not be waited for: it is the whole reason for
        // having two thresholds.
        var policy = BudgetOf(20_000);

        var (compactor, summarizer, chat, history, ui, _) = Build(model: RoomyModel, policy: policy);
        GrowUntil(history, policy, RoomyModel, ContextPressureLevel.Proactive);

        var turns = history.Turns.Count;
        chat.Enqueue("the summary");

        await compactor.BeforeRequestAsync("a new question");

        // Returned before the summary was applied, and without saying anything the user
        // would have to read: work that is not waited for should not interrupt either.
        history.Turns.Should().HaveCount(turns);
        ui.Infos.Should().BeEmpty();
        summarizer.State.Should().NotBe(BackgroundSummarizationState.Idle);

        // The turn boundary applies it, not the request that started it.
        await summarizer.WhenIdleAsync();
        compactor.AfterResponse();

        history.Turns[0].AiResponse.Should().Be("the summary");
        history.Turns[^1].AiResponse.Should().StartWith("answer");
    }

    [Fact]
    public async Task A_summary_that_was_written_earlier_is_applied_without_blocking_the_request()
    {
        // The turn that overflows should find the summary already written, because the
        // previous turn started it. A pause is only acceptable when there was no time.
        var policy = BudgetOf(20_000);

        var (compactor, summarizer, chat, history, ui, _) = Build(model: RoomyModel, policy: policy);
        GrowUntil(history, policy, RoomyModel, ContextPressureLevel.Proactive);
        chat.Enqueue("the summary");

        // The turn that crosses the threshold. It is answered, and the work it started
        // finishes while the answer is being read.
        await compactor.BeforeRequestAsync("a question that still fits");
        await summarizer.WhenIdleAsync();

        // The next turn is over the critical ratio: without the ready summary this is the
        // moment the user would have waited.
        GrowUntil(history, policy, RoomyModel, ContextPressureLevel.Critical);

        await compactor.BeforeRequestAsync("a question that does not fit");

        history.Turns[0].AiResponse.Should().Be("the summary");
        ui.Infos.Should().NotContain(message => message.Contains("Shortening it before the request goes out", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Nothing_happens_when_the_switch_is_off()
    {
        var (compactor, summarizer, chat, history, _, _) = Build(autoCompact: false);
        FillHistory(history, padding: OverBudgetPadding);
        chat.Enqueue("the summary");

        await compactor.BeforeRequestAsync("a new question");
        compactor.AfterResponse();

        history.Turns.Should().HaveCount(3);
        chat.Requests.Should().BeEmpty();
        summarizer.State.Should().Be(BackgroundSummarizationState.Idle);
    }

    [Fact]
    public async Task A_summary_that_fails_in_the_background_is_retried_in_front_of_the_question()
    {
        // The first attempt answers with nothing, which is a summary that cannot be used and
        // a history too short for the digest to improve on. The request cannot be sent without
        // room, which is what makes the second attempt worth the wait.
        var (compactor, _, chat, history, ui, policy) = Build();
        FillHistory(history);
        chat.Enqueue(string.Empty);
        chat.Enqueue("the summary");

        await compactor.BeforeRequestAsync("a new question");

        // One attempt behind the previous answer, one in front of this question.
        chat.Requests.Should().HaveCount(2);

        // The second one was used, so nothing was lost by the first one failing and the
        // session does not carry a failure it did not have.
        history.Turns[0].AiResponse.Should().Be("the summary");
        policy.ConsecutiveFailures.Should().Be(0);
        policy.IsTripped.Should().BeFalse();
        ui.Infos.Should().Contain(message => message.Contains("Shortening it before the request goes out", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_summary_that_fails_costs_the_user_one_attempt_and_no_more()
    {
        // Enormous turns and nothing scripted: the summary fails, but there is no need for
        // one, because the turns themselves are larger than anything worth keeping. The
        // second attempt is not made, so the pause stays one model call long rather than two.
        var (compactor, _, chat, history, _, policy) = Build();
        FillHistory(history, padding: HugePadding);
        var before = PressureOf(history, policy, SmallWindowModel).UsedTokens;

        await compactor.BeforeRequestAsync("a new question");

        chat.Requests.Should().HaveCount(1);
        policy.ConsecutiveFailures.Should().Be(0);
        PressureOf(history, policy, SmallWindowModel).UsedTokens.Should().BeLessThan(before);
    }

    [Fact]
    public async Task A_failed_summary_is_not_a_reason_to_lose_the_conversation()
    {
        // Three short turns with an impossible budget: there is nothing to drop and the
        // digest has nothing to gain, so the conversation has to survive as it is.
        var (compactor, _, _, history, ui, policy) = Build();
        FillHistory(history);

        await compactor.BeforeRequestAsync("a new question");

        history.Turns.Should().HaveCount(3);
        policy.ConsecutiveFailures.Should().Be(1);
        ui.Errors.Should().Contain(message => message.Contains("empty summary", StringComparison.Ordinal));
        ui.Errors.Should().Contain(message => message.Contains("left alone", StringComparison.Ordinal));
    }

    [Fact]
    public async Task After_repeated_failures_compaction_gives_up_and_the_session_carries_on()
    {
        var (compactor, _, chat, history, ui, policy) = Build();
        FillHistory(history);

        for (var attempt = 0; attempt < CompactionPolicy.DefaultMaxFailures; attempt++)
        {
            await compactor.BeforeRequestAsync("a new question");
        }

        policy.IsTripped.Should().BeTrue();
        ui.Errors.Should().Contain(message => message.Contains("will not be attempted again", StringComparison.Ordinal));

        var requestsBefore = chat.Requests.Count;

        // Nothing more is asked of the model for compaction: a session that answers with a
        // long conversation is better than one that spends every turn failing to shorten it.
        await compactor.BeforeRequestAsync("another question");
        compactor.AfterResponse();

        chat.Requests.Should().HaveCount(requestsBefore);
        history.Turns.Should().HaveCount(3);
    }
}
