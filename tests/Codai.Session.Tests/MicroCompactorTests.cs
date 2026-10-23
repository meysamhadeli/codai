using Codai.Core.Chat;
using Codai.Core.Context;
using Codai.Session.Tests.Fakes;
using FluentAssertions;

namespace Codai.Session.Tests;

/// <summary>
/// Covers the tier that runs before a summary is paid for: clearing the bulk of old tool
/// output without a model call.
/// </summary>
public class MicroCompactorTests
{
    private const string Root = @"C:\project";

    /// <summary>Characters per field that put a turn well over what trimming will clear.</summary>
    private const int BulkPadding = 100_000;

    private static (
        MicroCompactor Compactor,
        ChatHistory History,
        FakeUserInterface Ui,
        InMemoryFileSystem FileSystem) Create()
    {
        var history = new ChatHistory();
        var ui = new FakeUserInterface();
        var fileSystem = new InMemoryFileSystem();
        var options = new SessionOptions { ProjectRoot = Root, Provider = "openai", Model = "gpt-4o" };

        return (
            new MicroCompactor(history, new TranscriptStore(fileSystem), options, ui),
            history,
            ui,
            fileSystem);
    }

    private static ChatTurn Bulk(string request) =>
        new(DateTimeOffset.UtcNow, request, new string('a', BulkPadding) + "LAST-ERROR-LINE");

    private static ContextPressure PressureOf(ChatHistory history, int windowTokens) =>
        ContextPressure.Measure(MicroCompaction.Estimate(history.Turns), windowTokens);

    [Fact]
    public async Task The_middle_of_an_old_turn_is_cleared_and_what_it_saved_is_reported()
    {
        var (compactor, history, ui, _) = Create();
        history.Add(Bulk("read the build log"));
        history.Add(new ChatTurn(DateTimeOffset.UtcNow, "question 1", "answer 1"));
        history.Add(new ChatTurn(DateTimeOffset.UtcNow, "question 2", "answer 2"));

        var saved = await compactor.TrimAsync(PressureOf(history, windowTokens: 20_000));

        saved.Should().BeGreaterThan(0);
        history.Turns[0].AiResponse.Should().Contain(MicroCompaction.ElisionMarker).And.EndWith("LAST-ERROR-LINE");
        ui.Infos.Should().Contain(message => message.Contains("Cleared", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_newest_turns_are_left_exactly_as_they_are()
    {
        var (compactor, history, _, _) = Create();
        history.Add(Bulk("read the build log"));
        history.Add(Bulk("read the second log"));
        history.Add(Bulk("the turn being worked on"));

        await compactor.TrimAsync(PressureOf(history, windowTokens: 20_000));

        history.Turns[^1].AiResponse.Should().HaveLength(BulkPadding + "LAST-ERROR-LINE".Length);
    }

    [Fact]
    public async Task What_is_cleared_is_archived_first()
    {
        var (compactor, history, _, fileSystem) = Create();
        history.Add(Bulk("read the build log"));
        history.Add(new ChatTurn(DateTimeOffset.UtcNow, "question 1", "answer 1"));
        history.Add(new ChatTurn(DateTimeOffset.UtcNow, "question 2", "answer 2"));

        await compactor.TrimAsync(PressureOf(history, windowTokens: 20_000));

        var archive = fileSystem.Files.Should()
            .ContainSingle(file => file.Key.Contains(".codai/transcripts", StringComparison.Ordinal))
            .Which;

        // The whole response is in the archive even though the conversation no longer has it,
        // which is what makes clearing recoverable rather than destructive.
        archive.Value.Should().Contain(new string('a', BulkPadding));
    }

    [Fact]
    public async Task A_conversation_with_room_to_spare_is_left_alone()
    {
        var (compactor, history, ui, fileSystem) = Create();
        history.Add(Bulk("read the build log"));
        history.Add(new ChatTurn(DateTimeOffset.UtcNow, "question 1", "answer 1"));

        var saved = await compactor.TrimAsync(PressureOf(history, windowTokens: 1_000_000));

        saved.Should().Be(0);
        history.Turns[0].AiResponse.Should().NotContain(MicroCompaction.ElisionMarker);
        ui.Infos.Should().BeEmpty();
        fileSystem.Files.Should().BeEmpty();
    }

    [Fact]
    public async Task A_summary_turn_is_never_cleared()
    {
        var (compactor, history, _, _) = Create();
        history.Add(new ChatTurn(
            DateTimeOffset.UtcNow,
            HistoryCompactor.SummaryTurnLabel,
            new string('s', BulkPadding),
            ChatTurnKind.CompactionSummary));
        history.Add(new ChatTurn(DateTimeOffset.UtcNow, "question 1", "answer 1"));
        history.Add(new ChatTurn(DateTimeOffset.UtcNow, "question 2", "answer 2"));

        var saved = await compactor.TrimAsync(PressureOf(history, windowTokens: 20_000));

        saved.Should().Be(0);
        history.Turns[0].AiResponse.Should().HaveLength(BulkPadding);
    }
}
