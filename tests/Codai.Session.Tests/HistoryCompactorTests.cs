using Codai.Core.Abstractions;
using Codai.Core.Chat;
using Codai.Core.Context;
using Codai.Core.Tokens;
using Codai.Core.Tools;
using Codai.Configuration;
using Codai.Providers;
using Codai.Session.Tests.Fakes;
using FluentAssertions;

namespace Codai.Session.Tests;

/// <summary>
/// Covers what compaction keeps, what it replaces, and what it does with a summary that
/// should not be trusted.
/// </summary>
public class HistoryCompactorTests
{
    private const string Root = @"C:\project";

    private static (
        HistoryCompactor Compactor,
        ScriptedChatCompletionService Chat,
        ChatHistory History,
        FakeUserInterface Ui,
        InMemoryFileSystem FileSystem) Create(string model = "gpt-4o", ModeModelState? models = null)
    {
        var chat = new ScriptedChatCompletionService();
        var history = new ChatHistory();
        var ui = new FakeUserInterface();
        var tokens = new TokenTracker();
        var fileSystem = new InMemoryFileSystem();
        var options = new SessionOptions { ProjectRoot = Root, Provider = "openai", Model = model };

        var compactor = new HistoryCompactor(
            chat,
            history,
            tokens,
            new ToolAccessPolicy(),
            ui,
            new TranscriptStore(fileSystem),
            options, models: models);

        return (compactor, chat, history, ui, fileSystem);
    }

    private static void Seed(ChatHistory history, int turns, int padding = 0)
    {
        for (var i = 0; i < turns; i++)
        {
            var filler = padding == 0 ? string.Empty : new string('x', padding);
            history.Add(new ChatTurn(DateTimeOffset.UtcNow, $"question {i}{filler}", $"answer {i}{filler}"));
        }
    }

    [Fact]
    public async Task Summary_input_follows_selected_model_instead_of_startup_window()
    {
        var selected = ResolvedProviderOptions.From(new AiProviderOptions { Provider = "openai", Model = "gpt-4" });
        var (compactor, chat, history, _, _) = Create("gpt-4.1", new ModeModelState(selected, selected));
        Seed(history, 80, padding: 4_000);
        chat.Enqueue("summary for selected small model");

        await compactor.CompactAsync();

        chat.Requests.Should().ContainSingle();
        var request = chat.Requests[0];
        TokenEstimator.EstimateRequest(request.SystemPrompt, request.UserMessage).Should().BeLessThan(6_000);
        request.AutoCompact.Should().BeFalse();
        request.AllowedTools.Should().BeEmpty();
    }

    [Fact]
    public async Task Manual_compaction_keeps_recent_context_small_enough_for_selected_model()
    {
        var (compactor, chat, history, _, _) = Create("gpt-4");
        Seed(history, 20, padding: 2_000);
        chat.Enqueue("Keep parser decisions, failed tests, and remaining work.");

        (await compactor.CompactAsync("Keep parser decisions and failed tests")).Should().BeTrue();

        history.Turns.Sum(turn => TokenEstimator.EstimateRequest(turn.UserRequest, turn.AiResponse)).Should().BeLessThan(4_000);
        history.Turns[^1].UserRequest.Should().StartWith("question 19");
        chat.Requests.Single().UserMessage.Should().Contain("Keep parser decisions and failed tests");
    }

    [Theory]
    [InlineData(2_000)]
    [InlineData(8_000)]
    public async Task Oversized_summary_falls_back_to_model_sized_digest(int padding)
    {
        var (compactor, chat, history, _, fileSystem) = Create("gpt-4");
        Seed(history, 20, padding);
        chat.Enqueue(new string('s', 24_000));

        (await compactor.CompactAsync()).Should().BeTrue();

        HistoryCompactor.Estimate(history.Turns).Should().BeLessThan(4_000);
        history.Turns.Should().NotContain(turn => turn.AiResponse == new string('s', 24_000));
        fileSystem.Files.Should().Contain(file => file.Key.Contains(".codai/transcripts", StringComparison.Ordinal)
            && file.Value.Contains("question 19", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Replaces_the_older_turns_with_a_summary_and_keeps_the_recent_ones()
    {
        var (compactor, chat, history, ui, _) = Create();
        Seed(history, 5);
        chat.Enqueue("everything that mattered");

        var compacted = await compactor.CompactAsync();

        compacted.Should().BeTrue();
        history.Turns.Should().HaveCount(4);
        history.Turns[0].UserRequest.Should().Be(HistoryCompactor.SummaryTurnLabel);
        history.Turns[0].AiResponse.Should().Be("everything that mattered");

        // The tail is what the next request is built on, so it survives word for word. It is
        // measured in tokens rather than counted in turns, so what the test can pin down is
        // that it reaches back from the newest turn and that the summary is not part of it.
        history.Turns[^1].AiResponse.Should().Be("answer 4");
        history.Turns[^2].AiResponse.Should().Be("answer 3");

        ui.Successes.Should().Contain(message => message.Contains("2 turn", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Sends_the_conversation_to_the_model()
    {
        var (compactor, chat, history, _, _) = Create();
        Seed(history, 4);
        chat.Enqueue("summary");

        await compactor.CompactAsync();

        chat.Requests.Should().ContainSingle();
        chat.Requests[0].SystemPrompt.Should().Contain("<summary>");
        chat.Requests[0].UserMessage.Should().Contain("question 0").And.Contain("answer 1");

        // The turns that are staying are not summarised: they are still there to be read.
        chat.Requests[0].UserMessage.Should().NotContain("answer 3");
    }

    [Fact]
    public async Task The_turns_being_replaced_are_archived_and_the_archive_is_named()
    {
        var (compactor, chat, history, _, fileSystem) = Create();
        Seed(history, 5);
        chat.Enqueue("summary");

        await compactor.CompactAsync();

        var archive = fileSystem.Files.Should()
            .ContainSingle(file => file.Key.Contains(".codai/transcripts", StringComparison.Ordinal))
            .Which;

        archive.Value.Should().Contain("# Conversation archived before compaction");
        archive.Value.Should().Contain("question 0").And.Contain("answer 4");

        // The summary is told the path so that it can pass it on: the archive is only
        // worth writing if a later model knows it exists.
        chat.Requests[0].UserMessage.Should().Contain(".codai/transcripts/");
    }

    [Fact]
    public async Task A_summary_request_the_provider_refused_for_length_is_sent_again_without_its_start()
    {
        var (compactor, chat, history, _, _) = Create();
        Seed(history, 6);
        chat.Failures.Enqueue(new ChatProviderException("This model's maximum context length is 8192 tokens."));
        chat.Enqueue("summary after the retry");

        var compacted = await compactor.CompactAsync();

        compacted.Should().BeTrue();
        chat.Requests.Should().HaveCount(2);
        chat.Requests[0].UserMessage.Should().NotContain("truncated for compaction retry");

        // The retry says what it dropped, because a summary that silently loses the start of
        // the conversation reads as a conversation that started in the middle.
        chat.Requests[1].UserMessage.Should()
            .Contain("[earlier conversation truncated for compaction retry]")
            .And.Contain("were left out because");
        history.Turns[0].AiResponse.Should().Be("summary after the retry");
    }

    [Fact]
    public async Task The_files_the_session_touched_come_back_with_the_summary()
    {
        var chat = new ScriptedChatCompletionService();
        var history = new ChatHistory();
        var fileSystem = new InMemoryFileSystem(Root);
        fileSystem.AddFile(Path.Combine(Root, "src", "Parser.cs"), "class Parser { }");
        var options = new SessionOptions { ProjectRoot = Root, Provider = "openai", Model = "gpt-4o" };
        var footprint = new SessionFootprint();
        footprint.RecordFile("src/Parser.cs");
        var compactor = new HistoryCompactor(
            chat,
            history,
            new TokenTracker(),
            new ToolAccessPolicy(),
            new FakeUserInterface(),
            new TranscriptStore(fileSystem),
            options,
            footprint: footprint,
            restorer: new ContextRestorer(fileSystem));
        Seed(history, 5, padding: 2_000);
        chat.Enqueue("summary");

        await compactor.CompactAsync();

        history.Turns.Should().ContainSingle(turn => turn.IsCompactionSummary);
        history.Turns[0].AiResponse.Should()
            .Contain("summary")
            .And.Contain(ContextRestorer.Heading)
            .And.Contain("class Parser { }");
    }

    [Fact]
    public async Task A_restore_that_would_outgrow_the_turns_is_dropped_and_the_summary_kept()
    {
        var chat = new ScriptedChatCompletionService();
        var history = new ChatHistory();
        var fileSystem = new InMemoryFileSystem(Root);
        fileSystem.AddFile(Path.Combine(Root, "src", "Huge.cs"), new string('f', 40_000));
        var options = new SessionOptions { ProjectRoot = Root, Provider = "openai", Model = "gpt-4o" };
        var footprint = new SessionFootprint();
        footprint.RecordFile("src/Huge.cs");
        var compactor = new HistoryCompactor(
            chat,
            history,
            new TokenTracker(),
            new ToolAccessPolicy(),
            new FakeUserInterface(),
            new TranscriptStore(fileSystem),
            options,
            footprint: footprint,
            restorer: new ContextRestorer(fileSystem));
        Seed(history, 3);
        chat.Enqueue("s");

        (await compactor.CompactAsync()).Should().BeTrue();

        // A summary is only a compaction if it is shorter than what it replaced, so what
        // gives way is the restore, never the summary.
        history.Turns[0].AiResponse.Should().Be("s");
    }

    [Fact]
    public async Task A_long_turn_is_cut_down_before_it_is_summarised()
    {
        var (compactor, chat, history, _, _) = Create();
        Seed(history, 3, padding: HistoryCompactor.MaxTurnCharacters);
        chat.Enqueue("summary");

        await compactor.CompactAsync();

        chat.Requests[0].UserMessage.Should().Contain("(truncated)");
    }

    [Fact]
    public async Task A_summary_that_is_not_smaller_than_what_it_replaces_is_refused()
    {
        var (compactor, chat, history, ui, _) = Create();
        Seed(history, 3);
        chat.Enqueue(new string('s', 20_000));

        var compacted = await compactor.CompactAsync();

        compacted.Should().BeFalse();
        history.Turns.Should().HaveCount(3);
        ui.Errors.Should().Contain(message => message.Contains("left alone", StringComparison.Ordinal));

        // The turns are too small for the fallback to save anything from, which is the one
        // case where compaction really does have to give up.
        history.Turns.Should().NotContain(
            turn => turn.UserRequest == ConversationDigest.DroppedTurnsLabel);
    }

    [Fact]
    public async Task A_usable_fallback_shortens_the_conversation_when_the_summary_is_empty()
    {
        // Twelve turns and an empty answer from the model: the fallback drops the middle
        // rather than leaving a conversation that no longer fits. This is the difference
        // between a session that can carry on and one that has to be started again.
        var (compactor, chat, history, ui, _) = Create();
        Seed(history, 12, padding: 500);
        chat.Enqueue(string.Empty);

        var compacted = await compactor.CompactAsync();

        compacted.Should().BeTrue();
        history.Turns.Should().HaveCountLessThan(12);
        history.Turns[0].UserRequest.Should().Be(ConversationDigest.DroppedTurnsLabel);
        history.Turns[0].AiResponse.Should().Contain(".codai/transcripts/");
        history.Turns[^1].UserRequest.Should().Contain("question 11");
        ui.Errors.Should().Contain(message => message.Contains("empty summary", StringComparison.Ordinal));
        ui.Infos.Should().Contain(message => message.Contains("shortened instead", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_usable_fallback_shortens_turns_when_it_cannot_drop_them()
    {
        // Four turns, each far over the per-turn cap. Nothing has to be dropped: cutting
        // every turn down already shortens the conversation enough to be worth keeping it
        // whole, which is the better outcome when the cap can reach it.
        var (compactor, chat, history, ui, _) = Create();
        Seed(history, 4, padding: 100_000);
        chat.Enqueue(string.Empty);

        var compacted = await compactor.CompactAsync();

        compacted.Should().BeTrue();
        history.Turns.Should().HaveCount(4);
        history.Turns[0].AiResponse.Should().Contain("(truncated)");
        history.Turns.Should().NotContain(
            turn => turn.UserRequest == ConversationDigest.DroppedTurnsLabel);
        ui.Infos.Should().Contain(message => message.Contains("shortened instead", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_summary_over_the_cap_is_reported_but_still_used()
    {
        // Five thousand tokens of summary against ten thousand tokens of turns: over the
        // cap, and still worth having. Reporting beats refusing here — the history is
        // shorter either way, and the user is the one who can act on it.
        var (compactor, chat, history, ui, _) = Create();
        Seed(history, 4, padding: 20_000);
        chat.Enqueue(new string('s', 30_000));

        var compacted = await compactor.CompactAsync();

        compacted.Should().BeTrue();
        ui.Infos.Should().Contain(message => message.Contains("above the", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_summary_wrapped_in_markup_is_unwrapped_and_its_analysis_dropped()
    {
        var (compactor, chat, history, _, _) = Create();
        Seed(history, 3);
        chat.Enqueue("<analysis>thinking out loud</analysis><summary>what mattered</summary>");

        await compactor.CompactAsync();

        history.Turns[0].AiResponse.Should().Be("what mattered");
    }

    [Fact]
    public async Task A_focus_is_passed_on_as_something_to_weight()
    {
        var (compactor, chat, history, _, _) = Create();
        Seed(history, 4);
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
        var (compactor, chat, history, _, _) = Create();
        Seed(history, 4);
        chat.Enqueue("summary");

        await compactor.CompactAsync(instructions);

        chat.Requests[0].UserMessage.Should().NotContain("focus on");
    }

    [Fact]
    public async Task Refuses_to_compact_an_almost_empty_history()
    {
        var (compactor, chat, history, ui, _) = Create();
        Seed(history, HistoryCompactor.MinimumTurnsToCompact - 1);

        var compacted = await compactor.CompactAsync();

        compacted.Should().BeFalse();
        chat.Requests.Should().BeEmpty();
        history.Turns.Should().HaveCount(HistoryCompactor.MinimumTurnsToCompact - 1);

        // A note rather than an error: nothing was attempted and nothing went wrong, the
        // conversation is simply too short for a summary to be worth writing.
        ui.Infos.Should().Contain(message => message.Contains("Nothing to compact", StringComparison.Ordinal));
        ui.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task Keeps_the_history_when_the_model_answers_nothing()
    {
        var (compactor, chat, history, ui, _) = Create();
        Seed(history, 3);
        chat.Enqueue(string.Empty);

        var compacted = await compactor.CompactAsync();

        compacted.Should().BeFalse();
        history.Turns.Should().HaveCount(3);
        ui.Errors.Should().Contain(message => message.Contains("empty summary", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Records_the_tokens_the_summary_cost()
    {
        var chat = new ScriptedChatCompletionService();
        var history = new ChatHistory();
        var ui = new FakeUserInterface();
        var tokens = new TokenTracker();
        Seed(history, 3);
        chat.Enqueue("summary", new TokenUsage(90, 10));

        var compactor = new HistoryCompactor(
            chat,
            history,
            tokens,
            new ToolAccessPolicy(),
            ui,
            new TranscriptStore(new InMemoryFileSystem()),
            new SessionOptions { ProjectRoot = Root, Provider = "openai", Model = "gpt-4o" });

        await compactor.CompactAsync();

        tokens.SessionUsage.Should().Be(new TokenUsage(90, 10));
        ui.ProgressLabels.Should().Contain("Compacting conversation...");
    }

    [Fact]
    public async Task Basic_strategy_shortens_without_calling_the_model()
    {
        var (compactor, chat, history, _, _) = Create();
        Seed(history, 12, padding: 500);

        var plan = await compactor.TryPlanAsync(
            instructions: null,
            foreground: true,
            CancellationToken.None,
            strategy: CompactionStrategy.Basic,
            preserveRecentTokens: 2_000);

        plan.Should().NotBeNull();
        plan!.Method.Should().Be(CompactionMethod.Digest);
        chat.Requests.Should().BeEmpty();

        compactor.Apply(plan);

        history.Turns.Should().HaveCountLessThan(12);
    }

    [Fact]
    public async Task A_second_compaction_folds_only_turns_after_the_existing_summary()
    {
        var (compactor, chat, history, _, _) = Create();
        Seed(history, 5);
        chat.Enqueue("first summary");

        (await compactor.CompactAsync()).Should().BeTrue();
        var firstSummary = history.Turns[0];

        history.Add(new ChatTurn(DateTimeOffset.UtcNow, "new question", "new answer"));
        chat.Enqueue("second summary");

        var plan = await compactor.TryPlanAsync(
            instructions: null,
            foreground: true,
            CancellationToken.None,
            preserveRecentTokens: 1);

        plan.Should().NotBeNull();
        chat.Requests.Should().HaveCount(2);
        chat.Requests[1].UserMessage.Should().NotContain("answer 0");
        chat.Requests[1].UserMessage.Should().Contain(firstSummary.AiResponse);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_plan_cannot_restore_history_that_was_cleared_or_rewritten(bool rewrite)
    {
        var (compactor, chat, history, _, _) = Create();
        Seed(history, 6);
        chat.Enqueue("old conversation summary");
        var plan = await compactor.TryPlanAsync(null, foreground: false, CancellationToken.None);
        plan.Should().NotBeNull();
        history.Clear();
        if (rewrite)
        {
            for (var turn = 0; turn < 6; turn++)
            {
                history.Add(new ChatTurn(DateTimeOffset.UtcNow, $"different {turn}", "new conversation"));
            }
        }
        var current = history.Turns.ToArray();

        Action apply = () => compactor.Apply(plan!);

        apply.Should().Throw<InvalidOperationException>().WithMessage("*history changed*");
        history.Turns.Should().Equal(current);
    }

    [Fact]
    public async Task A_plan_preserves_turns_appended_after_its_snapshot()
    {
        var (compactor, chat, history, _, _) = Create();
        Seed(history, 6);
        chat.Enqueue("earlier work");
        var plan = await compactor.TryPlanAsync(null, foreground: false, CancellationToken.None);
        plan.Should().NotBeNull();
        var appended = new ChatTurn(DateTimeOffset.UtcNow, "latest requirement", "latest response");
        history.Add(appended);

        compactor.Apply(plan!);

        history.Turns[0].AiResponse.Should().Be("earlier work");
        history.Turns[^1].Should().Be(appended);
    }

    [Fact]
    public void An_oversized_token_tail_leaves_nothing_to_replace()
    {
        var turns = Enumerable.Range(0, 5)
            .Select(index => new ChatTurn(
                DateTimeOffset.UtcNow,
                $"question {index}",
                $"answer {index}"))
            .ToArray();

        HistoryCompactor.FindCutIndex(turns, int.MaxValue).Should().Be(0);
    }

    [Theory]
    [InlineData("gpt-4")]
    [InlineData("gpt-4.1")]
    public async Task Summary_input_including_previous_checkpoint_fits_model_budget(string model)
    {
        var (compactor, chat, history, _, _) = Create(model);
        history.Add(new ChatTurn(DateTimeOffset.UtcNow, HistoryCompactor.SummaryTurnLabel,
            "ORIGINAL_CONSTRAINT " + new string('s', 500_000), ChatTurnKind.CompactionSummary));
        Seed(history, 6, padding: 2_000);
        chat.Enqueue("bounded checkpoint");

        await compactor.TryPlanAsync(null, foreground: true, CancellationToken.None, preserveRecentTokens: 1);

        var request = chat.Requests.Should().ContainSingle().Subject;
        var inputBudget = Math.Min(HistoryCompactor.MaxSummaryInputTokens,
            (int)(ContextWindow.For(model) * 0.75));
        TokenEstimator.EstimateRequest(request.SystemPrompt, request.UserMessage)
            .Should().BeLessThanOrEqualTo(inputBudget);
        request.UserMessage.Should().Contain("ORIGINAL_CONSTRAINT").And.Contain("question 3");
        request.AllowedTools.Should().BeEmpty();
    }

    [Fact]
    public async Task Clipped_summary_transcript_keeps_trailing_diagnostics()
    {
        var (compactor, chat, history, _, _) = Create();
        history.Add(new ChatTurn(DateTimeOffset.UtcNow, "investigate failure",
            "START_DIAGNOSTIC " + new string('x', 20_000) + " END_DIAGNOSTIC"));
        Seed(history, 2);
        chat.Enqueue("diagnostic summary");

        await compactor.TryPlanAsync(null, foreground: true, CancellationToken.None, preserveRecentTokens: 1);

        chat.Requests.Should().ContainSingle().Which.UserMessage.Should()
            .Contain("START_DIAGNOSTIC").And.Contain("END_DIAGNOSTIC");
    }
}
