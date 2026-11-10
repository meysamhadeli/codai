using System.Text.Json;
using Codai.Cli.Ui;
using Codai.Core.Tools;
using FluentAssertions;
using Spectre.Console;

namespace Codai.Session.Tests;

public class ApprovalPanelTests
{
    private static ToolApprovalRequest Command() => new(
        "run_command",
        "Run a shell command in the project.",
        "run_command(command=python --version, timeout_seconds=60)",
        "Run a shell command in the project.",
        "python --version && python -c \"import pygame\"");

    private static string Plain(StringWriter writer) =>
        System.Text.RegularExpressions.Regex.Replace(writer.ToString(), "\u001b\\[[0-?]*[ -/]*[@-~]", string.Empty);

    [Fact]
    public void Short_approval_sizes_to_contents_and_reserves_conversation_space()
    {
        var panel = new ApprovalPanel(Command());

        panel.MeasureHeight(110, 36).Should().BeLessThan(18);
        panel.MeasureHeight(44, 18).Should().BeLessThanOrEqualTo(18);
    }

    [Fact]
    public void The_panel_names_the_thing_being_approved()
    {
        var panel = new ApprovalPanel(Command(), unicode: false);

        panel.Title.Should().Be("Run this command?");
        panel.Options[0].Should().Be("Yes");
        panel.Options[1].Should().Be("No");
        panel.Options[2].Should().Contain("don't ask again for run_command");
    }

    [Fact]
    public void The_body_shows_the_whole_command_and_the_explanation()
    {
        var body = string.Join('\n', new ApprovalPanel(Command()).Body());

        body.Should().Contain("python --version && python -c");
        body.Should().Contain("Run a shell command in the project.");
    }

    [Fact]
    public void File_approval_includes_path_and_proposed_contents_not_only_summary()
    {
        var request = ToolApprovalRequest.Create("write_file", "Write a file.", "short summary",
            JsonSerializer.SerializeToElement(new { path = "source.cs", content = "first line\nlast line" }));

        request.Preview.Should().Be("source.cs\n\nfirst line\nlast line");
    }

    [Theory]
    [InlineData(0, "[Empty file contents]")]
    [InlineData(60000, "x")]
    [InlineData(60001, "[Preview truncated]")]
    public void File_approval_bounds_contents_and_marks_empty_or_truncated_writes(int length, string ending)
    {
        var request = ToolApprovalRequest.Create("write_file", "Write a file.", "short summary",
            JsonSerializer.SerializeToElement(new { path = "source.cs", content = new string('x', length) }));

        request.Preview.Should().StartWith("source.cs\n\n").And.EndWith(ending);
        request.Preview!.Length.Should().BeLessThan(60100);
    }

    [Fact]
    public void Long_previews_are_cut_with_a_count_of_what_was_left_out()
    {
        var request = Command() with
        {
            Preview = string.Join('\n', Enumerable.Range(1, 30).Select(line => $"line {line}")),
        };

        var body = string.Join('\n', new ApprovalPanel(request).Body());

        body.Should().Contain("… 20 more lines").And.NotContain("line 30");
    }

    [Fact]
    public void Enter_approves_the_highlighted_choice()
    {
        var panel = new ApprovalPanel(Command());

        panel.HandleKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));

        panel.Closed.Should().BeTrue();
        panel.Command.Should().Be(ApprovalPanel.Allow);
    }

    [Fact]
    public void Down_then_enter_refuses()
    {
        var panel = new ApprovalPanel(Command());

        panel.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false));
        panel.HandleKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));

        panel.Selected.Should().Be(1);
        panel.Command.Should().Be(ApprovalPanel.Deny);
    }

    [Fact]
    public void Number_keys_pick_a_choice_directly()
    {
        var second = new ApprovalPanel(Command());
        second.HandleKey(new ConsoleKeyInfo('2', ConsoleKey.D2, false, false, false));
        second.Command.Should().Be(ApprovalPanel.Deny);

        var third = new ApprovalPanel(Command());
        third.HandleKey(new ConsoleKeyInfo('3', ConsoleKey.D3, false, false, false));
        third.Command.Should().Be(ApprovalPanel.AllowAlways);
    }

    [Fact]
    public void Escape_refuses()
    {
        var panel = new ApprovalPanel(Command());

        panel.HandleKey(new ConsoleKeyInfo('\u001b', ConsoleKey.Escape, false, false, false));

        panel.Closed.Should().BeTrue();
        panel.Command.Should().Be(ApprovalPanel.Deny);
    }

    [Theory]
    [InlineData(ConsoleKey.Y, 'y', ApprovalPanel.Allow)]
    [InlineData(ConsoleKey.D1, '1', ApprovalPanel.Allow)]
    [InlineData(ConsoleKey.N, 'n', ApprovalPanel.Deny)]
    [InlineData(ConsoleKey.A, 'a', ApprovalPanel.AllowAlways)]
    public void Direct_shortcuts_ignore_the_highlight(ConsoleKey key, char character, string expected)
    {
        var panel = new ApprovalPanel(Command());
        panel.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false));

        panel.HandleKey(new ConsoleKeyInfo(character, key, false, false, false));

        panel.Command.Should().Be(expected);
        panel.Closed.Should().BeTrue();
    }

    [Fact]
    public void Arrow_keys_stay_inside_the_list()
    {
        var panel = new ApprovalPanel(Command());

        for (var press = 0; press < 5; press++)
        {
            panel.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, false, false, false));
        }
        panel.Selected.Should().Be(0);

        for (var press = 0; press < 8; press++)
        {
            panel.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false));
        }
        panel.Selected.Should().Be(2);
        panel.Closed.Should().BeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_panel_frames_the_command_choices_and_keyboard_footer(bool unicode)
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.Yes });
        console.Profile.Width = 100;
        console.Profile.Height = 30;

        new ApprovalPanel(Command(), unicode).Draw(console);

        var output = Plain(writer);
        output.Should().Contain("Run this command?")
            .And.Contain("python --version")
            .And.Contain("Do you want to proceed?")
            .And.Contain("1. Yes")
            .And.Contain("don't ask again for run_command")
            .And.Contain("Esc")
            .And.Contain(unicode && console.Profile.Capabilities.Unicode ? "\u256d" : "+")
            .And.Contain("Enter");
    }

    [Fact]
    public void The_panel_erases_the_full_width_of_each_covered_row()
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer),
            Ansi = AnsiSupport.Yes,
            Interactive = InteractionSupport.Yes,
        });
        console.Profile.Width = 100;
        console.Profile.Height = 30;

        new ApprovalPanel(Command()).Draw(console);

        // The panel is drawn over the transcript, so every row it paints must be erased
        // from column 1 first; otherwise transcript text in the margins bleeds through
        // the border and the panel looks dirty.
        writer.ToString().Should().Contain(";1H\u001b[2K");
        writer.ToString().Should().Contain("\u001b[2;2H");
        writer.ToString().Should().Contain("\u001b[30;1H\u001b[2K");
    }

    [Fact]
    public void The_selected_choice_is_marked()
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.No });
        console.Profile.Width = 100;
        console.Profile.Height = 30;
        var panel = new ApprovalPanel(Command(), unicode: false);
        panel.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false));

        panel.Draw(console);

        Plain(writer).Should().Contain("> 2. No");
    }

    [Theory]
    [InlineData(24, 12)]
    [InlineData(44, 22)]
    public void Compact_approval_keeps_preview_and_all_decisions(int width, int height)
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.Yes });
        console.Profile.Width = width;
        console.Profile.Height = height;

        new ApprovalPanel(Command()).Draw(console);

        Plain(writer).Should().Contain("python").And.Contain("1.").And.Contain("2.").And.Contain("3.");
    }

    [Fact]
    public void Preview_paging_keeps_decision_selection_and_can_reach_last_line()
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.Yes });
        console.Profile.Width = 100;
        console.Profile.Height = 30;
        var panel = new ApprovalPanel(Command() with { Preview = string.Join('\n', Enumerable.Range(1, 30).Select(line => $"line {line}")) });
        panel.Draw(console);
        for (var page = 0; page < 4; page++)
        {
            writer.GetStringBuilder().Clear();
            panel.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.PageDown, false, false, false));
            panel.Draw(console);
        }

        Plain(writer).Should().Contain("line 30").And.Contain("1. Yes").And.Contain("Esc");
        panel.Selected.Should().Be(0);
        panel.Closed.Should().BeFalse();
    }
}
