using Codai.Core;
using Codai.Core.Tools;
using Spectre.Console;

namespace Codai.Cli.Ui;

/// <summary>
/// The decision panel shown before a tool that can change something runs.
/// </summary>
/// <remarks>
/// The question is only useful if the thing being approved is visible, so the panel puts the exact
/// command, path, or patch in a bordered block above a short list of choices, rather than appending
/// a clipped summary to a yes/no line. Arrow keys and number keys both work, so approving takes one
/// keystroke and Escape refuses from anywhere in the panel.
/// </remarks>
internal sealed class ApprovalPanel : IOverlay
{
    /// <summary>Returned when the call may run once.</summary>
    public const string Allow = "allow";

    /// <summary>Returned when the user does not want to be asked about this tool again this session.</summary>
    public const string AllowAlways = "always";

    /// <summary>Returned when the user refuses.</summary>
    public const string Deny = "deny";

    private const int MaxPreviewLines = 10;

    private readonly ToolApprovalRequest _request;
    private readonly bool _unicode;
    private readonly string[] _options;

    public ApprovalPanel(ToolApprovalRequest request, bool unicode = true)
    {
        _request = request;
        _unicode = unicode;
        _options =
        [
            "Yes",
            "Yes, and don't ask again for " + request.ToolName + " this session",
            "No, and tell Codai what to do instead",
        ];
    }

    /// <inheritdoc />
    public bool Closed { get; private set; }

    /// <inheritdoc />
    public string? Command { get; private set; }

    /// <summary>Gets the highlighted option.</summary>
    public int Selected { get; private set; }

    /// <summary>Gets the heading, which names what kind of thing is about to happen.</summary>
    public string Title => _request.ToolName switch
    {
        "run_command" => "Run this command?",
        "write_file" => "Write this file?",
        "edit_file" or "apply_patch" => "Apply these changes?",
        "task" => "Start a sub-agent?",
        _ => "Run " + _request.ToolName + "?",
    };

    /// <summary>Gets the labels of the three choices.</summary>
    public IReadOnlyList<string> Options => _options;

    /// <summary>Gets the body lines: the preview, then a one-line explanation.</summary>
    public IReadOnlyList<string> Body()
    {
        var preview = _request.PreviewOrDefault.Replace("\r", string.Empty, StringComparison.Ordinal).TrimEnd('\n');
        var lines = preview.Length == 0 ? [] : preview.Split('\n').ToList();
        if (lines.Count > MaxPreviewLines)
        {
            var omitted = lines.Count - MaxPreviewLines;
            lines = lines.Take(MaxPreviewLines).ToList();
            lines.Add($"… {omitted} more lines");
        }

        if (!string.IsNullOrWhiteSpace(_request.Detail))
        {
            lines.Add(string.Empty);
            lines.Add(_request.DetailOrDefault);
        }

        return lines;
    }

    public void HandleKey(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.UpArrow:
                Selected = Math.Max(0, Selected - 1);
                break;
            case ConsoleKey.DownArrow or ConsoleKey.Tab:
                Selected = Math.Min(_options.Length - 1, Selected + 1);
                break;
            case ConsoleKey.Enter or ConsoleKey.Y or ConsoleKey.D1 or ConsoleKey.NumPad1:
                Choose(Selected);
                break;
            case ConsoleKey.A or ConsoleKey.D2 or ConsoleKey.NumPad2:
                Choose(1);
                break;
            case ConsoleKey.N or ConsoleKey.D3 or ConsoleKey.NumPad3:
                Choose(2);
                break;
            case ConsoleKey.Escape:
                Choose(2);
                break;
        }
    }

    /// <summary>Renders the panel as a bordered block centred in the terminal.</summary>
    public void Draw(IAnsiConsole console, string theme = "graphite", int minimumTop = 1)
    {
        var width = Math.Min(84, Math.Max(24, console.Profile.Width - 4));
        var inner = Math.Max(8, width - 4);
        var left = Math.Max(1, (console.Profile.Width - width) / 2 + 1);
        var highlight = TerminalThemes.Find(theme).Highlight;
        var lines = Content(inner);
        var top = Math.Max(minimumTop, (console.Profile.Height - lines.Count - 2) / 2 + 1);

        Border(console, left, top, width, isTop: true);
        for (var row = 0; row < lines.Count; row++)
        {
            var rowTop = top + 1 + row;
            console.Write(new ControlCode($"\u001b[{rowTop};1H\u001b[2K\u001b[{rowTop};{left}H"));
            WriteRow(console, lines[row], inner);
        }
        Border(console, left, top + lines.Count + 1, width, isTop: false);

        void WriteRow(IAnsiConsole target, (string Text, string Style) line, int width)
        {
            var text = ComposerLayout.Clip(TranscriptMarkdown.Clean(line.Text), width);
            target.Write(new Text(_unicode ? "│ " : "| ", Style.Parse("grey")));
            target.Write(new Text(text + new string(' ', Math.Max(0, width - text.GetCellWidth())), Style.Parse(line.Style)));
            target.Write(new Text(_unicode ? " │" : " |", Style.Parse("grey")));
        }

        List<(string Text, string Style)> Content(int width)
        {
            var content = new List<(string Text, string Style)>
            {
                (Title, "bold white"),
                (string.Empty, "white"),
            };
            var preview = _request.PreviewOrDefault.Replace("\r", string.Empty, StringComparison.Ordinal).TrimEnd('\n');
            content.AddRange(ComposerLayout.Create(preview, 0, width, MaxPreviewLines).Lines.Select(line => (line, "white")));
            if (!string.IsNullOrWhiteSpace(_request.Detail))
            {
                content.Add((string.Empty, "white"));
                content.AddRange(ComposerLayout.Create(_request.DetailOrDefault, 0, width, 3).Lines.Select(line => (line, "grey70")));
            }
            content.Add((string.Empty, "white"));
            content.Add(("Do you want to proceed?", "bold white"));
            for (var index = 0; index < _options.Length; index++)
            {
                var marker = index == Selected ? (_unicode ? "❯ " : "> ") : "  ";
                content.Add((marker + $"{index + 1}. {_options[index]}", index == Selected ? highlight : "grey70"));
            }
            return content;
        }
    }

    private void Border(IAnsiConsole console, int left, int top, int width, bool isTop)
    {
        console.Write(new ControlCode($"\u001b[{top};1H\u001b[2K\u001b[{top};{left}H"));
        var fill = new string(_unicode ? '─' : '-', Math.Max(0, width - 2));
        console.Write(new Text(
            _unicode
                ? (isTop ? ((char)0x256D).ToString() + fill + (char)0x256E : ((char)0x2570).ToString() + fill + (char)0x256F)
                : "+" + fill + "+",
            Style.Parse("grey")));
    }

    private void Choose(int index)
    {
        Selected = Math.Clamp(index, 0, _options.Length - 1);
        Command = Selected switch { 0 => Allow, 1 => AllowAlways, _ => Deny };
        Closed = true;
    }
}
