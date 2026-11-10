using Spectre.Console;

namespace Codai.Cli.Ui;

internal static class PromptBlock
{
    public const int ContentOffset = 2;

    public static Text Border(int width, bool unicode) =>
        new(new string(unicode ? '\u2500' : '-', Math.Max(1, width)), Style.Parse("grey50"));

    public static int ContentWidth(int width, bool isInput = true) => Math.Max(1, width - (isInput ? ContentOffset : 2) - 1);

    public static Markup SuggestionRow(string command, int width, string accent, bool selected, bool unicode = true)
    {
        width = Math.Max(4, width);
        var nameWidth = Math.Min(Math.Min(28, Math.Max(12, width / 3)), width - 4);
        var name = ComposerLayout.Clip(command, nameWidth);
        var gap = Math.Min(2, Math.Max(0, width - 4 - nameWidth));
        var descriptionWidth = Math.Max(0, width - 4 - nameWidth - gap);
        var description = ComposerLayout.Clip(ComposerCompletion.Describe(command), descriptionWidth);
        var background = selected ? "grey11" : "default";
        var rail = unicode ? "\u258d" : "|";
        return new Markup($"[{accent} on {background}]{rail}[/]" +
            $"[{(selected ? accent : "white")} on {background}] {(selected ? ">" : " ")} {Markup.Escape(name)}{new string(' ', nameWidth - name.GetCellWidth() + gap)}[/]" +
            $"[{(selected ? accent : "grey62")} on {background}]{Markup.Escape(description)}{new string(' ', descriptionWidth - description.GetCellWidth())}[/]");
    }

    public static Markup Row(string text, int width, string accent, bool unicode = true, string foreground = "white", bool isInput = false, bool showPrompt = true, string trailing = "")
    {
        width = Math.Max(1, width);
        var offset = Math.Min(isInput ? ContentOffset : 2, width);
        var metadata = ComposerLayout.Clip(trailing, Math.Max(0, width - offset - 3));
        var reserved = metadata.Length > 0 ? metadata.GetCellWidth() + 2 : 0;
        var content = ComposerLayout.Clip(text, Math.Max(0, width - offset - 1 - reserved));
        var padding = new string(' ', Math.Max(0, width - offset - content.GetCellWidth() - metadata.GetCellWidth()));
        var background = isInput ? "default" : "grey7";
        var marker = showPrompt ? unicode ? "\u276f" : ">" : " ";
        var markerColor = accent;
        if (!isInput)
        {
            var color = Style.Parse(accent).Foreground;
            markerColor = $"#{color.R * 3 / 4:X2}{color.G * 3 / 4:X2}{color.B * 3 / 4:X2}";
        }
        return new Markup($"[{markerColor} on {background}]{marker}[/]" +
            $"[{foreground} on {background}]{new string(' ', offset - 1)}{Markup.Escape(content)}{padding}[/]" +
            $"[grey62 on {background}]{Markup.Escape(metadata)}[/]");
    }
}
