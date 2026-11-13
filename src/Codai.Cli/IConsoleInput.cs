using Codai.Cli.Ui;

namespace Codai.Cli;

/// <summary>
/// Small console-input abstraction so the interactive loop can be exercised without
/// reading from the real terminal.
/// </summary>
public interface IConsoleInput
{
    bool IsRedirected => false;

    /// <summary>Whether the key is the paste shortcut (Ctrl+V).</summary>
    static bool IsPasteKey(ConsoleKeyInfo key) =>
        key.Key == ConsoleKey.V && key.Modifiers.HasFlag(ConsoleModifiers.Control);

    /// <summary>
    /// Takes the pending bracketed-paste payload, falling back to the system
    /// clipboard. Sources without paste support return <c>false</c>.
    /// </summary>
    bool TryConsumePaste(out string? text)
    {
        text = null;
        return false;
    }

    Task<string> ReadToEndAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This input source does not support redirected task input.");

    /// <summary>
    /// Reads one line, or returns <c>null</c> at end of input (Ctrl+D, closed stdin).
    /// </summary>
    string? ReadLine();

    Task<string?> ReadLineAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ReadLine());
    }

    Task<string?> ReadResponseAsync(CancellationToken cancellationToken = default) => ReadLineAsync(cancellationToken);

    bool TryReadKey(out ConsoleKeyInfo key)
    {
        key = default;
        return false;
    }
}

/// <summary>Reads from the process's standard input.</summary>
public sealed class SystemConsoleInput : IConsoleInput
{
    private readonly Func<string?> _readClipboard;
    private readonly Func<ConsoleKeyInfo?> _readKey;
    private readonly TerminalPasteParser _pasteParser = new();
    private readonly Queue<ConsoleKeyInfo> _mouseKeys = new();
    private string? _pasteText;
    private readonly object _readLock = new();
    private readonly List<string> _history = [];
    private Task<string?>? _pendingRead;
    private int _interactiveReads;
    private string? _suspendedDraft;
    private WindowsConsoleMouseInput? _windowsMouse;
    private readonly TerminalMouseParser _mouseParser = new();
    private bool _mouseTracking;
    internal bool IsReadingComposer { get; private set; }
    public string? PendingInsertion { get; set; }

    /// <summary>Handles mouse scrolling without inserting terminal reports into the draft.</summary>
    public Func<TerminalMouseEvent, bool>? MouseHandler { get; set; }

    public Func<ConsoleKeyInfo, bool>? OverlayKeyHandler { get; set; }

    public Func<string?>? PaletteCommandHandler { get; set; }
    /// <summary>Optional handler for application-level terminal shortcuts.</summary>
    public Func<ConsoleKeyInfo, bool>? KeyHandler { get; set; }

    /// <summary>Optional callback used to redraw the prompt after a shortcut.</summary>
    public Action<string, int>? RedrawHandler { get; set; }

    public Action<string, int, IReadOnlyList<string>, int>? ComposerRedrawHandler { get; set; }

    /// <summary>Workspace used for @ file completion.</summary>
    public string? CompletionRoot { get; set; }

    /// <summary>Discovered skill names offered by slash-command completion, including unchecked skills.</summary>
    public IReadOnlyList<string> SkillNames { get; set; } = [];

    /// <summary>Placeholder displayed inside an empty interactive composer.</summary>
    public string? Placeholder { get; set; }

    public string InitialText { get; set; } = string.Empty;
    /// <summary>The shared instance.</summary>
    public static SystemConsoleInput Instance { get; } = new();

    public SystemConsoleInput() : this(TerminalClipboard.ReadText)
    {
    }

    internal SystemConsoleInput(Func<string?> readClipboard, Func<ConsoleKeyInfo?>? readKey = null)
    {
        _readClipboard = readClipboard;
        _readKey = readKey ?? ReadAvailableKey;
    }

    private static ConsoleKeyInfo? ReadAvailableKey() => Console.KeyAvailable ? Console.ReadKey(intercept: true) : null;

    private static (int Width, int Height) WindowSize()
    {
        try
        {
            return (Console.WindowWidth, Console.WindowHeight);
        }
        catch (IOException)
        {
            return (0, 0);
        }
    }

    internal bool TryPaste(ConsoleKeyInfo key, ComposerBuffer editor)
    {
        if (!IConsoleInput.IsPasteKey(key))
        {
            return false;
        }
        var text = ConsumePendingPaste();
        if (text is not null)
        {
            editor.InsertPaste(text);
        }
        return true;
    }

    /// <summary>Takes the pending bracketed-paste text, falling back to the system clipboard.</summary>
    internal string? ConsumePendingPaste()
    {
        var text = _pasteText ?? _readClipboard();
        _pasteText = null;
        return text;
    }

    /// <inheritdoc />
    public bool TryConsumePaste(out string? text)
    {
        text = ConsumePendingPaste();
        return text is not null;
    }

    public bool IsRedirected => Console.IsInputRedirected;

    internal void StartMouseTracking()
    {
        if (_mouseTracking || IsRedirected)
        {
            return;
        }
        if (OperatingSystem.IsWindows())
        {
            _windowsMouse = new WindowsConsoleMouseInput();
        }
        _mouseTracking = true;
    }

    internal void StopMouseTracking()
    {
        _mouseTracking = false;
        _mouseParser.Flush();
        _pasteParser.Reset();
        _pasteText = null;
        _mouseKeys.Clear();
        if (OperatingSystem.IsWindows())
        {
            _windowsMouse?.Restore();
            _windowsMouse = null;
        }
    }

    public async Task<string> ReadToEndAsync(CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(Console.OpenStandardInput(), Console.InputEncoding, leaveOpen: true);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public string? ReadLine() => Console.ReadLine();

    public async Task<string?> ReadLineAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsRedirected)
        {
            return await ReadInteractiveLineAsync(true, cancellationToken).ConfigureAwait(false);
        }

        Task<string?> pending;
        lock (_readLock)
        {
            pending = _pendingRead ??= Task.Run(Console.ReadLine, CancellationToken.None);
        }

        var line = await pending.WaitAsync(cancellationToken).ConfigureAwait(false);
        lock (_readLock)
        {
            if (ReferenceEquals(_pendingRead, pending))
            {
                _pendingRead = null;
            }
        }

        return line;
    }

    public Task<string?> ReadResponseAsync(CancellationToken cancellationToken = default) =>
        IsRedirected ? ReadLineAsync(cancellationToken) : ReadInteractiveLineAsync(false, cancellationToken);

    internal async Task<string?> ReadInteractiveLineAsync(bool composer, CancellationToken cancellationToken)
    {
        lock (_readLock)
        {
            _interactiveReads++;
            IsReadingComposer = composer;
        }
        try
        {
            return await ReadInteractiveCoreAsync(composer, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lock (_readLock)
            {
                _interactiveReads--;
                IsReadingComposer = false;
                _pasteParser.Reset();
                _pasteText = null;
            }
        }
    }

    public bool TryReadKey(out ConsoleKeyInfo key)
    {
        lock (_readLock)
        {
            key = default;
            if (_interactiveReads > 0 || IsRedirected)
            {
                return false;
            }
            return TryReadTerminalKey(out key, out _);
        }
    }

    private bool TryReadTerminalKey(out ConsoleKeyInfo key, out bool mouseHandled)
    {
        _pasteText = null;
        mouseHandled = false;
        var handled = false;
        void HandleMouse(TerminalMouseEvent mouse)
        {
            if (mouse.Action == TerminalMouseAction.Paste)
            {
                _mouseKeys.Enqueue(TerminalPasteParser.PasteKey);
                handled = true;
            }
            else
            {
                handled |= MouseHandler?.Invoke(mouse) == true;
            }
        }
        bool ReadBuffered(out ConsoleKeyInfo bufferedKey)
        {
            if (_mouseKeys.TryDequeue(out bufferedKey) || _mouseParser.TryReadKey(out bufferedKey))
            {
                return true;
            }
            while (_pasteParser.TryRead(out var next, out var paste))
            {
                if (paste is not null || !_mouseTracking)
                {
                    _pasteText = paste;
                    bufferedKey = next;
                    return true;
                }
                if (_mouseParser.Push(next) is { } mouse)
                {
                    HandleMouse(mouse);
                }
                if (_mouseKeys.TryDequeue(out bufferedKey) || _mouseParser.TryReadKey(out bufferedKey))
                {
                    return true;
                }
            }
            bufferedKey = default;
            return false;
        }

        try
        {
            if (OperatingSystem.IsWindows() && _windowsMouse is { } mouseInput)
            {
                while (mouseInput.TryRead(out var mouse))
                {
                    HandleMouse(mouse);
                }
            }
            if (ReadBuffered(out key))
            {
                return true;
            }
            while (_readKey() is { } next)
            {
                _pasteParser.Push(next);
                if (ReadBuffered(out key))
                {
                    return true;
                }
            }
            _pasteParser.FlushExpired();
            if (ReadBuffered(out key))
            {
                return true;
            }
            _mouseParser.FlushExpired();
            return ReadBuffered(out key);
        }
        finally
        {
            mouseHandled = handled;
        }
    }

    private async Task<string?> ReadInteractiveCoreAsync(bool composer, CancellationToken cancellationToken)
    {
        var editor = new ComposerBuffer(composer ? _history : []);
        if (composer)
        {
            editor.Insert(_suspendedDraft ?? InitialText);
            _suspendedDraft = null;
            InitialText = string.Empty;
            if (PendingInsertion is { } insertion)
            {
                editor.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.Home, false, false, true));
                editor.Insert(insertion + (editor.Text.Length > 0 && !insertion.EndsWith(' ') ? " " : string.Empty));
                PendingInsertion = null;
            }
        }
        var completion = new ComposerCompletion { SkillNames = SkillNames };
        IReadOnlyList<string>? files = null;
        var previousLength = 0;
        var previousCursor = 0;
        var previousSize = WindowSize();

        void Draw()
        {
            if (composer && ComposerRedrawHandler is not null)
            {
                ComposerRedrawHandler(editor.Text, editor.Cursor, completion.Options, completion.Selected);
            }
            else if (composer && RedrawHandler is not null)
            {
                RedrawHandler(editor.Text, editor.Cursor);
            }
            else
            {
                Console.Write(new string('\b', previousCursor));
                Console.Write(editor.Text);
                Console.Write(new string(' ', Math.Max(0, previousLength - editor.Text.Length)));
                Console.Write(new string('\b', Math.Max(previousLength, editor.Text.Length) - editor.Cursor));
                previousLength = editor.Text.Length;
                previousCursor = editor.Cursor;
            }
        }

        Draw();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (composer && PaletteCommandHandler?.Invoke() is { } command)
            {
                _suspendedDraft = editor.Text;
                return command;
            }
            var hasKey = TryReadTerminalKey(out var key, out var mouseHandled);
            if (mouseHandled && composer)
            {
                Draw();
            }
            if (!hasKey)
            {
                await Task.Delay(25, cancellationToken).ConfigureAwait(false);
                var size = WindowSize();
                if (composer && previousSize != size)
                {
                    previousSize = size;
                    Draw();
                }
                continue;
            }

            var control = key.Modifiers.HasFlag(ConsoleModifiers.Control);
            if (composer && OverlayKeyHandler?.Invoke(key) == true)
            {
                Draw();
                continue;
            }
            if (!composer && key.Key == ConsoleKey.Escape)
            {
                return string.Empty;
            }
            if (control && key.Key == ConsoleKey.D && editor.Text.Length == 0)
            {
                return null;
            }
            if (control && key.Key == ConsoleKey.C)
            {
                return string.Empty;
            }
            if (composer && completion.HandleKey(key, editor))
            {
                Draw();
                continue;
            }
            if (composer && KeyHandler?.Invoke(key) == true)
            {
                Draw();
                continue;
            }
            if (key.Key == ConsoleKey.Enter && (!composer || key.Modifiers == ConsoleModifiers.None))
            {
                if (composer && !string.IsNullOrWhiteSpace(editor.Text) && (_history.Count == 0 || _history[^1] != editor.Text))
                {
                    _history.Add(editor.Text);
                    if (_history.Count > 100)
                    {
                        _history.RemoveAt(0);
                    }
                }
                if (!composer)
                {
                    Console.WriteLine();
                }
                return editor.Text;
            }
            if (TryPaste(key, editor) || editor.HandleKey(key))
            {
                if (composer)
                {
                    if (editor.Text.Contains('@') && files is null)
                    {
                        files = ComposerCompletion.FindFiles(CompletionRoot, cancellationToken);
                    }
                    completion.Update(editor, files ?? []);
                }
                Draw();
            }
        }
    }
}
