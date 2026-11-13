using Codai.Core;
using Codai.Core.Abstractions;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Codai.Cli.Ui;

public sealed class PluginBrowser(IReadOnlyList<SessionMenuItem> items, string tab = "Discover") : IOverlay
{
    private static readonly string[] Tabs = ["Discover", "Installed", "Marketplaces"];
    private int _selected;
    private int _marketplace;
    public string Query { get; private set; } = string.Empty;
    public bool Closed { get; private set; }
    public string? Command { get; private set; }

    private string[] Marketplaces => ["All", .. items.Select(item => item.Group).Where(group => group.Length > 0).Distinct(StringComparer.Ordinal)];

    private SessionMenuItem[] Matches() => [.. items.Where(item =>
        (_marketplace == 0 || item.Group == Marketplaces[_marketplace]) &&
        (item.Label.Contains(Query, StringComparison.OrdinalIgnoreCase) ||
         item.Description.Contains(Query, StringComparison.OrdinalIgnoreCase) ||
         item.Group.Contains(Query, StringComparison.OrdinalIgnoreCase)))];

    public void HandleKey(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Escape)
        {
            Closed = true;
            return;
        }
        if (key.Key is ConsoleKey.Tab or ConsoleKey.LeftArrow or ConsoleKey.RightArrow)
        {
            var direction = key.Key == ConsoleKey.LeftArrow || key.Modifiers.HasFlag(ConsoleModifiers.Shift) ? -1 : 1;
            Command = "@plugins:tab:" + Tabs[(Math.Max(0, Array.IndexOf(Tabs, tab)) + direction + Tabs.Length) % Tabs.Length];
            Closed = true;
            return;
        }
        if (key.Key == ConsoleKey.S && key.Modifiers.HasFlag(ConsoleModifiers.Control))
        {
            _marketplace = (_marketplace + 1) % Marketplaces.Length;
            _selected = 0;
            return;
        }
        var matches = Matches();
        if (key.Key is ConsoleKey.UpArrow or ConsoleKey.DownArrow or ConsoleKey.Home or ConsoleKey.End or ConsoleKey.PageUp or ConsoleKey.PageDown)
        {
            _selected = Math.Clamp(key.Key switch
            {
                ConsoleKey.Home => 0,
                ConsoleKey.End => matches.Length - 1,
                ConsoleKey.PageUp => _selected - 5,
                ConsoleKey.PageDown => _selected + 5,
                ConsoleKey.UpArrow => _selected - 1,
                _ => _selected + 1,
            }, 0, Math.Max(0, matches.Length - 1));
        }
        else if (key.Key == ConsoleKey.Enter)
        {
            if (matches.Length > 0)
            {
            Command = (tab == "Discover" ? "@plugins:install:" : "@plugins:view:") + matches[_selected].Value;
                Closed = true;
            }
        }
        else if (key.Key == ConsoleKey.Backspace && Query.Length > 0)
        {
            Query = Query[..(Query.Length - (char.IsLowSurrogate(Query[^1]) && Query.Length > 1 ? 2 : 1))];
            _selected = 0;
        }
        else if (!char.IsControl(key.KeyChar) && Query.Length < 256)
        {
            Query += key.KeyChar;
            _selected = 0;
        }
    }

    public void Draw(IAnsiConsole console, string theme = "graphite", int minimumTop = 1)
    {
        var width = Math.Max(1, console.Profile.Width - 4);
        var top = Math.Max(1, minimumTop);
        var height = Math.Max(1, console.Profile.Height - top + 1);
        var unicode = console.Profile.Capabilities.Unicode;
        var accent = TerminalThemes.Find(theme).Accent;
        var matches = Matches();
        var count = $"({(matches.Length == 0 ? 0 : _selected + 1)}/{matches.Length})";
        var search = SettingsPalette.SearchField(ComposerLayout.Clip(Query.Length == 0 ? "Search plugins..." : Query, Math.Max(1, width - 4)), width);
        var rows = new List<IRenderable>
        {
            new Markup($"[bold {accent}]Plugins[/]" + new string(' ', Math.Max(1, width - 7 - count.GetCellWidth())) + $"[grey62]{Markup.Escape(count)}[/]"),
            new Text(search.Text, Style.Parse(search.Style)),
            new Text(string.Empty),
        };
        var groups = Marketplaces;
        var filters = groups.Select((group, index) => $"{group} {(index == 0 ? items.Count : items.Count(item => item.Group == group))}").ToArray();
        var filterText = string.Join("  ·  ", filters) + (groups.Length > 1 ? "  (ctrl+s to filter by marketplace)" : string.Empty);
        var compactFilters = filterText.GetCellWidth() > width && groups.Length > 1;
        var activeFilter = filters[_marketplace];
        if (compactFilters)
        {
            var filterCount = (_marketplace == 0 ? items.Count : items.Count(item => item.Group == groups[_marketplace])).ToString(System.Globalization.CultureInfo.InvariantCulture);
            activeFilter = ComposerLayout.Clip(groups[_marketplace], Math.Max(1, width - filterCount.Length - 1)) + " " + filterCount;
            filterText = activeFilter + $"  ·  {groups.Length - 1} marketplaces (ctrl+s to filter)";
        }
        rows.Add(new Text(ComposerLayout.Clip(filterText, width), Style.Parse("grey62")));
        rows.Add(new Text(string.Empty));
        var footer = SettingsPalette.WrapText(tab == "Discover" ? "Tab to switch sections · Enter to install · Esc to close" : "Tab to switch sections · Enter to confirm · Esc to close", width, 2);
        var capacity = Math.Max(1, (height - rows.Count - footer.Length - 2) / 3);
        var first = Math.Max(0, _selected - capacity + 1);
        foreach (var index in Enumerable.Range(first, Math.Min(capacity, Math.Max(0, matches.Length - first))))
        {
            var item = matches[index];
            var prefix = (index == _selected ? unicode ? "\u276f " : "> " : "  ") +
                (item.IsCurrent ? unicode ? "\u2714 " : "x " : "  ") +
                TerminalIcons.Prefix(TerminalIcon.Plugin, unicode);
            var background = index == _selected ? " on grey19" : string.Empty;
            var source = ComposerLayout.Clip(item.Group.Length > 0 ? item.Group : item.RightLabel, Math.Max(0, width / 3));
            var label = ComposerLayout.Clip(TranscriptMarkdown.Clean(item.Label).Replace('\n', ' '), Math.Max(1, width - prefix.GetCellWidth() - source.GetCellWidth() - 2));
            var padding = new string(' ', Math.Max(0, width - prefix.GetCellWidth() - label.GetCellWidth() - source.GetCellWidth()));
            rows.Add(new Markup(TerminalIcons.White($"[bold {(index == _selected || item.IsCurrent ? accent : "default")}{background}]{Markup.Escape(prefix + label + padding)}[/]" +
                $"[grey62{background}]{Markup.Escape(source)}[/]")));
            var description = TranscriptMarkdown.Clean(item.Description).Replace('\n', ' ');
            var inset = prefix.GetCellWidth();
            description = new string(' ', inset) + ComposerLayout.Clip(description, Math.Max(0, width - inset));
            rows.Add(new Text(description + new string(' ', Math.Max(0, width - description.GetCellWidth())), Style.Parse("grey62" + background)));
            rows.Add(new Text(string.Empty));
        }
        if (matches.Length == 0)
        {
            rows.Add(new Text("No matching entries.", Style.Parse("grey62")));
        }
        if (first + capacity < matches.Length)
            rows.Add(new Text((unicode ? "\u2193" : "v") + " more below", Style.Parse("grey62")));
        rows.Add(new Text(string.Empty));
        rows.AddRange(footer.Select(line => new Text(line, Style.Parse("grey62"))));
        for (var row = 0; row < height; row++)
        {
            console.Write(new ControlCode($"\u001b[{top + row};1H\u001b[2K\u001b[{top + row};3H"));
            if (row < rows.Count) console.Write(rows[row]);
        }
        if (height > 1)
        {
            var column = 11;
            foreach (var label in width >= 65 ? Tabs : [tab])
            {
                var text = ComposerLayout.Clip(" " + label + " ", Math.Max(0, width - column - count.GetCellWidth()));
                if (text.Length == 0)
                {
                    break;
                }
                console.Write(new ControlCode($"\u001b[{top};{column}H"));
                console.Write(new Text(text, Style.Parse(label == tab ? "bold " + accent : "grey62")));
                column += text.Length + 1;
            }
        }
        if (height > 3)
        {
            var offset = compactFilters ? 0 : filters.Take(_marketplace).Sum(filter => filter.GetCellWidth() + 5);
            if (offset < width)
            {
                console.Write(new ControlCode($"\u001b[{top + 3};{3 + offset}H"));
                console.Write(new Text(ComposerLayout.Clip(activeFilter, width - offset), Style.Parse("bold " + accent)));
            }
        }
    }
}