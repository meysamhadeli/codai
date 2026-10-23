namespace Codai.Cli.Ui;

public sealed class ComposerBuffer(IReadOnlyList<string> history)
{
    private int _historyIndex = history.Count;
    private string _draft = string.Empty;

    public string Text { get; private set; } = string.Empty;

    public int Cursor { get; private set; }

    public void Insert(string value)
    {
        Text = Text.Insert(Cursor, value);
        Cursor += value.Length;
    }

    public void Replace(int start, int length, string value)
    {
        Text = Text.Remove(start, length).Insert(start, value);
        Cursor = start + value.Length;
    }

    public bool HandleKey(ConsoleKeyInfo key)
    {
        var control = key.Modifiers.HasFlag(ConsoleModifiers.Control);
        switch (key.Key)
        {
            case ConsoleKey.Enter when key.Modifiers.HasFlag(ConsoleModifiers.Shift) || key.Modifiers.HasFlag(ConsoleModifiers.Alt):
                Insert("\n");
                break;
            case ConsoleKey.Backspace:
                if (Cursor > 0)
                {
                    var start = PreviousPosition(Cursor);
                    Text = Text.Remove(start, Cursor - start);
                    Cursor = start;
                }
                break;
            case ConsoleKey.Delete:
                if (Cursor < Text.Length)
                {
                    Text = Text.Remove(Cursor, NextPosition(Cursor) - Cursor);
                }
                break;
            case ConsoleKey.LeftArrow:
                Cursor = control ? WordStart() : PreviousPosition(Cursor);
                break;
            case ConsoleKey.RightArrow:
                Cursor = control ? WordEnd() : NextPosition(Cursor);
                break;
            case ConsoleKey.Home:
                Cursor = control ? 0 : LineStart();
                break;
            case ConsoleKey.End:
                Cursor = control ? Text.Length : LineEnd();
                break;
            case ConsoleKey.UpArrow:
                if (LineStart() > 0)
                {
                    var column = Cursor - LineStart();
                    var end = LineStart() - 1;
                    var start = end == 0 ? 0 : Text.LastIndexOf('\n', end - 1) + 1;
                    Cursor = Math.Min(start + column, end);
                }
                else
                {
                    NavigateHistory(-1);
                }
                break;
            case ConsoleKey.DownArrow:
                if (LineEnd() < Text.Length)
                {
                    var column = Cursor - LineStart();
                    var start = LineEnd() + 1;
                    var end = Text.IndexOf('\n', start);
                    Cursor = Math.Min(start + column, end < 0 ? Text.Length : end);
                }
                else
                {
                    NavigateHistory(1);
                }
                break;
            case ConsoleKey.U when control:
                Text = string.Empty;
                Cursor = 0;
                break;
            case ConsoleKey.W when control:
                var wordStart = WordStart();
                Text = Text.Remove(wordStart, Cursor - wordStart);
                Cursor = wordStart;
                break;
            default:
                if (char.IsControl(key.KeyChar) || control || key.Modifiers.HasFlag(ConsoleModifiers.Alt))
                {
                    return false;
                }
                Insert(key.KeyChar.ToString());
                break;
        }

        return true;
    }

    private int LineStart() => Cursor == 0 ? 0 : Text.LastIndexOf('\n', Cursor - 1) + 1;

    private int LineEnd()
    {
        var end = Text.IndexOf('\n', Cursor);
        return end < 0 ? Text.Length : end;
    }

    private int PreviousPosition(int position) => position == 0 ? 0
        : position > 1 && char.IsLowSurrogate(Text[position - 1]) && char.IsHighSurrogate(Text[position - 2])
            ? position - 2 : position - 1;

    private int NextPosition(int position) => position >= Text.Length ? Text.Length
        : position + 1 < Text.Length && char.IsHighSurrogate(Text[position]) && char.IsLowSurrogate(Text[position + 1])
            ? position + 2 : position + 1;

    private int WordStart()
    {
        var position = Cursor;
        while (position > 0 && char.IsWhiteSpace(Text[position - 1]))
        {
            position = PreviousPosition(position);
        }
        while (position > 0 && !char.IsWhiteSpace(Text[position - 1]))
        {
            position = PreviousPosition(position);
        }
        return position;
    }

    private int WordEnd()
    {
        var position = Cursor;
        while (position < Text.Length && !char.IsWhiteSpace(Text[position]))
        {
            position = NextPosition(position);
        }
        while (position < Text.Length && char.IsWhiteSpace(Text[position]))
        {
            position = NextPosition(position);
        }
        return position;
    }

    private void NavigateHistory(int direction)
    {
        if (history.Count == 0 || direction > 0 && _historyIndex == history.Count)
        {
            return;
        }
        if (_historyIndex == history.Count)
        {
            _draft = Text;
        }
        _historyIndex = Math.Clamp(_historyIndex + direction, 0, history.Count);
        Text = _historyIndex == history.Count ? _draft : history[_historyIndex];
        Cursor = Text.Length;
    }
}
