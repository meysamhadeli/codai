using Codai.Cli;
using Codai.Cli.Ui;
using FluentAssertions;
using Spectre.Console;

namespace Codai.Session.Tests;

public sealed class MouseScrollTests
{
    [Theory]
    [InlineData(24, true)]
    [InlineData(100, true)]
    [InlineData(24, false)]
    [InlineData(100, false)]
    public void Scrollbar_uses_thin_outermost_rail_and_proportional_thumb_without_wrapping(int width, bool unicode)
    {
        using var writer = new StringWriter();
        var console = CreateConsole(writer);
        console.Profile.Width = width;
        var transcript = new ChatTranscript { Unicode = unicode };
        transcript.Add(Lines());
        transcript.Draw(console, 18);

        var output = writer.ToString();
        var glyph = unicode ? "\u2595" : "\\|";
        var cells = System.Text.RegularExpressions.Regex.Matches(output,
            $"\u001b\\[(\\d+);{width}H\u001b\\[38;5;(237|247)m{glyph}\u001b\\[0m\u001b\\[\\1;{width}H");
        cells.Should().HaveCount(18);
        cells.Count(cell => cell.Groups[2].Value == "247").Should().Be(5);
        cells.TakeLast(5).Should().OnlyContain(cell => cell.Groups[2].Value == "247");
        output.Should().NotContain("\u2588").And.NotContain("\u2502");
        output.Should().NotContain("\u001b[48;5;237m").And.NotContain("\u001b[48;5;247m");

        writer.GetStringBuilder().Clear();
        transcript.Draw(console, 18);
        writer.ToString().Should().BeEmpty();

        transcript.Scroll(1000);
        writer.GetStringBuilder().Clear();
        transcript.Draw(console, 18);
        var top = System.Text.RegularExpressions.Regex.Matches(writer.ToString(),
            $"\u001b\\[(\\d+);{width}H\u001b\\[38;5;(237|247)m{glyph}\u001b\\[0m\u001b\\[\\1;{width}H");
        top.Take(5).Should().OnlyContain(cell => cell.Groups[2].Value == "247");
    }

    [Fact]
    public async Task Wheel_scrolls_transcript_both_ways_without_moving_footer()
    {
        using var writer = new StringWriter();
        var console = CreateConsole(writer);
        using var ui = new SpectreUserInterface(console, new Input());
        await ui.ReadInputAsync();
        ui.WriteLine(Lines());
        writer.GetStringBuilder().Clear();

        ui.HandleMouse(new(30, 8, TerminalMouseAction.ScrollUp)).Should().BeTrue();
        writer.ToString().Should().Contain("row057").And.NotContain("row060");
        writer.ToString().Should().Contain("\u001b[19;1H").And.Contain("configured model");
        writer.GetStringBuilder().Clear();

        ui.HandleMouse(new(30, 8, TerminalMouseAction.ScrollDown)).Should().BeTrue();
        writer.ToString().Should().Contain("row060").And.Contain("\u001b[19;1H");
    }

    [Fact]
    public async Task Scrollbar_click_and_drag_reach_both_ends_and_release_ends_drag()
    {
        using var writer = new StringWriter();
        var console = CreateConsole(writer);
        using var ui = new SpectreUserInterface(console, new Input());
        await ui.ReadInputAsync();
        ui.WriteLine(Lines());
        writer.GetStringBuilder().Clear();

        ui.HandleMouse(new(99, 1, TerminalMouseAction.Press)).Should().BeFalse();
        ui.HandleMouse(new(100, 1, TerminalMouseAction.Press)).Should().BeTrue();
        writer.ToString().Should().Contain("row001").And.NotContain("row060");
        writer.GetStringBuilder().Clear();
        ui.HandleMouse(new(98, 24, TerminalMouseAction.Drag)).Should().BeTrue();
        writer.ToString().Should().Contain("row060").And.NotContain("row001");

        ui.HandleMouse(new(100, 18, TerminalMouseAction.Release));
        ui.HandleMouse(new(100, 1, TerminalMouseAction.Drag)).Should().BeFalse();
        ui.HandleMouse(new(10, 1, TerminalMouseAction.Press)).Should().BeFalse();
        ui.HandleMouse(new(100, 24, TerminalMouseAction.Press)).Should().BeFalse();
    }

    [Fact]
    public async Task Wheel_remains_available_while_response_is_running()
    {
        using var writer = new StringWriter();
        var console = CreateConsole(writer);
        using var ui = new SpectreUserInterface(console, new Input());
        await ui.ReadInputAsync();
        ui.WriteLine(Lines());

        await ui.RunTurnAsync(_ =>
        {
            writer.GetStringBuilder().Clear();
            ui.HandleMouse(new(30, 8, TerminalMouseAction.ScrollUp)).Should().BeTrue();
            writer.ToString().Should().Contain("row057").And.NotContain("row060");
            return Task.FromResult(true);
        });
    }

    [Fact]
    public async Task Modal_consumes_mouse_input_without_scrolling_background()
    {
        using var writer = new StringWriter();
        var console = CreateConsole(writer);
        var input = new Input();
        using var ui = new SpectreUserInterface(console, input);
        await ui.ReadInputAsync();
        ui.WriteLine(Lines());
        input.BeforeKey = () => ui.HandleMouse(new(30, 8, TerminalMouseAction.ScrollUp)).Should().BeFalse();

        (await ui.AskQuestionAsync("Choose", ["one", "two"])).Should().Be("one");
    }

    [Fact]
    public async Task Mouse_reporting_is_enabled_and_disabled_with_viewport()
    {
        using var writer = new StringWriter();
        var console = CreateConsole(writer);
        using var ui = new SpectreUserInterface(console, new Input());
        ui.HandleMouse(new(30, 8, TerminalMouseAction.ScrollUp)).Should().BeFalse();
        await ui.ReadInputAsync();
        writer.ToString().Should().Contain("\u001b[?1003h\u001b[?1006h");
        writer.GetStringBuilder().Clear();

        ui.RestoreTerminal();
        writer.ToString().Should().Contain("\u001b[?1003l\u001b[?1006l\u001b[?1049l");
        ui.HandleMouse(new(30, 8, TerminalMouseAction.ScrollUp)).Should().BeFalse();
    }

    [Fact]
    public void Scrollbar_mapping_clamps_and_handles_short_transcripts()
    {
        var transcript = new ChatTranscript();
        transcript.Add(Lines());
        transcript.GetLines(80);
        transcript.ScrollToRow(-20, 18);
        transcript.ScrollOffset.Should().Be(42);
        transcript.ScrollToRow(100, 18);
        transcript.ScrollOffset.Should().Be(0);
        transcript.ScrollToRow(8, 18);
        transcript.ScrollOffset.Should().BeInRange(20, 24);
        transcript.ScrollToRow(0, 100);
        transcript.ScrollOffset.Should().Be(0);
    }

    private static string Lines() => string.Join('\n', Enumerable.Range(1, 60).Select(number => $"row{number:000}"));

    private static IAnsiConsole CreateConsole(StringWriter writer)
    {
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer),
            Ansi = AnsiSupport.Yes,
            Interactive = InteractionSupport.Yes,
        });
        console.Profile.Width = 100;
        console.Profile.Height = 24;
        return console;
    }

    private sealed class Input : IConsoleInput
    {
        public Action? BeforeKey { get; set; }
        public string? ReadLine() => "hello";

        public bool TryReadKey(out ConsoleKeyInfo key)
        {
            key = default;
            if (BeforeKey is not { } action)
            {
                return false;
            }
            BeforeKey = null;
            action();
            key = new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false);
            return true;
        }
    }
}
