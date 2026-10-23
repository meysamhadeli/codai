using System.Text.Json;
using Codai.Cli;
using Codai.Cli.Ui;
using Codai.Core.Tools;
using Codai.Tools;
using FluentAssertions;
using Spectre.Console;

namespace Codai.Session.Tests;

public sealed class TranscriptStepTests
{
    [Fact]
    public void Running_and_finished_tool_bodies_stay_closed_until_selected()
    {
        var transcript = new ChatTranscript();
        transcript.StartTool("run_command", "dotnet test");
        transcript.ToolOutput("run_command", "live output\n");
        Text(transcript).Should().Contain("dotnet test").And.Contain("(running)").And.NotContain("live output");
        transcript.CompleteTool("run_command", true, "finished");
        Text(transcript).Should().Contain("(done)").And.NotContain("live output");

        transcript.ToggleTool().Should().BeTrue();
        Text(transcript).Should().Contain("live output");
        transcript.ToolOutput("run_command", "later output\n");
        Text(transcript).Should().Contain("later output");
        transcript.ToggleTool().Should().BeTrue();
        Text(transcript).Should().NotContain("live output").And.NotContain("later output");
    }

    [Fact]
    public void Clicking_opens_only_selected_step_and_keyboard_toggles_that_selection()
    {
        using var writer = new StringWriter();
        var console = Console(writer);
        var transcript = new ChatTranscript();
        AddTool(transcript, "one.txt", "first details");
        AddTool(transcript, "two.txt", "second details");
        transcript.Draw(console, 12);

        transcript.ToggleStepAtRow(1, 12).Should().BeTrue();
        Text(transcript).Should().Contain("first details").And.NotContain("second details");
        transcript.Draw(console, 12);
        transcript.ToggleStepAtRow(2, 12).Should().BeFalse("an expanded body is not another header");
        transcript.ToggleTool().Should().BeTrue();
        Text(transcript).Should().NotContain("first details").And.NotContain("second details");
    }

    [Fact]
    public void Header_hit_testing_tracks_scrolling_wrapping_resize_and_expansion()
    {
        using var writer = new StringWriter();
        var console = Console(writer);
        var transcript = new ChatTranscript();
        transcript.Add(string.Join('\n', Enumerable.Range(1, 30).Select(index => $"history {index}")));
        AddTool(transcript, "target.txt", string.Join('\n', Enumerable.Range(1, 20).Select(index => $"detail {index}")));
        AddTool(transcript, "other.txt", "other details");
        transcript.Add(string.Join('\n', Enumerable.Range(1, 30).Select(index => $"tail {index}")));
        transcript.Draw(console, 10);
        transcript.Scroll(25);
        transcript.Draw(console, 10);
        var row = RowOf(transcript, console, 10, "target.txt");

        transcript.ToggleStepAtRow(row, 10).Should().BeTrue();
        transcript.Draw(console, 10);
        RowOf(transcript, console, 10, "target.txt").Should().Be(row);
        Text(transcript).Should().Contain("detail 20").And.NotContain("other details");

        console.Profile.Width = 38;
        transcript.Draw(console, 10);
        row = RowOf(transcript, console, 10, "target.txt");
        transcript.ToggleStepAtRow(row, 10).Should().BeTrue();
        Text(transcript, 35).Should().NotContain("detail 20");
        transcript.Draw(console, 10);
        transcript.ToggleStepAtRow(0, 10).Should().BeFalse();
        transcript.ToggleStepAtRow(11, 10).Should().BeFalse();
    }

    [Fact]
    public void Edit_summary_shows_real_counts_while_diff_starts_closed()
    {
        var transcript = new ChatTranscript();
        transcript.StartTool("write_file", "new.cs");
        transcript.CompleteTool("write_file", true, "written");
        transcript.ToolResult("write_file", ToolResult.Success("Created new.cs") with
        {
            FileChanges = [new("new.cs", 40, 0, "+hidden source")],
        });

        Text(transcript).Should().Contain("Edited new.cs").And.Contain("+40").And.Contain("-0")
            .And.NotContain("hidden source").And.NotContain("Created new.cs");
        transcript.ToggleTool();
        Text(transcript).Should().Contain("+hidden source");
    }

    [Fact]
    public void Failed_partial_patch_shows_landed_counts_and_failure_without_claiming_success()
    {
        var transcript = new ChatTranscript();
        transcript.StartTool("apply_patch", "");
        transcript.CompleteTool("apply_patch", false, "missing file");
        transcript.ToolResult("apply_patch", ToolResult.Failure("missing file") with
        {
            FileChanges = [new("landed.txt", 2, 1, "-old\n+new\n+more")],
        });

        Text(transcript).Should().Contain("landed.txt").And.Contain("+2").And.Contain("-1").And.Contain("(failed)");
        transcript.ToggleTool();
        Text(transcript).Should().Contain("missing file").And.Contain("+more");
    }

    [Fact]
    public void Thinking_and_reasoning_share_one_status_step_and_finish_before_answer()
    {
        var transcript = new ChatTranscript();
        transcript.StartActivity("Thinking");
        transcript.StartActivity("Thinking");
        transcript.StartActivity("Reasoning");
        transcript.GetLines(80).Should().ContainSingle();
        Text(transcript).Should().Contain("Reasoning").And.Contain("(running)");
        transcript.AppendMarkdown("Public answer");

        Text(transcript).Should().Contain("Finished reasoning").And.Contain("Public answer").And.NotContain("Elapsed:");
        transcript.ToggleTool();
        Text(transcript).Should().Contain("Elapsed:").And.Contain("private model reasoning is not displayed");
        transcript.StartTool("read_file", "next.txt");
        Text(transcript).Should().Contain("Finished reasoning");
    }

    [Theory]
    [InlineData("failed")]
    [InlineData("stopped")]
    public void Unfinished_activity_records_its_actual_outcome(string status)
    {
        var transcript = new ChatTranscript();
        transcript.StartActivity("Thinking");
        transcript.CompleteActivity(status);

        Text(transcript).Should().Contain($"({status})").And.NotContain("Finished thinking");
        transcript.ToggleTool();
        Text(transcript).Should().Contain($"Thinking: {status}");
    }

    [Fact]
    public void Clear_discards_old_click_targets_and_selection()
    {
        using var writer = new StringWriter();
        var console = Console(writer);
        var transcript = new ChatTranscript();
        AddTool(transcript, "old.txt", "old details");
        transcript.Draw(console, 10);
        transcript.Clear();

        transcript.ToggleStepAtRow(1, 10).Should().BeFalse();
        transcript.ToggleTool().Should().BeFalse();
        transcript.StartActivity("Thinking");
        Text(transcript).Should().NotContain("old.txt");
    }

    [Fact]
    public async Task Ui_mouse_opens_action_in_place_without_disrupting_footer_or_scrollbar()
    {
        using var writer = new StringWriter();
        var console = Console(writer);
        using var ui = new SpectreUserInterface(console, new Input());
        await ui.ReadInputAsync();
        var root = Path.Combine(Path.GetTempPath(), "codai-step-tests");
        var files = new Fakes.InMemoryFileSystem(root);
        var runner = new ToolRunner(new ToolRegistry([new WriteFileTool()]), AlwaysApproveToolApprover.Instance,
            new InteractiveToolObserver(ui), files, root);
        var result = await runner.ExecuteAsync("write_file",
            JsonSerializer.SerializeToElement(new { path = "test.txt", content = "source" }));
        result.IsSuccess.Should().BeTrue();
        (await files.ReadAllTextAsync(Path.Combine(root, "test.txt"))).Should().Be("source");
        writer.ToString().Should().Contain("+1").And.NotContain("+source");
        writer.GetStringBuilder().Clear();

        var stepRow = Enumerable.Range(1, 18).First(row => ui.HandleMouse(new(4, row, TerminalMouseAction.Press)));
        writer.ToString().Should().Contain("+source").And.Contain("[Agent]");
        writer.GetStringBuilder().Clear();
        ui.HandleMouse(new(4, stepRow, TerminalMouseAction.Press)).Should().BeTrue();
        writer.ToString().Should().NotContain("+source");
        ui.HandleMouse(new(4, 21, TerminalMouseAction.Press)).Should().BeFalse();
    }

    [Fact]
    public async Task Ui_turn_failure_finishes_reasoning_as_failed()
    {
        using var writer = new StringWriter();
        var console = Console(writer);
        using var ui = new SpectreUserInterface(console, new Input());
        await ui.ReadInputAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => ui.RunTurnAsync<int>(_ =>
        {
            ui.WriteInfo("Reasoning...");
            throw new InvalidOperationException("provider failed");
        }));

        writer.ToString().Should().Contain("(failed)");
    }

    private static void AddTool(ChatTranscript transcript, string path, string output)
    {
        transcript.StartTool("read_file", path);
        transcript.CompleteTool("read_file", true, "done");
        transcript.ToolResult("read_file", output);
    }

    private static int RowOf(ChatTranscript transcript, IAnsiConsole console, int rows, string text)
    {
        var lines = transcript.GetLines(console.Profile.Width - 3).ToList();
        var first = Math.Max(0, lines.Count - rows - transcript.ScrollOffset);
        var row = lines.FindIndex(line => line.Contains(text, StringComparison.Ordinal)) - first + 1;
        row.Should().BeInRange(1, rows);
        return row;
    }

    private static string Text(ChatTranscript transcript, int width = 80) => string.Join('\n', transcript.GetLines(width));

    private static IAnsiConsole Console(StringWriter writer)
    {
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.Yes, Interactive = InteractionSupport.Yes,
        });
        console.Profile.Width = 83;
        console.Profile.Height = 24;
        return console;
    }

    private sealed class Input : IConsoleInput
    {
        public string? ReadLine() => "hello";
    }
}
