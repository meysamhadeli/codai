using Codai.Cli;
using FluentAssertions;

namespace Codai.Session.Tests;

public sealed class TerminalMouseInputTests
{
    [Theory]
    [InlineData("\u001b[<64;80;3M", TerminalMouseAction.ScrollUp)]
    [InlineData("\u001b[<65;80;3M", TerminalMouseAction.ScrollDown)]
    [InlineData("\u001b[<80;80;3M", TerminalMouseAction.ScrollUp)]
    [InlineData("\u001b[<0;80;3M", TerminalMouseAction.Press)]
    [InlineData("\u001b[<32;80;3M", TerminalMouseAction.Drag)]
    [InlineData("\u001b[<0;80;3m", TerminalMouseAction.Release)]
    public void Sgr_reports_decode_without_becoming_draft_keys(string report, TerminalMouseAction action)
    {
        var parser = new TerminalMouseParser();
        TerminalMouseEvent? mouse = null;
        foreach (var character in report)
        {
            mouse = parser.Push(Key(character));
        }

        mouse.Should().Be(new TerminalMouseEvent(80, 3, action));
        parser.TryReadKey(out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("\u001b[<1;80;3M")]
    [InlineData("\u001b[<66;80;3M")]
    [InlineData("\u001b[<67;80;3M")]
    public void Unsupported_mouse_buttons_are_consumed_without_typing_or_scrolling(string report)
    {
        var parser = new TerminalMouseParser();
        foreach (var character in report)
        {
            parser.Push(Key(character)).Should().BeNull();
        }
        parser.TryReadKey(out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("\u001bx")]
    [InlineData("\u001b[1;5A")]
    [InlineData("\u001b[<invalid")]
    [InlineData("\u001b[<0;0;3M")]
    public void Non_mouse_input_is_preserved(string text)
    {
        var parser = new TerminalMouseParser();
        foreach (var character in text)
        {
            parser.Push(Key(character));
        }
        parser.Flush();
        List<char> keys = [];
        while (parser.TryReadKey(out var key))
        {
            keys.Add(key.KeyChar);
        }
        new string(keys.ToArray()).Should().Be(text);
    }

    [Fact]
    public void Fragmented_reports_stay_buffered_and_escape_can_be_flushed()
    {
        var parser = new TerminalMouseParser();
        foreach (var character in "\u001b[<64;")
        {
            parser.Push(Key(character));
        }
        parser.TryReadKey(out _).Should().BeFalse();
        foreach (var character in "10;")
        {
            parser.Push(Key(character));
        }
        parser.Push(Key('5'));
        parser.Push(Key('M')).Should().Be(new TerminalMouseEvent(10, 5, TerminalMouseAction.ScrollUp));
        parser.Push(Key('\u001b'));
        parser.Flush();
        parser.TryReadKey(out var escape).Should().BeTrue();
        escape.Key.Should().Be(ConsoleKey.Escape);
    }

    [Fact]
    public async Task Bare_escape_is_released_after_mouse_report_timeout()
    {
        var parser = new TerminalMouseParser();
        parser.Push(Key('\u001b'));
        parser.TryReadKey(out _).Should().BeFalse();

        await Task.Delay(150);
        parser.FlushExpired();

        parser.TryReadKey(out var escape).Should().BeTrue();
        escape.Key.Should().Be(ConsoleKey.Escape);
    }

    [Theory]
    [InlineData(0x00780000u, 4u, TerminalMouseAction.ScrollUp)]
    [InlineData(0xFF880000u, 4u, TerminalMouseAction.ScrollDown)]
    [InlineData(1u, 0u, TerminalMouseAction.Press)]
    [InlineData(1u, 1u, TerminalMouseAction.Drag)]
    [InlineData(0u, 0u, TerminalMouseAction.Release)]
    public void Windows_mouse_records_decode_wheel_and_drag(uint buttons, uint flags, TerminalMouseAction action) =>
        WindowsConsoleMouseInput.DecodeAction(buttons, flags).Should().Be(action);

    private static ConsoleKeyInfo Key(char character) =>
        new(character, character == '\u001b' ? ConsoleKey.Escape : ConsoleKey.NoName, false, false, false);
}
