using Codai.Core;
using Codai.Core.Agents;
using Spectre.Console;

namespace Codai.Cli.Ui;

public sealed class SettingsPalette : IOverlay
{
    private static readonly (string Title, string Detail, string Command)[] Actions =
    [
        ("Session settings", "Edit local configuration with typed values", ":preferences"),
        ("Select model", "Set the model used for future requests", ":model"),
        ("Provider configuration", "Edit provider, endpoint, and masked credentials", ":preferences provider"),
        ("Theme", "Choose a color theme to apply after saving", ":preferences theme"),
        ("MCP connections", "Enable, disable, or remove configured servers", ":preferences mcp"),
        ("Plugin configuration", "Enable, disable, or remove plugin entries", ":preferences plugins"),
        ("Skills and workflows", "Choose a command to insert into the composer", ":preferences skills"),
        ("Conversation branch", "Save an independent copy of the conversation", ":session fork"),
        ("Checkpoint recovery", "Choose saved task, checkpoint, and restore scope", ":session restore"),
        ("Fresh conversation", "Archive current history and reset the conversation", ":session new"),
        ("Saved conversations", "Browse and continue a saved conversation", ":session history"),
        ("Resolved configuration", "Inspect effective settings and their sources", ":config"),
        ("Workspace rules", "Inspect active instruction files", ":config rules"),
        ("Plan mode", "Prepare a plan before implementation", "mode:Plan"),
        ("Agent mode", "Work with the available workspace tools", "mode:Agent"),
        ("Approval policy", "Toggle confirmation for tool execution", "approval"),
        ("Session commands", "View available configuration and task commands", ":help"),
    ];

    public string Query { get; private set; } = string.Empty;
    public int Selected { get; private set; }
    public bool Closed { get; private set; }
    public string? Command { get; private set; }
    public bool EditingModel { get; private set; }
    private readonly AgentMode _mode;
    private readonly string? _title;
    private readonly bool _inputOnly;
    private readonly bool _secret;
    private readonly bool _question;
    private readonly (string Title, string Detail, string Command)[] _actions = Actions;
    private readonly ComposerBuffer _valueEditor = new([]);

    public SettingsPalette(AgentMode mode = AgentMode.Agent) => _mode = mode;

    public SettingsPalette(string title, IReadOnlyList<Codai.Core.Abstractions.SessionMenuItem>? items = null, string initialValue = "", bool secret = false, bool question = false)
    {
        _title = title;
        _inputOnly = items is null;
        _secret = secret;
        _question = question;
        Query = initialValue;
        _valueEditor.Insert(initialValue);
        _actions = items?.Select(item => (item.Label, item.Description, item.Value)).ToArray() ?? [];
    }

    public void HandleKey(ConsoleKeyInfo key)
    {
        if ((_inputOnly || EditingModel) && key.Key is not (ConsoleKey.Enter or ConsoleKey.Escape))
        {
            if (_valueEditor.Text.Length < 4096 || char.IsControl(key.KeyChar))
            {
                _valueEditor.HandleKey(key);
                Query = _valueEditor.Text;
            }
            return;
        }
        var matches = Matches();
        if (key.Key == ConsoleKey.Escape)
        {
            Closed = true;
        }
        else if (key.Key == ConsoleKey.Enter)
        {
            if (_inputOnly)
            {
                Command = Query;
                Closed = true;
            }
            else if (EditingModel)
            {
                if (!string.IsNullOrWhiteSpace(Query))
                {
                    var model = Query.Trim();
                    var separator = model.IndexOf('/');
                    if (separator > 0 && separator < model.Length - 1 && !model.Any(char.IsWhiteSpace))
                    {
                        Command = ":model " + _mode.ToString().ToLowerInvariant() + " " + model;
                        Closed = true;
                    }
                }
            }
            else if (matches.Length > 0)
            {
                var action = matches[Selected];
                if (_title is null && action.Command == ":model")
                {
                    EditingModel = true;
                    Query = string.Empty;
                    Selected = 0;
                }
                else
                {
                    Command = action.Command;
                    Closed = true;
                }
            }
        }
        else if (!_inputOnly && !EditingModel && key.Key is ConsoleKey.UpArrow or ConsoleKey.DownArrow)
        {
            Selected = Math.Clamp(Selected + (key.Key == ConsoleKey.UpArrow ? -1 : 1), 0, Math.Max(0, matches.Length - 1));
        }
        else if (key.Key == ConsoleKey.Backspace && Query.Length > 0)
        {
            Query = Query[..(Query.Length - (char.IsLowSurrogate(Query[^1]) && Query.Length > 1 ? 2 : 1))];
            Selected = 0;
        }
        else if (!char.IsControl(key.KeyChar) && Query.Length < 256)
        {
            Query += key.KeyChar;
            Selected = 0;
        }
    }

    public void Draw(IAnsiConsole console, string theme = "graphite", int minimumTop = 1)
    {
        var width = Math.Min(76, Math.Max(1, console.Profile.Width - 4));
        var height = Math.Min(28, Math.Max(1, console.Profile.Height - minimumTop - 1));
        var left = Math.Max(1, (console.Profile.Width - width) / 2 + 1);
        var top = Math.Max(minimumTop, (console.Profile.Height - height) / 2 + 1);
        var inner = Math.Max(1, width - 4);
        var matches = Matches();
        var itemRows = matches.All(match => match.Detail.Length == 0) ? 2 : 3;
        var count = _question ? matches.Length : Math.Max(1, (height - 7) / itemRows);
        var first = Math.Max(0, Selected - count + 1);
        var title = _title ?? (EditingModel ? "Codai / Model" : "Codai / Settings");
        IReadOnlyList<string> titleLines = _question
            ? ComposerLayout.Create(TranscriptMarkdown.Clean(title).Replace('\n', ' '), 0, inner, Math.Max(1, (height - 6) / 3)).Lines
            : [title];
        var lines = titleLines.Select(line => (Text: line, Style: "bold white on grey15")).ToList();
        var inputLabel = _inputOnly ? (_question ? "Answer: " : "Value: ") : EditingModel ? "Model: " : "Search: ";
        var inputRow = lines.Count + 1;
        lines.AddRange([
            (string.Empty, "white on grey15"),
            (inputLabel + (_secret ? new string('*', Query.Length) : Query), "white on grey23"),
            (string.Empty, "white on grey15"),
        ]);
        if (_inputOnly)
        {
            lines.Add((string.Empty, "grey70 on grey15"));
        }
        else if (EditingModel)
        {
            lines.Add(($"{_mode}: provider/model", "grey70 on grey15"));
        }
        else if (matches.Length == 0)
        {
            lines.Add((_question ? "No matching choices" : "No matching settings", "grey70 on grey15"));
        }
        else
        {
            IReadOnlyList<string>[] labels = matches.Select(match => _question
                ? ComposerLayout.Create(TranscriptMarkdown.Clean(match.Title).Replace('\n', ' '), 0, inner, Math.Max(1, height - lines.Count - 1)).Lines
                : (IReadOnlyList<string>)[match.Title]).ToArray();
            if (_question)
            {
                while (first < Selected && labels.Skip(first).Take(Selected - first + 1).Sum(label => label.Count + 1) > height - lines.Count)
                {
                    first++;
                }
            }
            for (var index = first; index < Math.Min(matches.Length, first + count); index++)
            {
                if (_question && lines.Count + labels[index].Count + 1 > height)
                {
                    break;
                }
                var highlight = TerminalThemes.Find(theme).Highlight;
                var style = index == Selected ? highlight : "white on grey15";
                lines.AddRange(labels[index].Select(label => (label, style)));
                if (!_question && matches[index].Detail.Length > 0)
                {
                    lines.Add((matches[index].Detail, index == Selected ? style : "grey70 on grey15"));
                }
                lines.Add((new string('-', inner), "grey35 on grey15"));
            }
        }
        for (var row = 0; row < height; row++)
        {
            var line = row < lines.Count ? lines[row] : (string.Empty, "white on grey15");
            console.Write(new ControlCode($"\u001b[{top + row};1H\u001b[2K\u001b[{top + row};{left}H"));
            var text = ComposerLayout.Clip(TranscriptMarkdown.Clean(line.Item1).Replace('\n', ' '), inner);
            console.Write(new Text("  " + text + new string(' ', Math.Max(0, width - 2 - text.GetCellWidth())), Style.Parse(line.Item2)));
        }
        var cursorWidth = (_inputOnly || EditingModel) ? (_secret ? _valueEditor.Cursor : Query[.._valueEditor.Cursor].GetCellWidth()) : Query.GetCellWidth();
        console.Write(new ControlCode($"\u001b[{top + Math.Min(inputRow, height - 1)};{Math.Min(console.Profile.Width - 1, left + 2 + Math.Min(inner - 1, (_question ? inputLabel.Length : _inputOnly || EditingModel ? 7 : 8) + cursorWidth))}H"));
    }

    private (string Title, string Detail, string Command)[] Matches() => _actions.Where(action =>
        (action.Title + " " + action.Detail).Contains(Query, StringComparison.OrdinalIgnoreCase)).ToArray();
}
