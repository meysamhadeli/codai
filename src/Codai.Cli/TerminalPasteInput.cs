using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Codai.Cli;

internal sealed class TerminalPasteParser
{
    private const string Start = "\u001b[200~";
    private const string End = "\u001b[201~";
    private readonly List<ConsoleKeyInfo> _sequence = [];
    private readonly Queue<(ConsoleKeyInfo Key, string? Paste)> _events = new();
    private readonly StringBuilder _text = new();
    private bool _pasting;
    private int _endMatched;
    private long _started;

    public void Push(ConsoleKeyInfo key)
    {
        if (_pasting)
        {
            _text.Append(key.KeyChar);
            _endMatched = key.KeyChar == End[_endMatched] ? _endMatched + 1 : key.KeyChar == '\u001b' ? 1 : 0;
            if (_endMatched == End.Length)
            {
                _text.Length -= End.Length;
                _events.Enqueue((PasteKey, _text.ToString()));
                _text.Clear();
                _endMatched = 0;
                _pasting = false;
            }
            return;
        }

        if (_sequence.Count == 0 && key.KeyChar != '\u001b')
        {
            _events.Enqueue((key, null));
            return;
        }
        if (_sequence.Count == 0)
        {
            _started = Stopwatch.GetTimestamp();
        }
        _sequence.Add(key);
        if (key.KeyChar != Start[_sequence.Count - 1])
        {
            Flush();
        }
        else if (_sequence.Count == Start.Length)
        {
            _sequence.Clear();
            _pasting = true;
        }
    }

    public static ConsoleKeyInfo PasteKey => new('\u0016', ConsoleKey.V, false, false, true);

    public bool TryRead(out ConsoleKeyInfo key, out string? paste)
    {
        if (_events.TryDequeue(out var input))
        {
            (key, paste) = input;
            return true;
        }
        key = default;
        paste = null;
        return false;
    }

    public void FlushExpired()
    {
        if (_sequence.Count > 0 && Stopwatch.GetElapsedTime(_started) >= TimeSpan.FromMilliseconds(100))
        {
            Flush();
        }
    }

    public void Reset()
    {
        _sequence.Clear();
        _text.Clear();
        _events.Clear();
        _endMatched = 0;
        _pasting = false;
    }

    private void Flush()
    {
        foreach (var key in _sequence)
        {
            _events.Enqueue((key, null));
        }
        _sequence.Clear();
    }
}

internal static class TerminalClipboard
{
    public static string? ReadText()
    {
        if (!OperatingSystem.IsWindows() || !OpenClipboard(0))
        {
            return null;
        }
        try
        {
            var handle = GetClipboardData(13);
            var pointer = handle == 0 ? 0 : GlobalLock(handle);
            if (pointer == 0)
            {
                return null;
            }
            try
            {
                return Marshal.PtrToStringUni(pointer);
            }
            finally
            {
                GlobalUnlock(handle);
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(nint owner);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern nint GetClipboardData(uint format);

    [DllImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern nint GlobalLock(nint handle);

    [DllImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(nint handle);
}
