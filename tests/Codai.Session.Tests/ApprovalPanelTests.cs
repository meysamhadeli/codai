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
    public void The_panel_names_the_thing_being_approved()
    {
        var panel = new ApprovalPanel(Command(), unicode: false);

        panel.Title.Should().Be("Run this command?");
        panel.Options[0].Should().Be("Yes");
        panel.Options[1].Should().Contain("don't ask again for run_command");
        panel.Options[2].Should().Contain("tell Codai what to do instead");
    }

    [Fact]
    public void The_body_shows_the_whole_command_and_the_explanation()
    {
        var body = string.Join('\n', new ApprovalPanel(Command()).Body());

        body.Should().Contain("python --version && python -c");
        body.Should().Contain("Run a shell command in the project.");
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
    public void Down_then_enter_records_a_remembered_approval()
    {
        var panel = new ApprovalPanel(Command());

        panel.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false));
        panel.HandleKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));

        panel.Selected.Should().Be(1);
        panel.Command.Should().Be(ApprovalPanel.AllowAlways);
    }

    [Fact]
    public void Number_keys_pick_a_choice_directly()
    {
        var second = new ApprovalPanel(Command());
        second.HandleKey(new ConsoleKeyInfo('2', ConsoleKey.D2, false, false, false));
        second.Command.Should().Be(ApprovalPanel.AllowAlways);

        var third = new ApprovalPanel(Command());
        third.HandleKey(new ConsoleKeyInfo('3', ConsoleKey.D3, false, false, false));
        third.Command.Should().Be(ApprovalPanel.Deny);
    }

    [Fact]
    public void Escape_refuses()
    {
        var panel = new ApprovalPanel(Command());

        panel.HandleKey(new ConsoleKeyInfo('\u001b', ConsoleKey.Escape, false, false, false));

        panel.Closed.Should().BeTrue();
        panel.Command.Should().Be(ApprovalPanel.Deny);
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
    public void The_panel_draws_a_border_the_command_and_the_choices(bool unicode)
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
            .And.Contain(unicode ? "╭" : "+")
            .And.Contain(unicode ? "│" : "|");
    }

    [Fact]
    public void The_panel_erases_the_full_width_of_each_covered_row()
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.Yes, Interactive = InteractionSupport.Yes,
        });
        console.Profile.Width = 100;
        console.Profile.Height = 30;

        new ApprovalPanel(Command()).Draw(console);

        // The panel is drawn over the transcript, so every row it paints must be erased
        // from column 1 first; otherwise transcript text in the margins bleeds through
        // the border and the panel looks dirty.
        writer.ToString().Should().Contain(";1H\u001b[2K");
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

        Plain(writer).Should().Contain("> 2. Yes, and don't ask again");
    }
}
