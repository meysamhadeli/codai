using System.Text.Json;
using Codai.Core;
using Codai.Core.Abstractions;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Codai.Cli.Ui;

public sealed class SettingsPalette : IOverlay
{
    private static readonly (string Title, string Detail, string Command, string Category)[] Actions =
    [
        ("Session", "Saved conversations", "/session", "Suggested"),
        ("New", "Create a new session", "/new", "Suggested"),
        ("Models", "Current mode's model", "/models", "Suggested"),
        ("Agents", "Agents: agent / plan", "/agents", "Session"),
        ("Compact", "Summarize conversation to free context", "/compact", "Session"),
        ("Auto-compact", "Choose automatic compaction window", "/autocompact", "Session"),
        ("Skills", "Enabled skill availability", "/skills", "Session"),
        ("Diff", "Working tree and staged", "/diff", "Session"),
        ("Themes", "Terminal colors", "/themes", "Customization"),
        ("Plugins", "Plugin availability", ":preferences plugins", "Customization"),
        ("MCPs", "MCP server availability", "/mcps", "Customization"),
        ("Auth", "Authenticate provider", "/auth", "Customization"),
        ("Help", "Session commands", "/help", "System"),
        ("Docs", "Open documentation", ":docs", "System"),
        ("Exit", "Leave Codai", "/exit", "System"),
    ];

    public string Query { get; private set; } = string.Empty;
    public int Selected { get; private set; }
    public bool Closed { get; private set; }
    public string? Command { get; private set; }
    private readonly string? _title;
    private readonly bool _inputOnly;
    private readonly bool _secret;
    private readonly bool _question;
    private readonly SessionMenuOptions? _options;
    private readonly IReadOnlyList<SessionMenuItem> _items = [];
    private readonly (string Title, string Detail, string Command, string Category)[] _actions = Actions;
    private readonly ComposerBuffer _valueEditor = new([]);
    private readonly HashSet<string> _checked = new(StringComparer.Ordinal);
    private bool _searchingSettings = true;
    private static readonly string[] SettingsSections = ["Status", "Config", "Usage", "Stats"];
    private bool IsSettingsView => _title is null || _title == "Codai / Edit configuration" ||
        _title.StartsWith("Codai / Settings / ", StringComparison.Ordinal);
    private string SettingsSection => _title?.StartsWith("Codai / Settings / ", StringComparison.Ordinal) == true
        ? _title["Codai / Settings / ".Length..] : "Config";
    internal bool ShowCursor => _options?.HorizontalSelection != true &&
        (_inputOnly || !_question && (_title is null ? _searchingSettings : Query.Length > 0));

    public SettingsPalette() { }

    public SettingsPalette(string title, IReadOnlyList<SessionMenuItem>? items = null, string initialValue = "", bool secret = false, bool question = false, SessionMenuOptions? options = null)
    {
        _title = title;
        _inputOnly = items is null;
        _secret = secret;
        _question = question;
        _options = options;
        _items = items ?? [];
        _checked.UnionWith(_items.Where(item => item.IsCurrent).Select(item => item.Value));
        Query = initialValue;
        _valueEditor.Insert(initialValue);
        _actions = items?.Select(item => (item.Label, item.Description, item.Value, item.Group)).ToArray() ?? [];
        Selected = Math.Max(0, Array.FindIndex(_actions, item => item.Command == options?.InitialValue));
    }

    public void HandleKey(ConsoleKeyInfo key)
    {
        if (_options?.HorizontalSelection == true)
        {
            if (key.Key is ConsoleKey.LeftArrow or ConsoleKey.RightArrow or ConsoleKey.UpArrow or ConsoleKey.DownArrow)
            {
                Selected = Math.Clamp(Selected + (key.Key is ConsoleKey.LeftArrow or ConsoleKey.UpArrow ? -1 : 1),
                    0, Math.Max(0, _actions.Length - 1));
            }
            else if (key.Key is ConsoleKey.Enter or ConsoleKey.Escape)
            {
                Command = key.Key == ConsoleKey.Enter && _actions.Length > 0 ? _actions[Selected].Command : null;
                Closed = true;
            }
            return;
        }
        if (IsSettingsView && key.Key is ConsoleKey.Tab or ConsoleKey.LeftArrow or ConsoleKey.RightArrow)
        {
            var direction = key.Key == ConsoleKey.LeftArrow || key.Modifiers.HasFlag(ConsoleModifiers.Shift) ? -1 : 1;
            var section = SettingsSections[(Array.IndexOf(SettingsSections, SettingsSection) + direction + SettingsSections.Length) % SettingsSections.Length].ToLowerInvariant();
            Command = _title is null ? "/settings " + section : "@settings:" + section;
            Closed = true;
            return;
        }
        if (IsSettingsView && key.KeyChar == '/' && (!_searchingSettings || Query.Length == 0))
        {
            _searchingSettings = true;
            Query = string.Empty;
            Selected = 0;
            return;
        }
        if (IsSettingsView && key.Key is ConsoleKey.UpArrow or ConsoleKey.DownArrow or ConsoleKey.Home or ConsoleKey.End or ConsoleKey.PageUp or ConsoleKey.PageDown)
        {
            _searchingSettings = false;
        }
        if (_inputOnly && key.Key is not (ConsoleKey.Enter or ConsoleKey.Escape))
        {
            if (_valueEditor.Text.Length < OverlayPaste.MaxValueLength || char.IsControl(key.KeyChar))
            {
                _valueEditor.HandleKey(key);
                Query = _valueEditor.Text;
            }
            return;
        }
        var matches = Matches();
        var shortcut = key.Modifiers == ConsoleModifiers.Control
            ? _options?.Shortcuts.FirstOrDefault(action => action.Key == key.Key) : null;
        if (shortcut is not null)
        {
            if (!shortcut.IncludeSelection || matches.Length > 0)
            {
                Command = shortcut.Result + (shortcut.IncludeSelection ? matches[Selected].Command : string.Empty);
                Closed = true;
            }
            return;
        }
        if (key.Key == ConsoleKey.Escape)
        {
            if (_options?.MultiSelect == true)
            {
                Command = JsonSerializer.Serialize(_items.Where(item => _checked.Contains(item.Value)).Select(item => item.Value).ToArray());
            }
            Closed = true;
        }
        else if (_options?.MultiSelect == true && key.Key == ConsoleKey.Enter)
        {
            if (matches.Length > 0 && !_checked.Add(matches[Selected].Command))
            {
                _checked.Remove(matches[Selected].Command);
            }
        }
        else if (key.Key == ConsoleKey.Enter || key.Key == ConsoleKey.Spacebar &&
            (_options?.ToggleOnSpace == true || IsSettingsView && (!_searchingSettings || Query.Length == 0)))
        {
            if (_inputOnly)
            {
                Command = Query;
                Closed = true;
            }
            else if (matches.Length > 0)
            {
                var action = matches[Selected];
                Command = action.Command;
                Closed = true;
            }
        }
        else if (!_inputOnly && key.Key is ConsoleKey.UpArrow or ConsoleKey.DownArrow)
        {
            Selected = Math.Clamp(Selected + (key.Key == ConsoleKey.UpArrow ? -1 : 1), 0, Math.Max(0, matches.Length - 1));
        }
        else if (!_inputOnly && key.Key is ConsoleKey.Home or ConsoleKey.End or ConsoleKey.PageUp or ConsoleKey.PageDown)
        {
            Selected = key.Key switch
            {
                ConsoleKey.Home => 0,
                ConsoleKey.End => Math.Max(0, matches.Length - 1),
                ConsoleKey.PageUp => Math.Max(0, Selected - 6),
                _ => Math.Min(Math.Max(0, matches.Length - 1), Selected + 6),
            };
        }
        else if (key.Key == ConsoleKey.Backspace && Query.Length > 0)
        {
            _searchingSettings = true;
            Query = Query[..(Query.Length - (char.IsLowSurrogate(Query[^1]) && Query.Length > 1 ? 2 : 1))];
            Selected = 0;
        }
        else if (!char.IsControl(key.KeyChar) && Query.Length < 256)
        {
            _searchingSettings = true;
            Query += key.KeyChar;
            Selected = 0;
        }
    }

    public bool AcceptsPaste => _inputOnly;

    public bool HandlePaste(string text)
    {
        if (!_inputOnly)
        {
            return false;
        }
        if (OverlayPaste.TryAppend(_valueEditor, text))
        {
            Query = _valueEditor.Text;
        }
        // True even when nothing was inserted: a paste that falls through would be
        // handled as a plain Ctrl+V keystroke.
        return true;
    }

    public void Draw(IAnsiConsole console, string theme = "graphite", int minimumTop = 1)
    {
        if (_options?.HorizontalSelection == true)
        {
            DrawHorizontal(console, theme, minimumTop);
            return;
        }
        if (minimumTop == 1)
        {
            console.Write(new ControlCode("\u001b[1;1H\u001b[2K"));
        }
        if (_title is null)
        {
            DrawSettings(console, theme, minimumTop);
            return;
        }
        if (!_inputOnly && !_question)
        {
            DrawPicker(console, theme, minimumTop);
            return;
        }
        var width = Math.Max(1, console.Profile.Width - 4);
        var padding = Math.Min(width >= 40 ? 3 : 2, Math.Max(0, (width - 1) / 2));
        var inner = Math.Max(1, width - padding * 2);
        var footer = new[] { DecisionPanel.SelectionFooter(inner, console.Profile.Capabilities.Unicode) };
        var footerRows = footer.Length == 0 ? 2 : footer.Length + 2;
        var desiredHeight = _inputOnly ? 8 : _question ? 24 : Math.Max(_options is null ? 8 : 11,
            _actions.Length + 7 + _actions.Select(action => action.Category).Where(category => category.Length > 0).Distinct().Count() * 2 + footerRows - 2);
        var height = Math.Min(Math.Min(_title is null ? 28 : 24, desiredHeight), Math.Max(1, console.Profile.Height - minimumTop - 1));
        var left = 3;
        var top = Math.Max(2, minimumTop);
        var matches = Matches();
        var appearance = TerminalThemes.Find(theme);
        const string surface = "default";
        const string muted = "grey62";
        var highlight = "bold " + appearance.Accent;
        var title = _title ?? "Commands";
        var showEscape = !_question && title.GetCellWidth() + 5 <= inner;
        IReadOnlyList<string> titleLines = _question
            ? ComposerLayout.Create(TranscriptMarkdown.Clean(title).Replace('\n', ' '), 0, inner, Math.Max(1, (height - 6) / 3)).Lines
            : [ComposerLayout.Clip(title, Math.Max(1, inner - (showEscape ? 5 : 0)))];
        var lines = new List<(string Text, string Style)> { (string.Empty, surface) };
        var descriptions = new Dictionary<int, (int Column, string Text)>();
        lines.AddRange(titleLines.Select((line, index) =>
            (line + (index == 0 && showEscape ? new string(' ', Math.Max(1, inner - line.GetCellWidth() - 3)) + "esc" : string.Empty), "bold " + surface)));
        if (showEscape)
        {
            descriptions[1] = (inner - 3, "esc");
        }
        lines.Add((string.Empty, surface));
        var inputRow = lines.Count;
        var inputLabel = _inputOnly ? (_question ? "Answer: " : "Value: ") : string.Empty;
        var value = _secret ? new string('*', Query.Length) : Query;
        var cursorWidth = _inputOnly
            ? (_secret ? _valueEditor.Cursor : Query[.._valueEditor.Cursor].GetCellWidth())
            : Query.GetCellWidth();
        var inputWidth = Math.Max(1, inner - inputLabel.GetCellWidth());
        var offset = Math.Max(0, cursorWidth - inputWidth + 1);
        var skipped = 0;
        var start = 0;
        while (start < value.Length && skipped < offset)
        {
            var length = char.IsHighSurrogate(value[start]) && start + 1 < value.Length ? 2 : 1;
            skipped += value.Substring(start, length).GetCellWidth();
            start += length;
        }
        lines.Add((inputLabel + (value.Length == 0 && !_inputOnly ? _options?.Placeholder ?? "Search" : ComposerLayout.Clip(value[start..], inputWidth)), value.Length == 0 ? "grey62 on grey11" : "white on grey11"));
        lines.Add((string.Empty, surface));
        var count = Math.Max(1, height - lines.Count - footerRows);
        var first = Math.Max(0, Selected - count + 1);
        if (_inputOnly)
        {
            lines.Add((string.Empty, surface));
        }
        else if (matches.Length == 0)
        {
            lines.Add((_question ? "No matching choices" : _options?.EmptyText ?? "No matching settings", muted));
        }
        else
        {
            IReadOnlyList<string>[] labels = matches.Select(match => _question
                ? WrapText(TranscriptMarkdown.Clean(match.Title).Replace('\n', ' '), inner, Math.Max(1, height - lines.Count - 1))
                : (IReadOnlyList<string>)[match.Title]).ToArray();
            if (_question)
            {
                while (first < Selected && labels.Skip(first).Take(Selected - first + 1).Sum(label => label.Count + 1) > height - lines.Count)
                {
                    first++;
                }
            }
            else
            {
                while (first < Selected && CategoryRowCount(first, Selected) > count)
                {
                    first++;
                }
            }
            var lastVisible = first;
            for (var index = first; index < Math.Min(matches.Length, first + count); index++)
            {
                if (_question && lines.Count + labels[index].Count + 1 > height)
                {
                    break;
                }
                var heading = !_question && matches[index].Category.Length > 0 &&
                    (index == first || matches[index].Category != matches[index - 1].Category);
                if (!_question && lines.Count + (heading ? index == first ? 1 : 2 : 0) + 1 > height - footerRows)
                {
                    break;
                }
                if (heading)
                {
                    if (index > first)
                    {
                        lines.Add((string.Empty, surface));
                    }
                    lines.Add((matches[index].Category, "bold " + appearance.Accent + " on grey7"));
                }
                var style = index == Selected ? highlight : surface;
                if (_question)
                {
                    lines.AddRange(labels[index].Select(label => (label, style)));
                    lines.Add((string.Empty, surface));
                }
                else
                {
                    var label = TranscriptMarkdown.Clean(matches[index].Title).Replace('\n', ' ');
                    var detail = TranscriptMarkdown.Clean(matches[index].Detail).Replace('\n', ' ');
                    var item = _items.FirstOrDefault(item => item.Value == matches[index].Command);
                    var marker = _items.Any(item => item.IsCurrent) ? item?.IsCurrent == true
                        ? console.Profile.Capabilities.Unicode ? "\u25cf " : "* " : "  " : string.Empty;
                    var labelWidth = Math.Min(_options is null ? inner : inner / 2,
                        matches.Max(match => TranscriptMarkdown.Clean(match.Title).GetCellWidth()) + marker.Length);
                    var right = ComposerLayout.Clip(item?.RightLabel ?? string.Empty, Math.Max(0, inner / 4));
                    var detailColumn = Math.Min(inner, labelWidth + 2);
                    var detailWidth = Math.Max(0, inner - detailColumn - (right.Length > 0 ? right.GetCellWidth() + 2 : 0));
                    var detailText = ComposerLayout.Clip(detail, detailWidth);
                    if (detailText.Length > 0)
                    {
                        descriptions[lines.Count] = (detailColumn, detailText + (right.Length > 0
                            ? new string(' ', Math.Max(1, inner - detailColumn - detailText.GetCellWidth() - right.GetCellWidth())) + right : string.Empty));
                    }
                    else if (right.Length > 0)
                    {
                        descriptions[lines.Count] = (inner - right.GetCellWidth(), right);
                    }
                    lines.Add((ComposerLayout.Clip(marker + label, detail.Length > 0 || right.Length > 0 ? labelWidth : inner), style));
                }
                lastVisible = index + 1;
            }
            if (!_question && (first > 0 || lastVisible < matches.Length))
            {
                lines.Add(($"{Selected + 1} / {matches.Length}", muted));
            }
        }
        if (footer.Length > 0 && height >= 8)
        {
            var footerStart = height - footer.Length - 1;
            while (lines.Count < footerStart)
            {
                lines.Add((string.Empty, surface));
            }
            if (lines.Count > footerStart)
            {
                lines.RemoveRange(footerStart, lines.Count - footerStart);
            }
            lines.AddRange(footer.Select(line => (line, muted)));
        }
        var paintHeight = minimumTop == 1 ? Math.Max(1, console.Profile.Height - top + 1) : height;
        for (var row = 0; row < paintHeight; row++)
        {
            var line = row < lines.Count ? lines[row] : (string.Empty, surface);
            console.Write(new ControlCode($"\u001b[{top + row};1H\u001b[2K\u001b[{top + row};{left}H"));
            var text = ComposerLayout.Clip(TranscriptMarkdown.Clean(line.Item1).Replace('\n', ' '), inner);
            console.Write(new Text(new string(' ', padding) + text + new string(' ', Math.Max(0, width - padding - text.GetCellWidth())), Style.Parse(line.Item2)));
            if (descriptions.TryGetValue(row, out var description))
            {
                console.Write(new ControlCode($"\u001b[{top + row};{left + padding + description.Column}H"));
                console.Write(new Text(description.Text, Style.Parse(line.Item2 == highlight ? highlight : muted)));
            }
        }
        console.Write(new ControlCode($"\u001b[{top + Math.Min(inputRow, height - 1)};{Math.Min(console.Profile.Width, left + padding + Math.Min(inner - 1, inputLabel.GetCellWidth() + Math.Max(0, cursorWidth - skipped)))}H"));

        int CategoryRowCount(int firstIndex, int lastIndex)
        {
            var rows = lastIndex - firstIndex + 1;
            for (var index = firstIndex; index <= lastIndex; index++)
            {
                if (matches[index].Category.Length > 0 && (index == firstIndex || matches[index].Category != matches[index - 1].Category))
                {
                    rows += index == firstIndex ? 1 : 2;
                }
            }
            return rows;
        }
    }

    private void DrawPicker(IAnsiConsole console, string theme, int minimumTop)
    {
        var width = Math.Max(1, console.Profile.Width - 4);
        var top = Math.Max(1, minimumTop);
        var height = Math.Max(1, console.Profile.Height - top + 1);
        var accent = TerminalThemes.Find(theme).Accent;
        var unicode = console.Profile.Capabilities.Unicode;
        var matches = Matches();
        var modelPicker = _title == "Select model";
        var multiSelect = _options?.MultiSelect == true;
        var rows = new List<IRenderable>
        {
            new Text(ComposerLayout.Clip(TranscriptMarkdown.Clean((_title ?? "Select").Replace("Codai / ", string.Empty, StringComparison.Ordinal)).Replace('\n', ' '), width), Style.Parse("bold " + accent)),
        };
        if (multiSelect)
        {
            var count = $"{_checked.Count}/{_items.Count}" + (width >= 36 ? " enabled" : string.Empty);
            var title = ComposerLayout.Clip(_title ?? "Skills", Math.Max(1, width - count.GetCellWidth() - 1));
            rows[0] = new Markup($"[bold {accent}]{Markup.Escape(title)}[/]" +
                new string(' ', Math.Max(1, width - title.GetCellWidth() - count.GetCellWidth())) + $"[grey62]{count}[/]");
        }
        if (modelPicker)
        {
            rows.AddRange(WrapText("Choose a model for the current provider and mode. Changes apply to this session only.", width, 2)
                .Select(line => new Text(line, Style.Parse("grey62"))));
        }
        var searchWidth = Math.Max(1, width - 4);
        var query = ComposerLayout.Clip(Query, searchWidth);
        var searchText = ComposerLayout.Clip(Query.Length == 0 ? _options?.Placeholder ?? "Search..." : query, searchWidth);
        var searchRow = rows.Count;
        var searchField = SearchField(searchText, width);
        rows.Add(new Text(searchField.Text, Style.Parse(searchField.Style)));
        rows.Add(new Text(string.Empty));
        var footerText = DecisionPanel.SelectionFooter(width, unicode);
        if (multiSelect)
        {
            footerText = footerText.Replace("cancel", "close", StringComparison.Ordinal);
        }
        var footer = WrapText(footerText, width, 2).ToArray();
        var capacity = Math.Max(1, height - rows.Count - footer.Length - 2);
        if (multiSelect)
        {
            capacity = Math.Max(1, capacity / 3);
        }
        var first = Math.Max(0, Selected - capacity + 1);
        for (var index = first; index < Math.Min(matches.Length, first + capacity) && rows.Count < height - footer.Length - 2; index++)
        {
            var item = _items.FirstOrDefault(item => item.Value == matches[index].Command && item.Label == matches[index].Title);
            if (multiSelect)
            {
                var background = index == Selected ? " on grey19" : string.Empty;
                var enabled = _checked.Contains(matches[index].Command);
                var skillMarker = enabled ? unicode ? "\u2714 " : "x " : "  ";
                var skillPrefix = (index == Selected ? unicode ? "\u276f " : "> " : "  ") + skillMarker;
                var source = ComposerLayout.Clip(item?.RightLabel ?? string.Empty, Math.Max(0, width / 3));
                var skillLabel = ComposerLayout.Clip(TranscriptMarkdown.Clean(matches[index].Title).Replace('\n', ' '),
                    Math.Max(1, width - skillPrefix.GetCellWidth() - source.GetCellWidth() - 2));
                var padding = new string(' ', Math.Max(0, width - skillPrefix.GetCellWidth() - skillLabel.GetCellWidth() - source.GetCellWidth()));
                rows.Add(new Markup($"[bold {(enabled ? accent : "default")}{background}]{Markup.Escape(skillPrefix + skillLabel + padding)}[/]" +
                    $"[grey62{background}]{Markup.Escape(source)}[/]"));
                var description = "    " + ComposerLayout.Clip(TranscriptMarkdown.Clean(matches[index].Detail).Replace('\n', ' '), Math.Max(1, width - 4));
                rows.Add(new Text(description + new string(' ', Math.Max(0, width - description.GetCellWidth())), Style.Parse("grey62" + background)));
                rows.Add(new Text(string.Empty));
                continue;
            }
            var marker = item?.IsCurrent == true ? unicode ? " ✔" : " [current]" : string.Empty;
            var prefix = (index == Selected ? unicode ? "❯ " : "> " : "  ") + (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + ". ";
            var labelWidth = width < 36 ? Math.Max(1, width - prefix.GetCellWidth()) : Math.Min(28, Math.Max(8, width / 3));
            var label = ComposerLayout.Clip(TranscriptMarkdown.Clean(matches[index].Title).Replace('\n', ' '), Math.Max(1, labelWidth - marker.GetCellWidth())) + marker;
            var detail = TranscriptMarkdown.Clean((item?.RightLabel is { Length: > 0 } right ? right + " " : string.Empty) + matches[index].Detail).Replace('\n', ' ');
            var detailWidth = Math.Max(0, width - prefix.GetCellWidth() - labelWidth - 2);
            var selectionStyle = index == Selected ? accent : "default";
            rows.Add(new Markup($"[{selectionStyle}]{Markup.Escape(prefix + label)}[/]" +
                (detailWidth > 0 ? new string(' ', Math.Max(1, labelWidth - label.GetCellWidth() + 2)) + $"[grey62]{Markup.Escape(ComposerLayout.Clip(detail, detailWidth))}[/]" : string.Empty)));
        }
        if (matches.Length == 0)
        {
            rows.Add(new Text(_options?.EmptyText ?? (modelPicker ? "No matching models" : "No matching items"), Style.Parse("grey62")));
        }
        rows.Add(new Text(string.Empty));
        rows.AddRange(footer.Select(line => new Text(line, Style.Parse("grey62"))));
        for (var row = 0; row < height; row++)
        {
            console.Write(new ControlCode($"\u001b[{top + row};1H\u001b[2K\u001b[{top + row};3H"));
            if (row < rows.Count)
            {
                console.Write(rows[row]);
            }
        }
        var cursorRow = Query.Length > 0 ? searchRow : rows.Count;
        console.Write(new ControlCode($"\u001b[{top + Math.Min(height - 1, cursorRow)};{Math.Min(console.Profile.Width, Query.Length > 0 ? 5 + query.GetCellWidth() : 3)}H"));
    }

    private void DrawSettings(IAnsiConsole console, string theme, int minimumTop)
    {
        var width = Math.Max(1, console.Profile.Width - 4);
        var top = Math.Max(1, minimumTop);
        var height = Math.Max(1, console.Profile.Height - top + (minimumTop == 1 ? 1 : -2));
        var accent = TerminalThemes.Find(theme).Accent;
        var unicode = console.Profile.Capabilities.Unicode;
        var matches = Matches();
        var footer = new[] { DecisionPanel.SelectionFooter(width, unicode) };
        var searchWidth = Math.Max(1, width - 4);
        var query = ComposerLayout.Clip(Query, searchWidth);
        var searchText = query.Length == 0 ? "Search settings..." : query;
        var rows = new List<(string Text, string Style)>
        {
            (string.Empty, "default"),
        };
        if (height >= 18)
        {
            rows.Add((string.Empty, "default"));
            rows.Add((string.Empty, "default"));
        }
        var searchRow = rows.Count;
        rows.Add(SearchField(ComposerLayout.Clip(searchText, searchWidth), width));
        rows.Add((string.Empty, "default"));
        var capacity = Math.Max(1, height - rows.Count - footer.Length - 2);
        var first = Math.Max(0, Selected - capacity + 1);
        var firstItemRow = rows.Count;
        var labelWidth = width < 50 && _title is null ? width - 2 : Math.Min(46, Math.Max(8, width * 2 / 5));
        for (var index = first; index < Math.Min(matches.Length, first + capacity); index++)
        {
            var label = ComposerLayout.Clip(matches[index].Title, Math.Max(1, labelWidth - 2));
            var value = _items.FirstOrDefault(item => item.Value == matches[index].Command && item.Label == matches[index].Title)?.RightLabel;
            var detail = width < 50 && value is null ? string.Empty : ComposerLayout.Clip(value ?? matches[index].Detail, Math.Max(0, width - labelWidth - 3));
            rows.Add(((index == Selected ? unicode ? "❯ " : "> " : "  ") + label +
                new string(' ', Math.Max(1, labelWidth - label.GetCellWidth())) + detail,
                index == Selected && (!_searchingSettings || Query.Length > 0) ? accent : "default"));
        }
        if (matches.Length == 0)
        {
            rows.Add(("  No matching settings", "grey62"));
        }
        var more = Math.Max(0, matches.Length - first - capacity);
        if (more > 0 || first > 0)
        {
            rows.Add(("  " + (first > 0 ? $"{(unicode ? "↑" : "^")} {first} above  " : string.Empty) +
                (more > 0 ? $"{(unicode ? "↓" : "v")} {more} more" : string.Empty), "grey62"));
        }
        rows.Add((string.Empty, "default"));
        rows.AddRange(footer.Select(line => (line, "grey62")));
        for (var row = 0; row < height; row++)
        {
            console.Write(new ControlCode($"\u001b[{top + row};1H\u001b[2K\u001b[{top + row};3H"));
            if (row == 0)
            {
                console.Write(new Text("Settings", Style.Parse("bold " + accent)));
                continue;
            }
            var line = row < rows.Count ? rows[row] : (string.Empty, "default");
            console.Write(new Text(ComposerLayout.Clip(line.Item1, width), Style.Parse(line.Item2)));
        }
        var cursorRow = _searchingSettings ? searchRow : firstItemRow + Selected - first;
        console.Write(new ControlCode($"\u001b[{top + Math.Min(cursorRow, height - 1)};{Math.Min(console.Profile.Width, _searchingSettings ? 5 + query.GetCellWidth() : 3)}H"));
    }

    private static (string Text, string Style) SearchField(string text, int width)
    {
        var label = ComposerLayout.Clip("  " + text, width);
        return (label + new string(' ', Math.Max(0, width - label.GetCellWidth())), "grey62 on grey7");
    }

    private void DrawHorizontal(IAnsiConsole console, string theme, int minimumTop)
    {
        var top = Math.Max(1, minimumTop);
        var height = Math.Max(1, console.Profile.Height - top + 1);
        var inner = Math.Max(1, console.Profile.Width - 4);
        var accent = TerminalThemes.Find(theme).Accent;
        var label = _options!.Placeholder;
        var value = _actions.Length > 0 ? _actions[Selected].Title : string.Empty;
        var combined = label.GetCellWidth() + value.GetCellWidth() + 1 <= inner;
        var labelLines = WrapText(label, inner, int.MaxValue);
        var footer = inner >= 48 ? "Left/Right to adjust - Enter to apply - Esc to cancel" : "Left/Right - Enter apply - Esc cancel";
        var footerLines = WrapText(footer, inner, int.MaxValue);
        List<(string Text, string Style)> lines =
        [
            (new string(console.Profile.Capabilities.Unicode ? '\u2500' : '-', inner), accent),
            (_title ?? string.Empty, "bold " + accent),
        ];
        var body = TranscriptMarkdown.Clean(_options.Description).Replace("\r", string.Empty, StringComparison.Ordinal)
            .Split('\n').SelectMany(line => WrapText(line, inner, int.MaxValue));
        var bodyBudget = Math.Max(0, height - lines.Count - footerLines.Length - (combined ? 4 : labelLines.Length + 4));
        lines.AddRange(body.Take(bodyBudget).Select(line => (line, "grey62")));
        lines.Add((string.Empty, "default"));
        var selectionRow = lines.Count;
        if (combined)
        {
            lines.Add((label + " " + value, "default"));
        }
        else
        {
            lines.AddRange(labelLines.Select(line => (line, "default")));
            lines.Add((value, "bold " + accent));
        }
        lines.Add((string.Empty, "default"));
        lines.AddRange(footerLines.Select(line => (line, "grey62")));
        for (var row = 0; row < height; row++)
        {
            if (console.Profile.Capabilities.Ansi)
            {
                console.Write(new ControlCode($"\u001b[{top + row};1H\u001b[2K\u001b[{top + row};3H"));
            }
            if (combined && row == selectionRow)
            {
                console.Write(new Text(label + " "));
                console.Write(new Text(value, Style.Parse("bold " + accent)));
            }
            else if (row < lines.Count)
            {
                console.Write(new Text(ComposerLayout.Clip(lines[row].Text, inner), Style.Parse(lines[row].Style)));
            }
            if (!console.Profile.Capabilities.Ansi)
            {
                console.WriteLine();
            }
        }
    }

    private (string Title, string Detail, string Command, string Category)[] Matches() => _actions.Where(action =>
        (action.Title + " " + action.Detail + " " + action.Category + (_title is null ? " " + action.Command : string.Empty))
            .Contains(Query, StringComparison.OrdinalIgnoreCase)).ToArray();

    internal static string[] WrapText(string text, int width, int maximumRows)
    {
        var lines = new List<string>();
        var current = string.Empty;
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current.Length > 0 && current.GetCellWidth() + word.GetCellWidth() + 1 <= width)
            {
                current += " " + word;
                continue;
            }
            if (current.Length > 0)
            {
                lines.Add(current);
            }
            var pieces = ComposerLayout.Create(word, 0, width, int.MaxValue).Lines;
            lines.AddRange(pieces.Take(Math.Max(0, pieces.Count - 1)));
            current = pieces.Count > 0 ? pieces[^1] : string.Empty;
        }
        if (current.Length > 0 || lines.Count == 0)
        {
            lines.Add(current);
        }
        return lines.Take(maximumRows).ToArray();
    }
}
