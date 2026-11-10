using System.Globalization;
using System.Text.RegularExpressions;
using Spectre.Console;

namespace Codai.Cli.Ui;

internal sealed partial class DiffViewer : IInlineDecision
{
    private sealed record DiffRow(string Text, int? OldLine = null, int? NewLine = null, char Kind = ' ');
    private sealed record DiffFile(string Name, List<DiffRow> Rows)
    {
        public int Added => Rows.Count(row => row.Kind == '+');
        public int Removed => Rows.Count(row => row.Kind == '-');
    }
    private sealed record DisplayRow(string Text, string Number, char Kind);
    private sealed class Source(IReadOnlyList<DiffFile> files)
    {
        public IReadOnlyList<DiffFile> Files { get; } = files;
        public int Selected { get; set; }
        public int Offset { get; set; }
        public int Horizontal { get; set; }
        public HashSet<int> Reviewed { get; } = [];
    }

    private readonly Source[] _sources;
    private int _source;
    private int _pageHeight = 20;
    private bool _detail;
    private bool _help;
    private int _bodyWidth = 80;
    private int _listHeight = 6;
    private readonly List<DisplayRow> _rows = [];
    private readonly List<int> _hunks = [];
    private Source Current => _sources[_source];

    public DiffViewer(string workingTree, string staged) => _sources = [new(Parse(workingTree)), new(Parse(staged))];

    public bool Closed { get; private set; }
    public string? Command => null;

    public int MeasureHeight(int width, int maximumHeight) => _detail || _help
        ? maximumHeight
        : Math.Min(maximumHeight, Math.Max(1, Math.Min(6, Current.Files.Count)) + 9 + Footer(width).Length);

    public void HandleKey(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Escape && _help)
        {
            _help = false;
            return;
        }
        switch (key.Key)
        {
            case ConsoleKey.Escape when _detail:
                _detail = false;
                return;
            case ConsoleKey.Escape or ConsoleKey.Q:
                Closed = true;
                return;
            case ConsoleKey.Tab:
                _detail = !_detail && Current.Files.Count > 0;
                return;
            case ConsoleKey.Enter:
                _detail = Current.Files.Count > 0;
                return;
            case ConsoleKey.D:
                _source = 1 - _source;
                return;
            case ConsoleKey.N:
                MoveFile(1);
                return;
            case ConsoleKey.P:
                MoveFile(-1);
                return;
            case ConsoleKey.M:
                if (Current.Files.Count > 0 && !Current.Reviewed.Add(Current.Selected))
                {
                    Current.Reviewed.Remove(Current.Selected);
                }
                return;
        }
        if (key.KeyChar == '?')
        {
            _help = !_help;
            return;
        }
        if (Current.Files.Count == 0)
        {
            return;
        }
        if (!_detail && key.Key is ConsoleKey.UpArrow or ConsoleKey.DownArrow or ConsoleKey.Home or ConsoleKey.End or ConsoleKey.PageUp or ConsoleKey.PageDown)
        {
            Current.Selected = Math.Clamp(key.Key switch
            {
                ConsoleKey.UpArrow => Current.Selected - 1,
                ConsoleKey.DownArrow => Current.Selected + 1,
                ConsoleKey.Home => 0,
                ConsoleKey.End => Current.Files.Count - 1,
                ConsoleKey.PageUp => Current.Selected - _listHeight,
                _ => Current.Selected + _listHeight,
            }, 0, Current.Files.Count - 1);
            Current.Offset = 0;
            Current.Horizontal = 0;
            return;
        }
        BuildRows();
        if (key.KeyChar is '[' or ']')
        {
            _detail = true;
            Current.Offset = key.KeyChar == ']'
                ? _hunks.FirstOrDefault(index => index > Current.Offset, Current.Offset)
                : _hunks.LastOrDefault(index => index < Current.Offset, Current.Offset);
        }
        else if (_detail)
        {
            Current.Offset = key.Key switch
            {
                ConsoleKey.DownArrow => Current.Offset + 1,
                ConsoleKey.UpArrow => Current.Offset - 1,
                ConsoleKey.PageDown => Current.Offset + _pageHeight,
                ConsoleKey.PageUp => Current.Offset - _pageHeight,
                ConsoleKey.Home => 0,
                ConsoleKey.End => Math.Max(0, _rows.Count - _pageHeight),
                _ => Current.Offset,
            };
            Current.Offset = Math.Clamp(Current.Offset, 0, Math.Max(0, _rows.Count - 1));
            Current.Horizontal = key.Key switch
            {
                ConsoleKey.RightArrow => Current.Horizontal + 4,
                ConsoleKey.LeftArrow => Math.Max(0, Current.Horizontal - 4),
                _ => Current.Horizontal,
            };
            Current.Horizontal = Math.Min(Current.Horizontal, Current.Files[Current.Selected].Rows.Select(row => row.Text.Length).DefaultIfEmpty(0).Max());
        }
    }

    public void Draw(IAnsiConsole console, string theme = "graphite", int minimumTop = 1)
    {
        var top = Math.Max(1, minimumTop);
        var width = Math.Max(1, console.Profile.Width - 2);
        var inner = Math.Max(1, width - 4);
        var height = Math.Max(1, MeasureHeight(console.Profile.Width, Math.Max(1, console.Profile.Height - top + 1)));
        var footer = Footer(console.Profile.Width);
        var footerTop = Math.Max(1, height - 1 - footer.Length);
        _pageHeight = Math.Max(1, footerTop - 7);
        _listHeight = Math.Max(1, Math.Min(6, footerTop - 8));
        _bodyWidth = inner;
        var files = Current.Files;
        BuildRows();
        Current.Offset = Math.Clamp(Current.Offset, 0, Math.Max(0, _rows.Count - 1));
        var firstFile = Math.Max(0, Current.Selected - _listHeight + 1);
        for (var row = 0; row < height; row++)
        {
            console.Write(new ControlCode($"\u001b[{top + row};1H\u001b[2K\u001b[{top + row};2H"));
            if (row == 0 || row == height - 1)
            {
                var left = console.Profile.Capabilities.Unicode ? row == 0 ? "\u256d" : "\u2570" : "+";
                var right = console.Profile.Capabilities.Unicode ? row == 0 ? "\u256e" : "\u256f" : "+";
                var rail = console.Profile.Capabilities.Unicode ? '\u2500' : '-';
                WriteCell(console, left + new string(rail, Math.Max(0, width - 2)) + right, width, "grey70");
                continue;
            }
            WriteCell(console, console.Profile.Capabilities.Unicode ? "\u2502 " : "| ", 2, "grey70");
            if (row == 1)
            {
                WriteCell(console, _source == 0 ? "Working-tree changes (git diff)" : "Staged changes (git diff --cached)", inner, "default");
            }
            else if (row == 3)
            {
                WriteCounts(console, files.Count.ToString(CultureInfo.InvariantCulture) + (files.Count == 1 ? " file changed" : " files changed"),
                    files.Sum(file => file.Added), files.Sum(file => file.Removed), inner, rightAligned: false);
            }
            else if (row >= footerTop)
            {
                WriteCell(console, footer.ElementAtOrDefault(row - footerTop) ?? string.Empty, inner, "grey62");
            }
            else if (_help && row >= 5)
            {
                string[] help = ["Diff keys", "Enter       View selected file", "Esc         Back to files / close", "Up/Down     Select file or scroll", "PgUp/PgDn   Scroll one page", "Home/End    First / last page", "Left/Right  Horizontal scroll", "n / p       Next / previous file", "] / [       Next / previous hunk", "d           Working tree / staged", "m           Toggle reviewed marker", "Tab         File list / detail", "q           Close viewer", "?           Close help"];
                WriteCell(console, help.ElementAtOrDefault(row - 5) ?? string.Empty, inner, "default");
            }
            else if (files.Count == 0)
            {
                WriteCell(console, row == 5 ? "No diff!" : string.Empty, inner, "grey62");
            }
            else if (_detail && row == 5)
            {
                var suffix = inner >= 32 ? " [read-only]" : string.Empty;
                var name = files[Current.Selected].Name + (Current.Reviewed.Contains(Current.Selected) ? "  [reviewed]" : string.Empty);
                WriteCell(console, ComposerLayout.Clip(name, Math.Max(1, inner - suffix.Length)) + suffix, inner, "default");
            }
            else if (_detail && row == 6)
            {
                WriteCell(console, new string(console.Profile.Capabilities.Unicode ? '\u2500' : '-', inner), inner, "grey62");
            }
            else if (_detail && row >= 7)
            {
                var diff = _rows.ElementAtOrDefault(Current.Offset + row - 7);
                var style = diff?.Kind switch { '+' => "#9be9a8 on #102819", '-' => "#ff9d9d on #30191b", _ => "default" };
                WriteCell(console, diff is null ? string.Empty : diff.Number + diff.Text, inner, style);
            }
            else if (!_detail && row == 5)
            {
                var arrow = console.Profile.Capabilities.Unicode ? "\u2191" : "^";
                WriteCell(console, firstFile > 0 ? $"{arrow} {firstFile} more files" : string.Empty, inner, "grey62");
            }
            else if (!_detail && row >= 6 && row < 6 + _listHeight && firstFile + row - 6 < files.Count)
            {
                var index = firstFile + row - 6;
                var file = files[index];
                var selected = index == Current.Selected;
                var marker = selected ? console.Profile.Capabilities.Unicode ? "\u276f " : "> " : "  ";
                WriteCounts(console, marker + (Current.Reviewed.Contains(index) ? "[x] " : string.Empty) + file.Name,
                    file.Added, file.Removed, inner, selected);
            }
            else if (!_detail && row == 6 + _listHeight)
            {
                var remaining = Math.Max(0, files.Count - firstFile - _listHeight);
                var arrow = console.Profile.Capabilities.Unicode ? "\u2193" : "v";
                WriteCell(console, remaining > 0 ? $"{arrow} {remaining} more files" : string.Empty, inner, "grey62");
            }
            else
            {
                WriteCell(console, string.Empty, inner, "default");
            }
            WriteCell(console, console.Profile.Capabilities.Unicode ? " \u2502" : " |", 2, "grey70");
        }
    }

    private string[] Footer(int width) => SettingsPalette.WrapText(_detail
        ? "Up/Down scroll - Esc back - n/p files - ? keys"
        : "Up/Down to select - Enter to view - Esc to close", Math.Max(1, width - 6), 4);

    private void BuildRows()
    {
        _rows.Clear();
        _hunks.Clear();
        if (Current.Files.Count == 0)
        {
            return;
        }
        var file = Current.Files[Current.Selected];
        var digits = Math.Max(1, file.Rows.Select(row => Math.Max(row.OldLine ?? 0, row.NewLine ?? 0)).DefaultIfEmpty(0).Max()
            .ToString(CultureInfo.InvariantCulture).Length);
        var numbered = file.Rows.Any(row => row.OldLine.HasValue || row.NewLine.HasValue);
        foreach (var row in file.Rows)
        {
            if (row.Kind == '@')
            {
                _hunks.Add(_rows.Count);
                continue;
            }
            if (numbered && row.OldLine is null && row.NewLine is null && row.Text != "[Diff truncated]" && !row.Text.StartsWith("\\ No newline", StringComparison.Ordinal))
            {
                continue;
            }
            var number = row.Kind == '-' ? row.OldLine : row.NewLine ?? row.OldLine;
            var text = number.HasValue && row.Text.Length > 0 ? row.Text[1..] : row.Text;
            var elements = new StringInfo(text);
            text = elements.SubstringByTextElements(Math.Min(Current.Horizontal, elements.LengthInTextElements));
            var prefixWidth = numbered ? digits + 2 : 0;
            var lines = ComposerLayout.Create(text, 0, Math.Max(1, _bodyWidth - prefixWidth), int.MaxValue).Lines;
            for (var index = 0; index < lines.Count; index++)
            {
                var prefix = numbered ? (index == 0 ? number?.ToString(CultureInfo.InvariantCulture) ?? string.Empty : string.Empty).PadLeft(digits)
                    + " " + (index == 0 && row.Kind is '+' or '-' ? row.Kind : ' ') : string.Empty;
                _rows.Add(new DisplayRow(lines[index], prefix, row.Kind));
            }
        }
    }

    private static void WriteCounts(IAnsiConsole console, string label, int added, int removed, int width, bool selected = false, bool rightAligned = true)
    {
        var plus = "+" + added.ToString(CultureInfo.InvariantCulture);
        var minus = "-" + removed.ToString(CultureInfo.InvariantCulture);
        if (width < plus.Length + minus.Length + 3)
        {
            WriteCell(console, label, width, "default");
            return;
        }
        var labelWidth = Math.Max(1, width - plus.Length - minus.Length - 2);
        if (!rightAligned)
        {
            labelWidth = Math.Min(labelWidth, label.GetCellWidth());
        }
        WriteCell(console, label, labelWidth, selected ? "white on grey19" : "default");
        WriteCell(console, " " + plus, plus.Length + 1, "#4eba6f");
        WriteCell(console, " " + minus, minus.Length + 1, "#ff6b6b");
        if (!rightAligned)
        {
            WriteCell(console, string.Empty, Math.Max(0, width - labelWidth - plus.Length - minus.Length - 2), "default");
        }
    }

    private void MoveFile(int delta)
    {
        if (Current.Files.Count == 0)
        {
            return;
        }
        Current.Selected = (Current.Selected + delta + Current.Files.Count) % Current.Files.Count;
        Current.Offset = 0;
        Current.Horizontal = 0;
    }

    private static void WriteCell(IAnsiConsole console, string text, int width, string style)
    {
        var clipped = ComposerLayout.Clip(TranscriptMarkdown.Clean(text), width);
        console.Write(new Text(clipped + new string(' ', Math.Max(0, width - clipped.GetCellWidth())), Style.Parse(style)));
    }

    private static List<DiffFile> Parse(string patch)
    {
        List<DiffFile> files = [];
        DiffFile? file = null;
        var oldLine = 0;
        var newLine = 0;
        var inHunk = false;
        foreach (var raw in patch.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n'))
        {
            var line = TranscriptMarkdown.Clean(raw);
            if (line.StartsWith("diff --git ", StringComparison.Ordinal))
            {
                file = new DiffFile(line[11..], []);
                files.Add(file);
                inHunk = false;
                continue;
            }
            if (file is null)
            {
                if (line.Length > 0)
                {
                    file = new DiffFile("Changes", []);
                    files.Add(file);
                }
                else
                {
                    continue;
                }
            }
            if (!inHunk && (line.StartsWith("+++ ", StringComparison.Ordinal) || line.StartsWith("--- ", StringComparison.Ordinal) || line.StartsWith("rename to ", StringComparison.Ordinal)))
            {
                var path = line.StartsWith("rename to ", StringComparison.Ordinal) ? line[10..] : line[4..];
                if (path != "/dev/null")
                {
                    if (path.StartsWith("a/", StringComparison.Ordinal) || path.StartsWith("b/", StringComparison.Ordinal))
                    {
                        path = path[2..];
                    }
                    file = file with { Name = path };
                    files[^1] = file;
                }
            }
            var hunk = HunkHeader().Match(line);
            if (hunk.Success && int.TryParse(hunk.Groups[1].Value, CultureInfo.InvariantCulture, out oldLine)
                && int.TryParse(hunk.Groups[2].Value, CultureInfo.InvariantCulture, out newLine))
            {
                inHunk = true;
                file.Rows.Add(new DiffRow(line, Kind: '@'));
            }
            else if (inHunk && line.Length > 0 && line[0] is '+' or '-' or ' ')
            {
                var kind = line[0];
                file.Rows.Add(new DiffRow(line, kind == '+' ? null : oldLine++, kind == '-' ? null : newLine++, kind));
            }
            else if (line.Length > 0)
            {
                file.Rows.Add(new DiffRow(line));
            }
        }
        return files;
    }

    [GeneratedRegex(@"^@@ -(\d+)(?:,\d+)? \+(\d+)(?:,\d+)? @@", RegexOptions.CultureInvariant)]
    private static partial Regex HunkHeader();
}