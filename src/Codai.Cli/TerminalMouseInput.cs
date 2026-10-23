using System.Diagnostics;
using System.Globalization;

namespace Codai.Cli;

/// <summary>A terminal mouse action, using one-based screen coordinates.</summary>
public readonly record struct TerminalMouseEvent(int Column, int Row, TerminalMouseAction Action);

/// <summary>Mouse actions used to scroll the conversation.</summary>
public enum TerminalMouseAction
{
    ScrollUp,
    ScrollDown,
    Press,
    Drag,
    Release,
}

internal sealed class TerminalMouseParser
{
    private readonly List<ConsoleKeyInfo> _sequence = [];
    private readonly Queue<ConsoleKeyInfo> _keys = new();
    private long _started;

    public TerminalMouseEvent? Push(ConsoleKeyInfo key)
    {
        if (_sequence.Count == 0)
        {
            if (key.KeyChar == '\u001b')
            {
                _started = Stopwatch.GetTimestamp();
                _sequence.Add(key);
            }
            else
            {
                _keys.Enqueue(key);
            }
            return null;
        }

        _sequence.Add(key);
        var count = _sequence.Count;
        if (count == 2 && key.KeyChar == '[' || count == 3 && key.KeyChar == '<')
        {
            return null;
        }
        if (count < 4)
        {
            Flush();
            return null;
        }
        if (key.KeyChar is 'M' or 'm')
        {
            var report = new string(_sequence.Skip(3).SkipLast(1).Select(part => part.KeyChar).ToArray());
            var parts = report.Split(';');
            if (parts.Length == 3 &&
                int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var button) &&
                int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var column) &&
                int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var row) &&
                column > 0 && row > 0)
            {
                _sequence.Clear();
                var action = (button & 0x63) switch
                {
                    64 => TerminalMouseAction.ScrollUp,
                    65 => TerminalMouseAction.ScrollDown,
                    0 => key.KeyChar == 'm' ? TerminalMouseAction.Release : TerminalMouseAction.Press,
                    32 => key.KeyChar == 'm' ? TerminalMouseAction.Release : TerminalMouseAction.Drag,
                    _ => (TerminalMouseAction?)null,
                };
                return action is { } value ? new TerminalMouseEvent(column, row, value) : null;
            }
            Flush();
        }
        else if (count >= 48 || key.KeyChar is not (>= '0' and <= '9' or ';'))
        {
            Flush();
        }
        return null;
    }

    public bool TryReadKey(out ConsoleKeyInfo key) => _keys.TryDequeue(out key);

    public void FlushExpired()
    {
        if (_sequence.Count > 0 && Stopwatch.GetElapsedTime(_started) >= TimeSpan.FromMilliseconds(100))
        {
            Flush();
        }
    }

    public void Flush()
    {
        foreach (var key in _sequence)
        {
            _keys.Enqueue(key);
        }
        _sequence.Clear();
    }
}
