using Codai.Cli;
using Codai.Cli.Ui;
using FluentAssertions;
using Spectre.Console;

namespace Codai.Session.Tests;

public class WorkspaceTrustPanelTests
{
    [Fact]
    public void Enter_defaults_to_trusting_the_directory()
    {
        var panel = new WorkspaceTrustPanel("/project");

        panel.HandleKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));

        panel.Closed.Should().BeTrue();
        panel.Command.Should().Be("trust");
    }

    [Theory]
    [InlineData(ConsoleKey.Enter, "leave")]
    [InlineData(ConsoleKey.Escape, "leave")]
    public void Highlighting_no_requires_enter_and_escape_always_leaves(ConsoleKey key, string expected)
    {
        var panel = new WorkspaceTrustPanel("/project");
        panel.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false));
        panel.Closed.Should().BeFalse();

        panel.HandleKey(new ConsoleKeyInfo('\0', key, false, false, false));

        panel.Command.Should().Be(expected);
    }

    [Theory]
    [InlineData(100, 30)]
    [InlineData(40, 24)]
    public void Warning_directory_and_decisions_are_visible_together(int width, int height)
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.No });
        console.Profile.Width = width;
        console.Profile.Height = height;

        new WorkspaceTrustPanel("/work/[untrusted]/project").Draw(console);

        var output = writer.ToString();
        output.Should().Contain("Workspace access").And.Contain("/work/[untrusted]/project")
            .And.Contain("> Yes").And.Contain("No")
            .And.Contain("Enter to confirm").And.NotContain("navigate");
        output.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n').Should().OnlyContain(line => line.Length <= width);
    }

    [Theory]
    [InlineData(null, "leave")]
    [InlineData("", "trust")]
    [InlineData("yes", "trust")]
    [InlineData("Y", "trust")]
    [InlineData("n", "leave")]
    [InlineData("no", "leave")]
    [InlineData("invalid", "leave")]
    [InlineData("trust", "trust")]
    public async Task Plain_terminal_defaults_to_yes_but_eof_and_no_decline(string? response, string expected)
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.No });
        using var ui = new SpectreUserInterface(console, new ResponseInput(response));

        (await ui.RequestWorkspaceTrustAsync("/project", CancellationToken.None)).Should().Be(expected);

        writer.ToString().Should().Contain("/project").And.Contain("[Y/n]");
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", true)]
    [InlineData("yes", true)]
    [InlineData("no", false)]
    [InlineData("invalid", false)]
    public async Task Plain_confirmation_defaults_to_yes_without_accepting_eof(string? response, bool expected)
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer),
            Ansi = AnsiSupport.No,
            Interactive = InteractionSupport.Yes,
        });
        using var ui = new SpectreUserInterface(console, new ResponseInput(response));

        (await ui.ConfirmAsync("Continue?", CancellationToken.None)).Should().Be(expected);

        writer.ToString().Should().Contain("[Y/n]");
    }

    private sealed class ResponseInput(string? response) : IConsoleInput
    {
        public string? ReadLine() => response;
    }
}
