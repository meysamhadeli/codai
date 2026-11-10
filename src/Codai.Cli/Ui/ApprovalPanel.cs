using Codai.Core;
using Codai.Core.Tools;
using Spectre.Console;

namespace Codai.Cli.Ui;

/// <summary>
/// The decision panel shown before a tool that can change something runs.
/// </summary>
/// <remarks>
/// The question is only useful if the thing being approved is visible, so the panel puts the exact
/// command, path, or patch in a wide preview above a short list of choices, rather than appending
/// a clipped summary to a yes/no line. Arrow keys and number keys both work, so approving takes one
/// keystroke and Escape refuses from anywhere in the panel.
/// </remarks>
internal sealed class ApprovalPanel : IInlineDecision
{
    /// <summary>Returned when the call may run once.</summary>
    public const string Allow = "allow";

    /// <summary>Returned when the user does not want to be asked about this tool again this session.</summary>
    public const string AllowAlways = "always";

    /// <summary>Returned when the user refuses.</summary>
    public const string Deny = "deny";

    private const int MaxPreviewLines = 10;
    private static readonly string[] CompactOptions = ["1. Yes", "2. No", "3. Remember tool"];

    private readonly ToolApprovalRequest _request;
    private readonly bool _unicode;
    private readonly string[] _options;
    private int _previewOffset;
    private int _previewRows = 1;

    public ApprovalPanel(ToolApprovalRequest request, bool unicode = true)
    {
        _request = request;
        _unicode = unicode;
        _options =
        [
            "Yes",
            "No",
            "Yes, and don't ask again for " + request.ToolName + " this session",
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
            case ConsoleKey.PageDown:
                _previewOffset += Math.Max(1, _previewRows - 1);
                break;
            case ConsoleKey.PageUp:
                _previewOffset = Math.Max(0, _previewOffset - Math.Max(1, _previewRows - 1));
                break;
            case ConsoleKey.UpArrow:
                Selected = Math.Max(0, Selected - 1);
                break;
            case ConsoleKey.DownArrow or ConsoleKey.Tab:
                Selected = Math.Min(_options.Length - 1, Selected + 1);
                break;
            case ConsoleKey.Enter:
                Choose(Selected);
                break;
            case ConsoleKey.Y or ConsoleKey.D1 or ConsoleKey.NumPad1:
                Choose(0);
                break;
            case ConsoleKey.N or ConsoleKey.D2 or ConsoleKey.NumPad2:
                Choose(1);
                break;
            case ConsoleKey.A or ConsoleKey.D3 or ConsoleKey.NumPad3:
                Choose(2);
                break;
            case ConsoleKey.Escape:
                Choose(1);
                break;
        }
    }

    /// <summary>Renders a framed preview with highlighted decisions and keyboard navigation.</summary>
    public int MeasureHeight(int width, int maximumHeight)
    {
        var inner = Math.Max(1, width - 6);
        var previewRows = Math.Min(MaxPreviewLines,
            ComposerLayout.Create(TranscriptMarkdown.Clean(_request.PreviewOrDefault), 0, inner, int.MaxValue).Lines.Count);
        var choiceRows = _options.Sum(option => SettingsPalette.WrapText(option, Math.Max(1, inner - 5), 3).Length);
        var detailRows = string.IsNullOrWhiteSpace(_request.Detail) ? 0 : SettingsPalette.WrapText(_request.DetailOrDefault, inner, 2).Length;
        return Math.Min(Math.Min(24, maximumHeight), 8 + previewRows + choiceRows + detailRows);
    }

    public void Draw(IAnsiConsole console, string theme = "graphite", int minimumTop = 1)
    {
        var width = Math.Max(1, console.Profile.Width - 2);
        var height = MeasureHeight(console.Profile.Width, Math.Max(1, console.Profile.Height - minimumTop - 1));
        var inner = Math.Max(1, width - 4);
        var innerHeight = Math.Max(1, height - 2);
        var highlight = "bold " + TerminalThemes.Find(theme).Accent;
        const string surface = "default";
        const string muted = "grey62";
        var unicode = _unicode && console.Profile.Capabilities.Unicode;
        var choices = _options.Select((option, index) => SettingsPalette.WrapText($"{index + 1}. {option}", Math.Max(1, inner - 2), 3)).ToArray();
        var choiceRows = choices.Sum(choice => choice.Length);
        if (choiceRows > innerHeight - 4)
        {
            choices = CompactOptions
                .Select(label => new[] { ComposerLayout.Clip(label, Math.Max(1, inner - 2)) }).ToArray();
            choiceRows = choices.Length;
        }
        var lines = new List<(string Text, string Style)>
        {
            (ComposerLayout.Clip(Title, inner), "bold " + surface),
            (new string(unicode ? '\u2500' : '-', inner), "grey23"),
        };
        var preview = TranscriptMarkdown.Clean(_request.PreviewOrDefault).TrimEnd('\n');
        var previewLines = ComposerLayout.Create(preview, 0, inner, int.MaxValue).Lines;
        var previewBudget = Math.Min(MaxPreviewLines, Math.Max(0, innerHeight - lines.Count - choiceRows - 1));
        _previewRows = Math.Max(1, previewBudget);
        _previewOffset = Math.Clamp(_previewOffset, 0, Math.Max(0, previewLines.Count - _previewRows));
        for (var row = 0; row < Math.Min(previewLines.Count - _previewOffset, previewBudget); row++)
        {
            var text = previewLines[_previewOffset + row];
            var omitted = previewLines.Count - _previewOffset - row;
            var style = _request.ToolName is "write_file" or "edit_file" or "apply_patch" ? text.FirstOrDefault() switch
            {
                '+' => "#9be9a8 on #102819",
                '-' => "#ff9d9d on #30191b",
                _ => "white on grey11",
            } : "white on grey11";
            lines.Add((row == previewBudget - 1 && omitted > 1 ? $"... {omitted} more lines" : text, style));
        }
        var detailBudget = innerHeight - lines.Count - choiceRows - 3;
        if (!string.IsNullOrWhiteSpace(_request.Detail) && detailBudget > 0)
        {
            lines.AddRange(SettingsPalette.WrapText(_request.DetailOrDefault, inner, Math.Min(2, detailBudget)).Select(line => (line, muted)));
        }
        if (lines.Count + choiceRows + 2 < innerHeight)
        {
            lines.Add((string.Empty, surface));
            lines.Add(("Do you want to proceed?", muted));
        }
        for (var index = 0; index < choices.Length; index++)
        {
            var style = index == Selected ? highlight : surface;
            for (var row = 0; row < choices[index].Length; row++)
            {
                var marker = index == Selected && row == 0 ? unicode ? "\u276f " : "> " : "  ";
                lines.Add((marker + choices[index][row], style));
            }
        }
        while (lines.Count < innerHeight - 1)
        {
            lines.Add((string.Empty, surface));
        }
        lines.Add((DecisionPanel.SelectionFooter(inner, unicode), muted));
        DecisionPanel.DrawFrame(console, lines, height, minimumTop, unicode);
    }

    private void Choose(int index)
    {
        Selected = Math.Clamp(index, 0, _options.Length - 1);
        Command = Selected switch { 0 => Allow, 2 => AllowAlways, _ => Deny };
        Closed = true;
    }
}
