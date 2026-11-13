using System.Globalization;
using Codai.Core;
using Codai.Core.Abstractions;
using Spectre.Console;

namespace Codai.Cli.Ui;

internal sealed class DecisionPanel(string title, string body, IReadOnlyList<SessionMenuItem>? items = null) : IInlineDecision
{
    private readonly ComposerBuffer _answer = new([]);
    private int _selected;
    private int _bodyOffset;

    public bool Closed { get; private set; }
    public string? Command { get; private set; }
    public bool ShowCursor => items is null;

    public void HandleKey(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Escape)
        {
            Closed = true;
        }
        else if (key.Key == ConsoleKey.Enter)
        {
            Command = items is null ? _answer.Text : items.Count > 0 ? items[_selected].Value : null;
            Closed = true;
        }
        else if (key.Key is ConsoleKey.PageUp or ConsoleKey.PageDown)
        {
            _bodyOffset = Math.Max(0, _bodyOffset + (key.Key == ConsoleKey.PageUp ? -3 : 3));
        }
        else if (items is null)
        {
            if (_answer.Text.Length < 4096 || char.IsControl(key.KeyChar))
            {
                _answer.HandleKey(key);
            }
        }
        else if (key.Key is ConsoleKey.UpArrow or ConsoleKey.DownArrow or ConsoleKey.Tab)
        {
            var direction = key.Key == ConsoleKey.UpArrow || key.Modifiers.HasFlag(ConsoleModifiers.Shift) ? -1 : 1;
            _selected = Math.Clamp(_selected + direction, 0, Math.Max(0, items.Count - 1));
        }
        else if (key.KeyChar is >= '1' and <= '9' && key.KeyChar - '1' < items.Count)
        {
            _selected = key.KeyChar - '1';
            Command = items[_selected].Value;
            Closed = true;
        }
    }

    public int MeasureHeight(int width, int maximumHeight)
    {
        var inner = Math.Max(1, width - 6);
        var bodyRows = BodyLines(inner).Length;
        var choiceRows = items?.Select((item, index) => SettingsPalette.WrapText(ChoiceLabel(item, index),
            Math.Max(1, inner - 2), int.MaxValue).Length).Sum() ?? 2;
        return Math.Min(maximumHeight, 7 + Math.Min(bodyRows, 10) + choiceRows);
    }

    public void Draw(IAnsiConsole console, string theme = "graphite", int minimumTop = 1)
    {
        var inner = Math.Max(1, console.Profile.Width - 6);
        var height = MeasureHeight(console.Profile.Width, Math.Max(1, console.Profile.Height - minimumTop));
        var accent = TerminalThemes.Find(theme).Accent;
        var highlight = "bold " + accent;
        List<(string Text, string Style)> lines =
        [
            (title, "bold default"),
            (new string(console.Profile.Capabilities.Unicode ? '\u2500' : '-', inner), "grey23"),
        ];
        var choices = items?.Select((item, index) => SettingsPalette.WrapText(
            ChoiceLabel(item, index), Math.Max(1, inner - 2), int.MaxValue)).ToArray();
        var choiceBudget = Math.Min(choices?.Sum(choice => choice.Length) ?? 2, Math.Max(1, height - 7));
        var bodyLines = BodyLines(inner);
        var bodyBudget = Math.Max(0, height - 6 - choiceBudget);
        _bodyOffset = Math.Clamp(_bodyOffset, 0, Math.Max(0, bodyLines.Length - bodyBudget));
        lines.AddRange(bodyLines.Skip(_bodyOffset).Take(bodyBudget).Select(line => (line, "default")));
        lines.Add((string.Empty, "default"));
        var cursorRow = 0;
        var cursorColumn = 0;
        if (items is null)
        {
            lines.Add(("Answer:", "grey62"));
            var answer = ComposerLayout.Create(_answer.Text, _answer.Cursor, inner, 1);
            cursorRow = lines.Count;
            cursorColumn = answer.CursorColumn;
            lines.Add((answer.Lines.Count > 0 ? answer.Lines[0] : string.Empty, "white on grey11"));
        }
        else if (choices is not null)
        {
            var first = _selected;
            var used = choices[_selected].Length;
            while (first > 0 && used + choices[first - 1].Length <= choiceBudget)
            {
                used += choices[--first].Length;
            }
            for (var index = first; index < choices.Length && choiceBudget > 0; index++)
            {
                var firstLine = true;
                foreach (var line in choices[index].Take(choiceBudget))
                {
                    lines.Add(((index == _selected && firstLine ? "> " : "  ") + line, index == _selected ? highlight : "default"));
                    firstLine = false;
                    choiceBudget--;
                }
            }
        }
        while (lines.Count < height - 3)
        {
            lines.Add((string.Empty, "default"));
        }
        lines.Add((SelectionFooter(inner, console.Profile.Capabilities.Unicode), "grey62"));
        DrawFrame(console, lines, height, minimumTop);
        if (ShowCursor && console.Profile.Capabilities.Ansi)
        {
            console.Write(new ControlCode(string.Create(CultureInfo.InvariantCulture,
                $"\u001b[{Math.Max(2, minimumTop) + cursorRow + 1};{4 + cursorColumn}H")));
        }
    }

    private string ChoiceLabel(SessionMenuItem item, int index) =>
        items is { Count: 2 } && items[0].Label == "Yes" && items[1].Label == "No"
            ? item.Label : $"{index + 1}. {item.Label}";

    internal static string SelectionFooter(int width, bool unicode) => width < 33
        ? "Enter confirm  Esc cancel"
        : unicode ? "Enter to confirm \u00b7 Esc to cancel" : "Enter to confirm - Esc to cancel";

    private string[] BodyLines(int width) => TranscriptMarkdown.Clean(body)
        .Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n')
        .SelectMany(line => SettingsPalette.WrapText(line, width, int.MaxValue)).ToArray();

    internal static void DrawFrame(IAnsiConsole console, IReadOnlyList<(string Text, string Style)> lines,
        int height, int minimumTop, bool unicode = true)
    {
        var width = Math.Max(1, console.Profile.Width - 2);
        var inner = Math.Max(1, width - 4);
        var top = Math.Max(2, minimumTop);
        unicode &= console.Profile.Capabilities.Unicode;
        if (minimumTop == 1 && console.Profile.Capabilities.Ansi)
        {
            console.Write(new ControlCode("\u001b[1;1H\u001b[2K"));
        }
        for (var row = 0; row < Math.Max(height, console.Profile.Height - top + 1); row++)
        {
            if (console.Profile.Capabilities.Ansi)
            {
                console.Write(new ControlCode($"\u001b[{top + row};1H\u001b[2K\u001b[{top + row};2H"));
            }
            if (row < height)
            {
                if (row == 0 || row == height - 1)
                {
                    var left = unicode ? row == 0 ? "\u256d" : "\u2570" : "+";
                    var right = unicode ? row == 0 ? "\u256e" : "\u256f" : "+";
                    console.Write(new Text(left + new string(unicode ? '\u2500' : '-', Math.Max(0, width - 2)) + right, Style.Parse("grey23")));
                }
                else
                {
                    var line = row - 1 < lines.Count ? lines[row - 1] : (string.Empty, "default");
                    var text = ComposerLayout.Clip(TranscriptMarkdown.Clean(line.Item1), inner);
                    console.Write(new Text(unicode ? "\u2502 " : "| ", Style.Parse("grey23")));
                    console.Write(TerminalIcons.Render(text + new string(' ', Math.Max(0, inner - text.GetCellWidth())), line.Item2));
                    console.Write(new Text(unicode ? " \u2502" : " |", Style.Parse("grey23")));
                }
            }
            if (!console.Profile.Capabilities.Ansi)
            {
                console.WriteLine();
            }
        }
    }
}
