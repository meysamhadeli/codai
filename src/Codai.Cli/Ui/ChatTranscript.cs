using System.Diagnostics;
using System.Text;
using Codai.Core.Tools;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Codai.Cli.Ui;

public sealed class ChatTranscript
{
    private readonly List<Entry> _entries = [];
    private int _version;
    private int _renderedVersion = -1;
    private int _renderedWidth;
    private string[] _lines = [];
    private string[] _paintedRows = [];
    private int _paintedWidth;
    private IAnsiConsole? _paintedConsole;
    private readonly Dictionary<int, Entry> _stepHeaders = [];
    private readonly Dictionary<int, Entry> _visibleSteps = [];
    private Entry? _activity;
    private Entry? _selectedStep;
    private long _renderedSecond;

    public int ScrollOffset { get; private set; }

    public void InvalidateDisplay() => _paintedRows = [];

    public void Add(string text, string style = "default")
    {
        CompleteActivity();
        AddEntry(new Entry { Text = TranscriptMarkdown.Clean(text), Style = style });
    }

    public void AddUser(string text)
    {
        CompleteActivity();
        AddEntry(new Entry { Text = TranscriptMarkdown.Clean(text), User = true });
    }

    public void StartActivity(string label)
    {
        if (_activity is { Status: "running" })
        {
            _activity.Activity = label;
            _version++;
            return;
        }
        _activity = new Entry { Activity = label, Status = "running", Started = Stopwatch.GetTimestamp() };
        AddEntry(_activity);
    }

    public void CompleteActivity(string status = "done")
    {
        if (_activity is not { Status: "running" })
        {
            return;
        }
        _activity.Finish(status);
        _version++;
    }

    public void AppendMarkdown(string text)
    {
        CompleteActivity();
        if (_entries.LastOrDefault() is not { Markdown: true } entry)
        {
            entry = new Entry { Markdown = true };
            AddEntry(entry);
        }
        entry.Text = Limit(entry.Text + text);
        _version++;
    }

    public void StartTool(string name, string summary)
    {
        CompleteActivity();
        AddEntry(new Entry
        {
            Tool = name,
            Text = ComposerLayout.Clip(TranscriptMarkdown.Clean(summary).Split('\n')[0], 160),
            Status = "running",
            Started = Stopwatch.GetTimestamp(),
        });
    }

    public void CompleteTool(string name, bool succeeded, string detail)
    {
        var entry = _entries.LastOrDefault(entry => entry.Tool == name);
        if (entry is null)
        {
            StartTool(name, name);
            entry = _entries[^1];
        }
        entry.Finish(succeeded ? "done" : "failed");
        if (!succeeded)
        {
            entry.Output = TranscriptMarkdown.Clean(detail);
        }
        _version++;
    }

    public void ToolOutput(string name, string text)
    {
        var entry = _entries.LastOrDefault(entry => entry.Tool == name);
        if (entry is null)
        {
            StartTool(name, name);
            entry = _entries[^1];
        }
        entry.Output = Limit(entry.Output + TranscriptMarkdown.Clean(text));
        _version++;
    }

    public void ToolResult(string name, string content)
    {
        var entry = _entries.LastOrDefault(entry => entry.Tool == name);
        if (entry is not null && entry.Output.Length == 0)
        {
            entry.Output = Limit(TranscriptMarkdown.Clean(content));
            _version++;
        }
    }

    public void ToolResult(string name, ToolResult result)
    {
        ToolResult(name, result.Content);
        var entry = _entries.LastOrDefault(entry => entry.Tool == name);
        if (entry is null || result.FileChanges.Count == 0)
        {
            return;
        }
        entry.AddedLines = result.FileChanges.Sum(change => (long)change.AddedLines);
        entry.RemovedLines = result.FileChanges.Sum(change => (long)change.RemovedLines);
        entry.Text = ComposerLayout.Clip(TranscriptMarkdown.Clean(string.Join(", ", result.FileChanges.Select(change => change.Path))), 160);
        var output = new StringBuilder();
        foreach (var change in result.FileChanges)
        {
            output.AppendLine($"{change.Path} +{change.AddedLines} -{change.RemovedLines}")
                .AppendLine(change.Preview);
            if (output.Length >= 60000)
            {
                break;
            }
        }
        entry.Output = Limit(TranscriptMarkdown.Clean(result.Content + "\n\n" + output));
        _version++;
    }

    public bool ToggleTool()
    {
        var entry = _selectedStep is { } selected && _entries.Contains(selected)
            ? selected : _entries.LastOrDefault(entry => entry.IsStep);
        if (entry is null)
        {
            return false;
        }
        entry.Expanded = !entry.Expanded;
        ScrollOffset = 0;
        _version++;
        return true;
    }

    public bool ToggleStepAtRow(int row, int rows)
    {
        if (!_visibleSteps.TryGetValue(row, out var entry) || !_entries.Contains(entry))
        {
            return false;
        }
        _selectedStep = entry;
        entry.Expanded = !entry.Expanded;
        _version++;
        GetLines(_renderedWidth);
        var header = _stepHeaders.First(pair => ReferenceEquals(pair.Value, entry)).Key;
        ScrollOffset = Math.Clamp(_lines.Length - rows - header + row - 1, 0, Math.Max(0, _lines.Length - rows));
        return true;
    }

    public void StopRunningTools()
    {
        CompleteActivity("stopped");
        foreach (var entry in _entries.Where(entry => entry.Tool is not null && entry.Status == "running"))
        {
            entry.Finish("stopped");
        }
        _version++;
    }

    public void Scroll(int lines) => ScrollOffset = Math.Clamp(ScrollOffset + lines, 0, Math.Max(0, _lines.Length - 1));

    public void ScrollToRow(int row, int rows)
    {
        var maximum = Math.Max(0, _lines.Length - rows);
        var first = (int)((long)Math.Clamp(row, 0, Math.Max(0, rows - 1)) * maximum / Math.Max(1, rows - 1));
        ScrollOffset = maximum - first;
    }

    public void Clear()
    {
        _entries.Clear();
        _activity = null;
        _selectedStep = null;
        _visibleSteps.Clear();
        ScrollOffset = 0;
        _version++;
    }

    public IReadOnlyList<string> GetLines(int width)
    {
        var second = Stopwatch.GetTimestamp() / Stopwatch.Frequency;
        if (_renderedVersion == _version && _renderedWidth == width &&
            (_renderedSecond == second || !_entries.Any(entry => entry.Activity is not null && entry.Expanded && entry.Status == "running")))
        {
            return _lines;
        }
        using var writer = new StringWriter();
        var capture = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.Yes });
        capture.Profile.Width = Math.Max(4, width);
        List<string> lines = [];
        _stepHeaders.Clear();
        foreach (var entry in _entries)
        {
            writer.GetStringBuilder().Clear();
            if (entry.IsStep)
            {
                _stepHeaders[lines.Count] = entry;
            }
            capture.Write(Render(entry, width));
            capture.WriteLine();
            var rendered = writer.ToString().Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n');
            lines.AddRange(rendered.Take(rendered.Length - 1));
        }
        var previousCount = _lines.Length;
        while (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }
        _lines = lines.Count == 0 ? [string.Empty] : lines.ToArray();
        if (ScrollOffset > 0)
        {
            ScrollOffset = Math.Min(_lines.Length - 1, ScrollOffset + Math.Max(0, _lines.Length - previousCount));
        }
        _renderedVersion = _version;
        _renderedWidth = width;
        _renderedSecond = second;
        return _lines;
    }

    public void Draw(IAnsiConsole console, int rows)
    {
        if (rows <= 0)
        {
            return;
        }
        var lines = GetLines(Math.Max(4, console.Profile.Width - 3));
        ScrollOffset = Math.Min(ScrollOffset, Math.Max(0, lines.Count - rows));
        var first = Math.Max(0, lines.Count - rows - ScrollOffset);
        var visible = Math.Min(rows, lines.Count - first);
        _visibleSteps.Clear();
        foreach (var (line, entry) in _stepHeaders)
        {
            if (line >= first && line < first + visible)
            {
                _visibleSteps[line - first + 1] = entry;
            }
        }
        if (_paintedWidth != console.Profile.Width || _paintedRows.Length != rows || !ReferenceEquals(_paintedConsole, console))
        {
            _paintedRows = new string[rows];
            _paintedWidth = console.Profile.Width;
            _paintedConsole = console;
        }
        var thumb = (int)((long)first * Math.Max(0, rows - 1) / Math.Max(1, lines.Count - rows));
        StringBuilder changes = new();
        for (var row = 0; row < rows; row++)
        {
            var content = row < visible ? " " + lines[first + row] + "\u001b[0m" : string.Empty;
            if (lines.Count > rows)
            {
                content += $"\u001b[{row + 1};{Math.Max(1, console.Profile.Width - 1)}H\u001b[90m" +
                    (row == thumb ? "\u2588" : "\u2502") + "\u001b[0m";
            }
            if (string.Equals(_paintedRows[row], content, StringComparison.Ordinal))
            {
                continue;
            }
            changes.Append($"\u001b[{row + 1};1H\u001b[2K").Append(content);
            _paintedRows[row] = content;
        }
        if (changes.Length > 0)
        {
            console.Write(new ControlCode(changes.ToString()));
        }
    }

    private void AddEntry(Entry entry)
    {
        _entries.Add(entry);
        while (_entries.Count > 200 || _entries.Count > 1 && _entries.Sum(item => item.Text.Length + item.Output.Length) > 256000)
        {
            _entries.RemoveAt(0);
        }
        _version++;
    }

    private static string Limit(string value) => value.Length <= 60000 ? value : value[..59960] + "\n[Preview limit reached]";

    private static IRenderable Render(Entry entry, int width)
    {
        if (entry.User)
        {
            return new Rows(ComposerLayout.Create(entry.Text, 0, Math.Max(1, width - 2), int.MaxValue).Lines.Select(line =>
                (IRenderable)new Text("> " + line + new string(' ', Math.Max(0, width - 2 - line.GetCellWidth())), Style.Parse("white on grey19"))));
        }
        if (entry.Markdown)
        {
            return TranscriptMarkdown.Render(entry.Text);
        }
        if (entry.IsStep)
        {
            var color = entry.Status == "failed" ? "red" : entry.Status == "stopped" ? "yellow" : "deepskyblue1";
            var title = entry.Activity is { } activity
                ? (entry.Status == "done" ? "Finished " + activity.ToLowerInvariant() : activity)
                : entry.AddedLines.HasValue ? "Edited" : entry.Tool switch
                {
                    "read_file" => "Read",
                    "list_files" or "glob" => "Listed",
                    "grep" => "Searched",
                    "run_command" => "Ran",
                    _ => entry.Tool ?? string.Empty,
                };
            var counts = entry.AddedLines is { } added ? $" +{added} -{entry.RemovedLines}" : string.Empty;
            var suffix = counts + $" ({entry.StatusLabel})";
            var label = ComposerLayout.Clip((entry.Expanded ? "v " : "> ") + title + " " + entry.Text,
                Math.Max(1, width - suffix.GetCellWidth()));
            var header = $"[{color} bold]{Markup.Escape(label)}[/]";
            if (entry.AddedLines is { } additions)
            {
                header += $" [green]+{additions}[/] [red]-{entry.RemovedLines}[/]";
            }
            header += $" [grey]({Markup.Escape(entry.StatusLabel)})[/]";
            List<IRenderable> content =
            [
                new Markup(header),
            ];
            if (entry.Expanded)
            {
                var output = entry.Activity is { } phase
                    ? $"{phase}: {entry.Status}. Elapsed: {ActivityStatus.FormatElapsed(entry.Duration ?? Stopwatch.GetElapsedTime(entry.Started))}." +
                        "\nStatus and timing only; private model reasoning is not displayed."
                    : entry.Output.Length > 0 ? entry.Output : "No output yet.";
                content.Add(TranscriptMarkdown.Code(output,
                    numbered: false, width: width));
            }
            return new Rows(content);
        }
        return new Text(entry.Text, Style.Parse(entry.Style));
    }

    private sealed class Entry
    {
        public string Text { get; set; } = string.Empty;
        public string Output { get; set; } = string.Empty;
        public string Style { get; init; } = "default";
        public string? Tool { get; init; }
        public string? Activity { get; set; }
        public bool IsStep => Tool is not null || Activity is not null;
        public long? AddedLines { get; set; }
        public long? RemovedLines { get; set; }
        public string Status { get; set; } = string.Empty;
        public bool User { get; init; }
        public bool Markdown { get; init; }
        public bool Expanded { get; set; }
        public long Started { get; init; }
        public TimeSpan? Duration { get; set; }

        /// <summary>Gets the status with the time the call took, once it is worth reading.</summary>
        public string StatusLabel => Duration is { TotalSeconds: >= 1 } elapsed
            ? $"{Status} · {ActivityStatus.FormatElapsed(elapsed)}"
            : Status;

        public void Finish(string status)
        {
            Status = status;
            Duration = Started == 0 ? null : Stopwatch.GetElapsedTime(Started);
        }
    }
}
