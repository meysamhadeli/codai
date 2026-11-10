using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Codai.Core.Tools;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Codai.Cli.Ui;

public sealed partial class ChatTranscript
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
    private readonly Dictionary<int, Entry> _stepControls = [];
    private readonly Dictionary<int, Entry> _visibleSteps = [];
    private Entry? _activity;
    private Entry? _selectedStep;
    private Entry? _pinnedStep;
    private long _renderedFrame;
    private bool _renderedUnicode;
    private long _outputTokens;
    private ActivitySnapshot? _modelMetrics;
    private readonly SpectreUserInterface.DotMatrixSpinner _unicodeDots = new(true);
    private readonly SpectreUserInterface.DotMatrixSpinner _asciiDots = new(false);

    internal bool Unicode { get; set; } = true;
    internal bool ShowActivityTokens { get; set; }
    internal ActivitySnapshot? ModelMetrics
    {
        get => _modelMetrics;
        set
        {
            if (_modelMetrics != value)
            {
                _modelMetrics = value;
                if (_entries.Any(entry => entry.Activity is not null && entry.Status == "running"))
                {
                    _version++;
                }
            }
        }
    }

    internal long OutputTokens
    {
        get => _outputTokens;
        set
        {
            var tokens = Math.Max(0, value);
            if (_outputTokens != tokens)
            {
                _outputTokens = tokens;
                if (_entries.Any(entry => entry.Activity is not null && entry.Status == "running"))
                {
                    _version++;
                }
            }
        }
    }

    public int ScrollOffset { get; private set; }

    /// <summary>Theme accent colour used for links, user-message edges, tool dots, and activity labels.</summary>
    public string Accent { get; set; } = "deepskyblue1";

    public void InvalidateDisplay() => _paintedRows = [];

    /// <summary>Forces a full re-render so a new accent colour is applied to already-painted lines.</summary>
    public void Invalidate()
    {
        _version++;
        _paintedRows = [];
    }

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
        if (_activity is { Activity: "Responding", Status: "running" })
        {
            CompleteActivity();
        }
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
        if (_activity.Activity == "Responding" && status == "done")
        {
            _entries.Remove(_activity);
            _activity = null;
        }
        else
        {
            _activity.Expanded = false;
        }
        _version++;
    }

    public void AppendMarkdown(string text) => AppendMarkdown(text, completeActivity: true);

    internal void AppendMarkdown(string text, bool completeActivity)
    {
        var responding = _activity is { Activity: "Responding", Status: "running" } ? _activity : null;
        if (responding is not null)
        {
            _entries.Remove(responding);
        }
        else
        {
            CompleteActivity();
        }
        if (_entries.LastOrDefault() is not { Markdown: true } entry)
        {
            entry = new Entry { Markdown = true };
            AddEntry(entry);
        }
        entry.Text = Limit(entry.Text + text);
        if (!completeActivity)
        {
            _activity = responding ?? new Entry { Activity = "Responding", Status = "running", Started = Stopwatch.GetTimestamp() };
            AddEntry(_activity);
        }
        else if (responding is not null)
        {
            responding.Finish("done");
            _activity = null;
        }
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
        entry.FileChanges = result.FileChanges;
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
        var latest = _entries.LastOrDefault(entry => entry.Tool is not null);
        var entry = _selectedStep is { } selected && _entries.Contains(selected)
            ? selected : latest;
        if (entry is null)
        {
            return false;
        }
        _selectedStep = entry;
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
        var anchorRow = _visibleSteps.First(pair => ReferenceEquals(pair.Value, entry)).Key;
        _selectedStep = entry;
        entry.Expanded = !entry.Expanded;
        _version++;
        GetLines(_renderedWidth);
        if (row == 1 && ReferenceEquals(entry, _pinnedStep))
        {
            ScrollOffset = 0;
            return true;
        }
        var header = _stepHeaders.First(pair => ReferenceEquals(pair.Value, entry)).Key;
        ScrollOffset = Math.Clamp(_lines.Length - rows - header + anchorRow - 1, 0, Math.Max(0, _lines.Length - rows));
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
        _outputTokens = 0;
        _modelMetrics = null;
        ShowActivityTokens = false;
        _activity = null;
        _selectedStep = null;
        _pinnedStep = null;
        _visibleSteps.Clear();
        ScrollOffset = 0;
        _version++;
    }

    public IReadOnlyList<string> GetLines(int width) => GetLines(width, (long)(Stopwatch.GetElapsedTime(0).TotalMilliseconds / 160));

    internal IReadOnlyList<string> GetLines(int width, long animationFrame)
    {
        if (_renderedVersion == _version && _renderedWidth == width && _renderedUnicode == Unicode &&
            (_renderedFrame == animationFrame || !_entries.Any(entry => entry.IsStep && entry.Status == "running")))
        {
            return _lines;
        }
        using var writer = new StringWriter();
        var capture = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.Yes });
        capture.Profile.Width = Math.Max(4, width);
        List<string> lines = [];
        _stepHeaders.Clear();
        _stepControls.Clear();

        string[] RenderLines(IRenderable renderable, int renderWidth)
        {
            capture.Profile.Width = renderWidth;
            writer.GetStringBuilder().Clear();
            capture.Write(renderable);
            capture.WriteLine();
            var rendered = writer.ToString().Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n');
            return rendered.Reverse().SkipWhile(string.IsNullOrWhiteSpace).Reverse().ToArray();
        }

        foreach (var entry in _entries)
        {
            if (lines.Count > 0)
            {
                lines.Add(string.Empty);
            }
            if (entry.Tool is not null)
            {
                _stepHeaders[lines.Count] = entry;
            }
            entry.DisclosureRows.Clear();
            var rendered = RenderLines(Render(entry, Math.Max(4, width), animationFrame), Math.Max(4, width));
            foreach (var controlRow in entry.DisclosureRows)
            {
                _stepControls[lines.Count + controlRow] = entry;
            }
            lines.AddRange(rendered);
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
        _renderedFrame = animationFrame;
        _renderedUnicode = Unicode;
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
        var pinned = ScrollOffset == 0
            ? _stepHeaders.LastOrDefault(pair => pair.Key < first && pair.Value.Status == "running") : default;
        _pinnedStep = pinned.Value;
        var offset = _pinnedStep is null ? 0 : 1;
        first = Math.Max(0, lines.Count - (rows - offset) - ScrollOffset);
        var visible = Math.Min(rows - offset, lines.Count - first);
        _visibleSteps.Clear();
        if (_pinnedStep is not null)
        {
            _visibleSteps[1] = _pinnedStep;
        }
        foreach (var (line, entry) in _stepHeaders.Concat(_stepControls))
        {
            if (line >= first && line < first + visible)
            {
                _visibleSteps[line - first + 1 + offset] = entry;
            }
        }
        if (_paintedWidth != console.Profile.Width || _paintedRows.Length != rows || !ReferenceEquals(_paintedConsole, console))
        {
            _paintedRows = new string[rows];
            _paintedWidth = console.Profile.Width;
            _paintedConsole = console;
        }
        var thumbHeight = Math.Clamp((int)((long)rows * rows / Math.Max(1, lines.Count)), 1, rows);
        var thumb = (int)((long)first * (rows - thumbHeight) / Math.Max(1, lines.Count - (rows - offset)));
        var edge = Math.Max(1, console.Profile.Width);
        var rail = Unicode ? '\u2595' : '|';
        StringBuilder changes = new();
        for (var row = 0; row < rows; row++)
        {
            var content = offset > 0 && row == 0 ? " " + lines[pinned.Key] + "\u001b[0m"
                : row - offset < visible ? " " + lines[first + row - offset] + "\u001b[0m" : string.Empty;
            if (lines.Count > rows)
            {
                var color = row >= thumb && row < thumb + thumbHeight ? 247 : 237;
                content += $"\u001b[{row + 1};{edge}H\u001b[38;5;{color}m{rail}\u001b[0m\u001b[{row + 1};{edge}H";
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

    private IRenderable Render(Entry entry, int width, long animationFrame)
    {
        if (entry.User)
        {
            var rows = ComposerLayout.Create(entry.Text, 0, PromptBlock.ContentWidth(width, isInput: false), int.MaxValue).Lines
                .Select((line, index) => PromptBlock.Row(line, width, Accent, Unicode, showPrompt: index == 0));
            return new Rows(rows);
        }
        if (entry.Markdown)
        {
            var answer = new Grid().AddColumn(new GridColumn().Width(2).Padding(0, 0)).AddColumn(new GridColumn().Padding(0, 0));
            answer.AddRow(new Text(Unicode ? "\u25cf" : "*"), TranscriptMarkdown.Render(entry.Text, Accent));
            return answer;
        }
        if (entry.IsStep)
        {
            var duration = entry.Duration ?? Stopwatch.GetElapsedTime(entry.Started);
            var elapsed = ActivityStatus.FormatElapsed(duration);
            if (entry.Activity is not null && entry.Status != "running")
            {
                return new Text("  " + (entry.Status == "done" ? "Thought for " + elapsed : entry.Activity + " (" + entry.Status + ")"), Style.Parse("grey"));
            }
            var title = entry.Activity is { } activity
                ? activity
                : entry.Tool switch
                {
                    "read_file" => "Read",
                    "list_files" or "glob" => "List",
                    "grep" => "Search",
                    "run_command" => "Bash",
                    "write_file" => "Write",
                    "edit_file" => "Update",
                    "apply_patch" => "Patch",
                    _ => entry.Tool ?? string.Empty,
                };
            var running = entry.Status == "running";
            if (running && entry.Activity is not null && ModelMetrics is { } metrics)
            {
                duration = metrics.Elapsed;
            }
            var status = entry.Status == "done" || running || entry.Activity is not null ? string.Empty : $" ({entry.Status})";
            var marker = Unicode ? "\u25cf" : "*";
            if (running)
            {
                marker = (Unicode ? _unicodeDots : _asciiDots).GetFrame((int)(animationFrame % int.MaxValue));
            }
            var markerWidth = marker.GetCellWidth() + 1;
            var hint = running
                ? SpectreUserInterface.FormatActivityMetrics(duration, entry.Activity is not null ? OutputTokens : 0,
                    Unicode, interruptible: true, width, ModelMetrics?.TokensEstimated ?? true,
                    entry.Activity is not null && (ShowActivityTokens || OutputTokens > 0),
                    entry.Activity is not null ? ModelMetrics?.InputTokens ?? 0 : 0) : string.Empty;
            var separateHint = running && entry.Tool is not null && width < 40;
            var hintWidth = hint.Length > 0 && !separateHint ? hint.GetCellWidth() + 1 : 0;
            var suffix = running ? string.Empty : ComposerLayout.Clip(status, Math.Max(0, width - 4));
            var label = ComposerLayout.Clip(title + (entry.Tool is not null ? "(" + entry.Text + ")" : running ? "..." : string.Empty),
                Math.Max(0, width - markerWidth - hintWidth - suffix.GetCellWidth()));
            var header = running
                ? $"[{Accent}]{Markup.Escape(marker)}[/] [{Accent}]{Markup.Escape(label)}[/][grey]{Markup.Escape(suffix)}[/]"
                : $"[{(entry.Status == "done" ? Accent : "#ff6b6b")}]{Markup.Escape(marker)}[/] {Markup.Escape(label)}[grey]{Markup.Escape(suffix)}[/]";
            if (running && !separateHint)
            {
                header += $" [grey]{Markup.Escape(hint)}[/]";
            }
            List<IRenderable> content =
            [
                new Markup(header),
            ];
            if (separateHint)
            {
                content.Add(new Text("  " + hint, Style.Parse("grey")));
            }
            if (entry.Tool is not null)
            {
                if (entry.AddedLines is { } additions)
                {
                    AddOutput($"Added {additions} {(additions == 1 ? "line" : "lines")}, removed {entry.RemovedLines} {(entry.RemovedLines == 1 ? "line" : "lines")}");
                    foreach (var change in entry.FileChanges)
                    {
                        if (entry.FileChanges.Count > 1)
                        {
                            AddOutput(change.Path);
                        }
                        AddPreview(change.Preview, diff: true);
                    }
                    if (entry.Status == "failed")
                    {
                        AddOutput(entry.Output.Split('\n')[0]);
                    }
                }
                else if (entry.Output.Length > 0)
                {
                    AddPreview(entry.Output, diff: false);
                }
                else if (entry.Expanded)
                {
                    AddOutput("No output yet.");
                }
            }
            return new Rows(content);

            void AddOutput(string text, string style = "grey", bool first = true)
            {
                var prefix = first ? Unicode ? "  \u2514 " : "  | " : "    ";
                var clipped = ComposerLayout.Clip(text, Math.Max(0, width - prefix.Length));
                content.Add(new Text(prefix + clipped + new string(' ', Math.Max(0, width - prefix.Length - clipped.GetCellWidth())), Style.Parse(style)));
            }

            void AddPreview(string output, bool diff)
            {
                List<(string Text, string Style)> outputLines = [];
                int? oldLine = null;
                int? newLine = null;
                foreach (var sourceLine in output.TrimEnd('\n').Split('\n'))
                {
                    if (diff)
                    {
                        var hunk = HunkHeader().Match(sourceLine);
                        if (hunk.Success && int.TryParse(hunk.Groups[1].Value, CultureInfo.InvariantCulture, out var oldStart)
                            && int.TryParse(hunk.Groups[2].Value, CultureInfo.InvariantCulture, out var newStart))
                        {
                            oldLine = oldStart;
                            newLine = newStart;
                            continue;
                        }
                    }
                    var style = diff && sourceLine.StartsWith('+') ? "#9be9a8 on #003300"
                        : diff && sourceLine.StartsWith('-') ? "#ff9d9d on #440000" : "grey";
                    var line = sourceLine;
                    if (diff && oldLine is not null && newLine is not null && sourceLine.Length > 0)
                    {
                        var number = sourceLine[0] == '-' ? oldLine : newLine;
                        line = number.Value.ToString(CultureInfo.InvariantCulture).PadLeft(4) + " " + sourceLine;
                        if (sourceLine[0] != '+')
                        {
                            oldLine++;
                        }
                        if (sourceLine[0] != '-')
                        {
                            newLine++;
                        }
                    }
                    outputLines.Add((line, style));
                }
                var count = entry.Expanded ? outputLines.Count : Math.Min(diff ? 8 : 3, outputLines.Count);
                for (var index = 0; index < count; index++)
                {
                    AddOutput(outputLines[index].Text, outputLines[index].Style, first: !diff && index == 0);
                }
                if (outputLines.Count > (diff ? 8 : 3))
                {
                    var icon = entry.Expanded ? Unicode ? "\u25be" : "v" : Unicode ? "\u25b8" : ">";
                    var label = entry.Expanded ? icon : $"{icon} {outputLines.Count - count} more lines";
                    entry.DisclosureRows.Add(content.Count);
                    AddOutput(label, style: "white", first: false);
                }
            }
        }
        return new Text(entry.Text, Style.Parse(entry.Style));
    }

    [GeneratedRegex(@"^@@ -(\d+)(?:,\d+)? \+(\d+)(?:,\d+)? @@", RegexOptions.CultureInvariant)]
    private static partial Regex HunkHeader();

    private sealed class Entry
    {
        public string Text { get; set; } = string.Empty;
        public string Output { get; set; } = string.Empty;
        public string Style { get; init; } = "default";
        public string? Tool { get; init; }
        public IReadOnlyList<ToolFileChange> FileChanges { get; set; } = [];
        public List<int> DisclosureRows { get; } = [];
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

        public void Finish(string status)
        {
            Status = status;
            Duration = Started == 0 ? null : Stopwatch.GetElapsedTime(Started);
        }
    }
}
