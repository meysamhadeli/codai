using Codai.Application;

namespace Codai.Cli.Ui;

public sealed class ComposerCompletion
{
    private int _start;
    private int _length;
    private string[] _matches = [];
    private int _selectedIndex;

    public IReadOnlyList<string> Options { get; private set; } = [];

    public int Selected { get; private set; }

    public void Update(ComposerBuffer editor, IReadOnlyList<string> files)
    {
        _start = editor.Cursor;
        while (_start > 0 && !char.IsWhiteSpace(editor.Text[_start - 1]))
        {
            _start--;
        }
        _length = editor.Cursor - _start;
        var token = editor.Text.Substring(_start, _length);
        var end = editor.Cursor;
        while (end < editor.Text.Length && !char.IsWhiteSpace(editor.Text[end]))
        {
            end++;
        }
        _length = end - _start;
        IEnumerable<string> matches = [];
        if (token.StartsWith('@'))
        {
            matches = files.Where(path => !path.Any(char.IsWhiteSpace) && path.Contains(token[1..], StringComparison.OrdinalIgnoreCase))
                .Select(path => "@" + path);
        }
        else if (_start == 0 && token.Length > 0 && token[0] is ':' or '/')
        {
            matches = SlashCommandParser.HelpText.Split('\n')
            .Select(line => token[0] + line.Split(' ', 2)[0][1..])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(command => command.StartsWith(token, StringComparison.OrdinalIgnoreCase));
        }
        _matches = matches.ToArray();
        _selectedIndex = 0;
        UpdateWindow();
    }

    public bool HandleKey(ConsoleKeyInfo key, ComposerBuffer editor)
    {
        if (Options.Count == 0 || key.Modifiers != ConsoleModifiers.None)
        {
            return false;
        }
        switch (key.Key)
        {
            case ConsoleKey.UpArrow:
                _selectedIndex = (_selectedIndex + _matches.Length - 1) % _matches.Length;
                UpdateWindow();
                return true;
            case ConsoleKey.DownArrow:
                _selectedIndex = (_selectedIndex + 1) % _matches.Length;
                UpdateWindow();
                return true;
            case ConsoleKey.Enter when _start == 0 && editor.Text[0] is ':' or '/':
                editor.Replace(_start, _length, Options[Selected]);
                Options = [];
                return false;
            case ConsoleKey.Tab:
                var needsSpace = _start + _length == editor.Text.Length || !char.IsWhiteSpace(editor.Text[_start + _length]);
                editor.Replace(_start, _length, Options[Selected] + (needsSpace ? " " : string.Empty));
                Options = [];
                return true;
            case ConsoleKey.Escape:
                Options = [];
                return true;
            default:
                return false;
        }
    }

    private void UpdateWindow()
    {
        var first = Math.Max(0, _selectedIndex - 5);
        Options = _matches.Skip(first).Take(6).ToArray();
        Selected = _selectedIndex - first;
    }

    public static IReadOnlyList<string> FindFiles(string? root, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return [];
        }
        List<string> files = [];
        Stack<string> pending = new();
        pending.Push(root);
        var visited = 0;
        var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System };
        while (pending.TryPop(out var directory) && visited < 10000 && files.Count < 3000)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos("*", options))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (++visited > 10000 || files.Count >= 3000)
                    {
                        break;
                    }
                    if (entry is DirectoryInfo)
                    {
                        if (!entry.Name.StartsWith('.') && entry.Name is not ("bin" or "obj" or "node_modules"))
                        {
                            files.Add(Path.GetRelativePath(root, entry.FullName).Replace('\\', '/') + "/");
                            pending.Push(entry.FullName);
                        }
                    }
                    else
                    {
                        files.Add(Path.GetRelativePath(root, entry.FullName).Replace('\\', '/'));
                    }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        files.Sort(StringComparer.OrdinalIgnoreCase);
        return files;
    }
}
