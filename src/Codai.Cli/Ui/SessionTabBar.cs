using Codai.Core.Abstractions;
using Spectre.Console;

namespace Codai.Cli.Ui;

internal static class SessionTabBar
{
    internal sealed record Segment(string Text, string? Command, bool Active = false);

    internal static IReadOnlyList<Segment> Layout(int width, IReadOnlyList<SessionTab> tabs, string activeId)
    {
        if (width < 12 || tabs.Count == 0)
        {
            return [];
        }
        var active = Math.Max(0, tabs.ToList().FindIndex(tab => tab.Id == activeId));
        var count = Math.Min(tabs.Count, Math.Max(1, (width - 7) / 18));
        var first = Math.Clamp(active - count / 2, 0, tabs.Count - count);
        var tabWidth = Math.Min(32, (width - 7) / count);
        List<Segment> result = [new(first > 0 ? "< " : "  ", first > 0 ? "/session previous" : null)];
        for (var index = first; index < first + count; index++)
        {
            var tab = tabs[index];
            var selected = tab.Id == activeId;
            var title = string.Join(' ', TranscriptMarkdown.Clean(tab.Title).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            var labelWidth = tabWidth - (selected ? 4 : 1);
            var label = ComposerLayout.Clip(title, labelWidth);
            result.Add(new Segment(" " + label + new string(' ', labelWidth - label.GetCellWidth()), "/session tab:" + tab.Id, selected));
            if (selected)
            {
                result.Add(new Segment(" x ", "/session close", true));
            }
        }
        result.Add(new Segment(first + count < tabs.Count ? " >" : "  ", first + count < tabs.Count ? "/session next" : null));
        result.Add(new Segment(" + ", "/session open"));
        return result;
    }

    internal static Markup Render(Segment segment, bool hovered = false)
    {
        var style = segment.Active ? "bold white on grey19" : hovered ? "white" : "grey62";
        var text = Markup.Escape(segment.Text);
        return new Markup($"[{style}]{text}[/]");
    }

    internal static string? HitTest(int column, IReadOnlyList<Segment> segments)
    {
        var offset = 1;
        foreach (var segment in segments)
        {
            var end = offset + segment.Text.GetCellWidth();
            if (column >= offset && column < end)
            {
                return segment.Command;
            }
            offset = end;
        }
        return null;
    }
}