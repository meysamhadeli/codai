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
    [Theory]
    [InlineData("red")]
    [InlineData("bold deepskyblue1 on grey19")]
    public void Nerd_icons_override_only_the_foreground_color(string style)
    {
        var glyph = TerminalIcons.For(TerminalIcon.Edit, unicode: true, nerdFont: true);
        TerminalIcons.White($"[{style}]{glyph} Update[/]")
            .Should().Be($"[{style}][white]{glyph}[/] Update[/]");
        TerminalIcons.White("[red]* failed[/]").Should().Be("[red]* failed[/]");
    }

    [Theory]
    [InlineData("read_file", "\uf0f6")]
    [InlineData("write_file", "\uf0c7")]
    [InlineData("edit_file", "\uf044")]
    [InlineData("apply_patch", "\uf044")]
    [InlineData("grep", "\uf002")]
    [InlineData("run_command", "\uf120")]
    [InlineData("fetch_web", "\uf0ac")]
    [InlineData("read_skill", "\uf0e7")]
    [InlineData("mcp_docs_search", "\uf1e6")]
    [InlineData("delegate_agent", "\uf007")]
    public void Action_icons_are_distinct_single_cell_glyphs_with_safe_fallbacks(string tool, string glyph)
    {
        var icon = TerminalIcons.Tool(tool);
        TerminalIcons.For(icon, unicode: true, nerdFont: true).Should().Be(glyph);
        glyph.GetCellWidth().Should().Be(1);
        TerminalIcons.For(icon, unicode: true, nerdFont: false).Should().Be("\u25cf");
        TerminalIcons.For(icon, unicode: false, nerdFont: true).Should().Be("*");
    }

    [Theory]
    [InlineData(24, true)]
    [InlineData(80, true)]
    [InlineData(24, false)]
    public void Inline_disclosure_icon_expands_and_collapses_tool_output(int width, bool unicode)
    {
        var transcript = new ChatTranscript { Unicode = unicode };
        AddTool(transcript, "sample.txt", "one\ntwo\nthree\nfour\nfive");
        using var writer = new StringWriter();
        var console = Console(writer);
        console.Profile.Width = width + 3;
        transcript.Draw(console, 15);

        Text(transcript, width).Should().Contain(unicode ? "\u25b8 2 more lines" : "> 2 more lines")
            .And.NotContain("ctrl+o").And.NotContain("five");
        transcript.ToggleStepAtRow(RowOf(transcript, console, 15, "2 more lines"), 15).Should().BeTrue();
        Text(transcript, width).Should().Contain("five").And.Contain(unicode ? "\u25be" : "v");

        transcript.Draw(console, 15);
        transcript.ToggleStepAtRow(RowOf(transcript, console, 15, unicode ? "\u25be" : "    v"), 15).Should().BeTrue();
        Text(transcript, width).Should().NotContain("five").And.Contain("2 more lines");
        transcript.ToggleTool().Should().BeTrue();
        Text(transcript, width).Should().Contain("five");
    }

    [Theory]
    [InlineData(24)]
    [InlineData(80)]
    public void Transcript_keeps_thoughts_tools_and_public_messages_in_chronological_rows(int width)
    {
        var transcript = new ChatTranscript();
        transcript.StartActivity("Reasoning");
        transcript.StartTool("run_command", "python --version");
        transcript.ToolResult("run_command", "Python 3.14.4");
        transcript.CompleteTool("run_command", true, "done");
        transcript.AppendMarkdown("Writing the game:", completeActivity: false);
        transcript.StartActivity("Reasoning");
        transcript.AppendMarkdown("Game ready.", completeActivity: false);
        transcript.CompleteActivity();

        var plain = Text(transcript, width);
        plain.Should().Contain("Thought for").And.Contain("Bash(python --version)")
            .And.Contain("Python 3.14.4").And.Contain("Writing the game:").And.Contain("Game ready.")
            .And.NotContain("╭").And.NotContain("╰").And.NotContain("Worked for");
        plain.IndexOf("Thought for", StringComparison.Ordinal).Should().BeLessThan(plain.IndexOf("Bash(", StringComparison.Ordinal));
        plain.IndexOf("Python 3.14.4", StringComparison.Ordinal).Should().BeLessThan(plain.IndexOf("Writing the game:", StringComparison.Ordinal));
        transcript.GetLines(width).Select(line => System.Text.RegularExpressions.Regex.Replace(line, "\u001b\\[[0-?]*[ -/]*[@-~]", string.Empty))
            .Should().OnlyContain(line => line.GetCellWidth() <= width);
    }

    [Theory]
    [InlineData(24, false)]
    [InlineData(44, true)]
    [InlineData(110, true)]
    public void Unframed_transcript_retains_public_progress_and_mcp_results(int width, bool unicode)
    {
        var transcript = new ChatTranscript { Unicode = unicode };
        transcript.StartActivity("Thinking");
        transcript.AppendMarkdown("PUBLIC_PROGRESS_START " + new string('x', 180) + " DETAIL_END", completeActivity: false);
        transcript.StartTool("mcp_docs_search", "correlation IDs");

        var live = Text(transcript, width);
        live.Should().NotContain("╭").And.Contain("mcp_docs_search").And.Contain("DETAIL_END");
        transcript.GetLines(width).Select(line => System.Text.RegularExpressions.Regex.Replace(line, "\u001b\\[[0-?]*[ -/]*[@-~]", string.Empty))
            .Should().OnlyContain(line => line.GetCellWidth() <= width);
        transcript.ToggleTool().Should().BeTrue();
        Text(transcript, width).Should().Contain("DETAIL_END");
        transcript.ToolOutput("mcp_docs_search", "RESULT");
        using var writer = new StringWriter();
        var console = Console(writer);
        console.Profile.Width = width;
        transcript.Draw(console, 40);
        var toolRow = RowOf(transcript, console, 40, "mcp_docs");
        transcript.ToggleStepAtRow(toolRow, 40).Should().BeTrue();
        Text(transcript, width).Should().Contain("RESULT");
        transcript.CompleteTool("mcp_docs_search", true, "Search complete");
        transcript.AppendMarkdown("Final answer", completeActivity: false);
        transcript.CompleteActivity();
        Text(transcript, width).Should().Contain("Final answer").And.Contain("mcp_docs_search").And.Contain("DETAIL_END");
    }

    [Theory]
    [InlineData(24)]
    [InlineData(80)]
    public void Running_activity_and_action_animate_without_changing_row_geometry(int width)
    {
        var transcript = new ChatTranscript { Accent = "cyan1" };
        transcript.StartActivity("Thinking");
        var first = string.Join('\n', transcript.GetLines(width, 0));
        var second = string.Join('\n', transcript.GetLines(width, 1));

        first.Should().NotBe(second);
        var firstPlain = System.Text.RegularExpressions.Regex.Replace(first, "\u001b\\[[0-?]*[ -/]*[@-~]", string.Empty);
        var secondPlain = System.Text.RegularExpressions.Regex.Replace(second, "\u001b\\[[0-?]*[ -/]*[@-~]", string.Empty);
        firstPlain.Should().StartWith("\u2800\u2800 ").And.Contain("Thinking")
            .And.NotContain("v ").And.NotContain("> ");
        secondPlain.Should().MatchRegex(@"[\u2800-\u283f][\u2800-\u2807] ");
        first.Should().Contain("\u001b[38;5;51mThinking").And.NotContain("\u001b[1;38;5;51m");
        second.Should().Contain("\u001b[38;5;51mThinking");
        firstPlain.Split('\n').Count(line => line.Contains("esc", StringComparison.Ordinal)).Should().Be(1);
        firstPlain.Should().NotContain("action");
        firstPlain.Split('\n').Select(line => line.GetCellWidth()).Should()
            .Equal(secondPlain.Split('\n').Select(line => line.GetCellWidth()));
        secondPlain.Split('\n').Should().OnlyContain(line => line.GetCellWidth() <= width);

        transcript.StartTool("read_file", "sample.txt");
        transcript.CompleteTool("read_file", true, "Read file");
        transcript.AppendMarkdown("Answer");
        string.Join('\n', transcript.GetLines(width, 2)).Should().Be(string.Join('\n', transcript.GetLines(width, 3)));
    }

    [Fact]
    public void Streaming_public_progress_remains_visible_without_activity_box()
    {
        var transcript = new ChatTranscript();
        transcript.StartActivity("Thinking");
        transcript.AppendMarkdown(string.Join('\n', Enumerable.Repeat("Output line  ", 40)), completeActivity: false);
        using var writer = new StringWriter();
        var console = Console(writer);
        transcript.Draw(console, 8);

        var plain = System.Text.RegularExpressions.Regex.Replace(writer.ToString(), "\u001b\\[[0-?]*[ -/]*[@-~]", string.Empty);
        plain.Should().Contain("Output line").And.Contain("esc interrupt)");
        Text(transcript).Should().Contain("Thought for").And.NotContain("╭");
        transcript.ToggleTool().Should().BeFalse();
        transcript.CompleteActivity();
        writer.GetStringBuilder().Clear();
        transcript.Draw(console, 8);
        writer.ToString().Should().NotContain("esc interrupt");
    }

    [Theory]
    [InlineData(3)]
    [InlineData(9)]
    public void Activity_label_stays_solid_while_dot_grid_animates(long frame)
    {
        var transcript = new ChatTranscript { Accent = "cyan1" };
        transcript.StartActivity("Thinking");

        string.Join('\n', transcript.GetLines(80, frame)).Should().Contain("\u001b[38;5;51mThinking...");
    }

    [Fact]
    public void Tool_preview_stays_visible_and_expansion_reveals_remaining_output()
    {
        var transcript = new ChatTranscript();
        transcript.StartTool("run_command", "dotnet test");
        transcript.ToolOutput("run_command", "live output\nsecond\nthird\nfourth\n");
        Text(transcript).Should().Contain("dotnet test").And.Contain("esc interrupt)").And.Contain("live output").And.NotContain("fourth");
        transcript.CompleteTool("run_command", true, "finished");
        Text(transcript).Should().Contain("Bash(dotnet test)").And.NotContain("(done)").And.Contain("live output")
            .And.Contain("\u25b8 1 more lines").And.NotContain("ctrl+o");

        transcript.ToggleTool().Should().BeTrue();
        Text(transcript).Should().Contain("fourth");
        transcript.ToolOutput("run_command", "later output\n");
        Text(transcript).Should().Contain("later output");
        transcript.ToggleTool().Should().BeTrue();
        Text(transcript).Should().Contain("live output").And.NotContain("fourth").And.NotContain("later output");
    }

    [Fact]
    public void Clicking_opens_only_selected_step_and_keyboard_toggles_that_selection()
    {
        using var writer = new StringWriter();
        var console = Console(writer);
        var transcript = new ChatTranscript();
        AddTool(transcript, "one.txt", "one\ntwo\nthree\nfirst details");
        AddTool(transcript, "two.txt", "one\ntwo\nthree\nsecond details");
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
        transcript.Scroll(35);
        transcript.Draw(console, 10);
        var row = RowOf(transcript, console, 10, "target.txt");

        transcript.ToggleStepAtRow(row, 10).Should().BeTrue();
        transcript.Draw(console, 10);
        RowOf(transcript, console, 10, "target.txt").Should().Be(row);
        Text(transcript).Should().Contain("detail 20").And.Contain("other details");

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
    public void Edit_summary_shows_real_counts_and_colored_inline_diff()
    {
        var transcript = new ChatTranscript();
        transcript.StartTool("write_file", "new.cs");
        transcript.CompleteTool("write_file", true, "written");
        transcript.ToolResult("write_file", ToolResult.Success("Created new.cs") with
        {
            FileChanges = [new("new.cs", 40, 0, "@@ -1,0 +7,1 @@\n+hidden source")],
        });

        Text(transcript).Should().Contain("Write(new.cs)").And.Contain("Added 40 lines, removed 0 lines")
            .And.Contain("7 +hidden source").And.NotContain("Created new.cs");
        string.Join('\n', transcript.GetLines(80)).Should().Contain("48;2;0;51;0");
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

        Text(transcript).Should().Contain("landed.txt").And.Contain("Added 2 lines, removed 1 line").And.Contain("(failed)");
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
        transcript.GetLines(80).Should().HaveCount(1);
        Text(transcript).Should().Contain("Reasoning").And.NotContain("Elapsed:")
            .And.NotContain("private model reasoning");
        transcript.AppendMarkdown("Public answer");

        Text(transcript).Should().Contain("Thought for").And.Contain("Public answer").And.NotContain("Elapsed:");
        transcript.ToggleTool();
        Text(transcript).Should().Contain("Thought for").And.NotContain("Reasoning: done").And.NotContain("Elapsed:");
        transcript.StartTool("read_file", "next.txt");
        Text(transcript).Should().Contain("Thought for");
    }

    [Fact]
    public void Actions_and_results_remain_visible_after_answer()
    {
        var transcript = new ChatTranscript();
        transcript.StartActivity("Reasoning");
        AddTool(transcript, "one.txt", "first details");
        AddTool(transcript, "two.txt", "second details");

        var plain = System.Text.RegularExpressions.Regex.Replace(Text(transcript), "\u001b\\[[0-?]*[ -/]*[@-~]", string.Empty);
        plain.Should().Contain("Thought for").And.NotContain("(esc interrupt").And.NotContain("2 actions")
            .And.Contain("Read(one.txt)").And.Contain("Read(two.txt)").And.Contain("first details").And.NotContain("Elapsed:");
        transcript.AppendMarkdown("Public answer");
        Text(transcript).Should().Contain("Thought for").And.NotContain("2 actions").And.Contain("Public answer")
            .And.Contain("one.txt").And.Contain("two.txt");

        transcript.ToggleTool().Should().BeTrue();
        Text(transcript).Should().Contain("one.txt").And.Contain("two.txt").And.Contain("first details");
        transcript.ToggleTool().Should().BeTrue();
        Text(transcript).Should().Contain("one.txt").And.Contain("two.txt");
        transcript.ToggleTool().Should().BeTrue();
        using var writer = new StringWriter();
        var console = Console(writer);
        transcript.Draw(console, 12);
        transcript.ToggleStepAtRow(RowOf(transcript, console, 12, "one.txt"), 12).Should().BeTrue();
        Text(transcript).Should().Contain("first details").And.Contain("second details");
        transcript.ToggleTool().Should().BeTrue();
        Text(transcript).Should().Contain("first details");
    }

    [Fact]
    public void Completed_activity_does_not_inherit_recoverable_tool_failure_status()
    {
        var transcript = new ChatTranscript();
        transcript.StartActivity("Reasoning");
        transcript.StartTool("read_file", "missing.txt");
        transcript.CompleteTool("read_file", false, "File not found");
        transcript.AppendMarkdown("Could not read the file.");

        Text(transcript).Should().Contain("Thought for").And.NotContain("Reasoning (failed)").And.NotContain("1 action")
            .And.Contain("missing.txt").And.Contain("(failed)");
        transcript.ToggleTool();
        Text(transcript).Should().Contain("Read(missing.txt)").And.Contain("(failed)");
    }

    [Fact]
    public void New_actions_keep_previous_results_visible()
    {
        var transcript = new ChatTranscript();
        transcript.StartActivity("Thinking");
        AddTool(transcript, "one.txt", "first details");
        using var writer = new StringWriter();
        var console = Console(writer);
        transcript.Draw(console, 12);
        AddTool(transcript, "two.txt", "second details");

        Text(transcript).Should().Contain("Thought for").And.NotContain("2 actions").And.Contain("one.txt").And.Contain("two.txt")
            .And.Contain("first details").And.Contain("second details");
        transcript.ToggleTool().Should().BeTrue();
        Text(transcript).Should().Contain("one.txt").And.Contain("two.txt");
    }

    [Theory]
    [InlineData(24)]
    [InlineData(44)]
    [InlineData(110)]
    public void Public_progress_tools_and_final_answer_remain_visible(int width)
    {
        var transcript = new ChatTranscript();
        transcript.StartActivity("Thinking");
        transcript.AppendMarkdown("FIRST_PROGRESS: looking for documentation", completeActivity: false);
        AddTool(transcript, "one.txt", "PRIVATE_TOOL_DETAILS");
        transcript.AppendMarkdown("SECOND_PROGRESS: checking another source", completeActivity: false);
        AddTool(transcript, "two.txt", "MORE_TOOL_DETAILS");

        var live = Text(transcript, width);
        live.Should().Contain("FIRST_PROGRESS").And.Contain("one.txt").And.Contain("two.txt")
            .And.Contain("PRIVATE_TOOL_DETAILS").And.Contain("MORE_TOOL_DETAILS");
        transcript.AppendMarkdown("FINAL_ANSWER: verified result", completeActivity: false);
        transcript.CompleteActivity();

        Text(transcript, width).Should().Contain("FINAL_ANSWER").And.Contain("FIRST_PROGRESS")
            .And.Contain("SECOND_PROGRESS").And.Contain("one.txt").And.Contain("two.txt");
    }

    [Theory]
    [InlineData(24)]
    [InlineData(44)]
    [InlineData(110)]
    public void Assistant_marker_and_tool_timing_fit_without_navigation_tabs(int width)
    {
        var transcript = new ChatTranscript();
        transcript.AppendMarkdown("Hello. Ready.");
        transcript.StartTool("run_command", "Check existing source and validate syntax");
        transcript.CompleteTool("run_command", true, "done");

        var lines = transcript.GetLines(width).Select(line =>
            System.Text.RegularExpressions.Regex.Replace(line, "\u001b\\[[0-?]*[ -/]*[@-~]", string.Empty)).ToArray();
        string.Join('\n', lines).Should().Contain("\u25cf").And.Contain("Bash(")
            .And.NotContain("(done)").And.NotContain("Sessions").And.NotContain("Pull requests");
        lines.Should().OnlyContain(line => line.GetCellWidth() <= width);
    }

    [Theory]
    [InlineData(24)]
    [InlineData(44)]
    [InlineData(110)]
    public void Submitted_prompt_uses_unpadded_strip_without_timestamp(int width)
    {
        var transcript = new ChatTranscript();
        transcript.AddUser("Hi\nSecond line");
        var lines = transcript.GetLines(width).Select(line =>
            System.Text.RegularExpressions.Regex.Replace(line, "\u001b\\[[0-?]*[ -/]*[@-~]", string.Empty)).ToArray();

        lines.Should().HaveCount(2);
        lines[0].Should().StartWith("\u276f Hi");
        lines[1].Should().StartWith("  Second line");
        System.Text.RegularExpressions.Regex.IsMatch(lines[0], @"\d{2}:\d{2}$").Should().BeFalse();
        lines.Should().OnlyContain(line => line.GetCellWidth() <= width);
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
        Text(transcript).Should().Contain($"Thinking ({status})").And.NotContain("Elapsed:");
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
        writer.ToString().Should().Contain("Added 1 line").And.Contain("+source");
        writer.GetStringBuilder().Clear();

        var stepRow = Enumerable.Range(1, 18).First(row => ui.HandleMouse(new(4, row, TerminalMouseAction.Press)));
        writer.ToString().Should().Contain("configured model");
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
        var row = lines.FindIndex(line => System.Text.RegularExpressions.Regex.Replace(line, "\u001b\\[[0-?]*[ -/]*[@-~]", string.Empty)
            .Contains(text, StringComparison.Ordinal)) - first + 1;
        row.Should().BeInRange(1, rows);
        return row;
    }

    private static string Text(ChatTranscript transcript, int width = 80) =>
        System.Text.RegularExpressions.Regex.Replace(string.Join('\n', transcript.GetLines(width)), "\u001b\\[[0-?]*[ -/]*[@-~]", string.Empty);

    private static IAnsiConsole Console(StringWriter writer)
    {
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer),
            Ansi = AnsiSupport.Yes,
            Interactive = InteractionSupport.Yes,
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
