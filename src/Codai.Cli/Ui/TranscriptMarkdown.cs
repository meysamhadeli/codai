using System.Globalization;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Codai.Cli.Ui;

public static class TranscriptMarkdown
{
    private static readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder().UsePipeTables().Build();

    public static IRenderable Render(string markdown) => new Rows(Blocks(Markdown.Parse(Clean(markdown), _pipeline)));

    public static string Clean(string text) => new(text.Replace("\t", "    ", StringComparison.Ordinal)
        .Where(character => !char.IsControl(character) || character == '\n').ToArray());

    public static IRenderable Code(string content, string language = "", bool numbered = true, int? width = null)
    {
        List<IRenderable> rows = [];
        if (!string.IsNullOrWhiteSpace(language))
        {
            rows.Add(new Text(Clean(language), Style.Parse("grey")));
        }
        var lines = Clean(content).TrimEnd('\n').Split('\n');
        var digits = lines.Length.ToString(CultureInfo.InvariantCulture).Length;
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var prefix = numbered ? (index + 1).ToString(CultureInfo.InvariantCulture).PadLeft(digits) + "  " : "  ";
            var style = line.StartsWith('+') ? "green on #153d24"
                : line.StartsWith('-') ? "red on #482022" : "silver on grey11";
            var display = prefix + line;
            if (width is { } columns)
            {
                display += new string(' ', Math.Max(0, columns - display.GetCellWidth()));
            }
            rows.Add(new Text(display, Style.Parse(style)));
        }
        return new Rows(rows);
    }

    private static IEnumerable<IRenderable> Blocks(ContainerBlock container)
    {
        foreach (var block in container)
        {
            switch (block)
            {
                case Markdig.Extensions.Tables.Table table:
                    var display = new Spectre.Console.Table().Border(TableBorder.Square).Expand();
                    var tableRows = table.OfType<Markdig.Extensions.Tables.TableRow>().ToArray();
                    if (tableRows.Length == 0)
                    {
                        break;
                    }
                    foreach (var cell in tableRows[0].OfType<Markdig.Extensions.Tables.TableCell>())
                    {
                        display.AddColumn(new TableColumn(new Markup("[bold deepskyblue1]" + Cell(cell) + "[/]")));
                    }
                    foreach (var row in tableRows.Skip(1))
                    {
                        display.AddRow(row.OfType<Markdig.Extensions.Tables.TableCell>().Select(cell => (IRenderable)new Markup(Cell(cell))).ToArray());
                    }
                    yield return display;
                    break;
                case HeadingBlock heading:
                    yield return new Markup("[bold deepskyblue1]" + Inline(heading.Inline) + "[/]");
                    yield return new Text(" ");
                    break;
                case CodeBlock code:
                    yield return Code(code.Lines.ToString(), code is FencedCodeBlock fenced ? fenced.Info ?? string.Empty : string.Empty);
                    yield return new Text(" ");
                    break;
                case ListBlock list:
                    var itemNumber = int.TryParse(list.OrderedStart, CultureInfo.InvariantCulture, out var start) ? start : 1;
                    foreach (var item in list.OfType<ListItemBlock>())
                    {
                        var label = list.IsOrdered ? (itemNumber++).ToString(CultureInfo.InvariantCulture) + ". " : "- ";
                        var itemRows = Blocks(item).ToArray();
                        var grid = new Grid().AddColumn(new GridColumn().Width(label.Length)).AddColumn();
                        grid.AddRow(new Text(label, Style.Parse("deepskyblue1")), new Rows(itemRows));
                        yield return grid;
                    }
                    break;
                case QuoteBlock quote:
                    yield return new Padder(new Rows(Blocks(quote)), new Padding(2, 0, 0, 0));
                    break;
                case ParagraphBlock paragraph:
                    yield return new Markup(Inline(paragraph.Inline));
                    yield return new Text(" ");
                    break;
                case ThematicBreakBlock:
                    yield return new Rule().RuleStyle("grey");
                    break;
                case LeafBlock leaf:
                    yield return new Text(leaf.Lines.ToString());
                    break;
                case ContainerBlock nested:
                    foreach (var child in Blocks(nested))
                    {
                        yield return child;
                    }
                    break;
            }
        }
    }

    private static string Cell(Markdig.Extensions.Tables.TableCell cell) =>
        string.Join(" ", cell.OfType<ParagraphBlock>().Select(paragraph => Inline(paragraph.Inline)));

    private static string Inline(ContainerInline? container)
    {
        if (container is null)
        {
            return string.Empty;
        }
        return string.Concat(container.Select(inline => inline switch
        {
            LiteralInline literal => Markup.Escape(literal.Content.ToString()),
            CodeInline code => "[green]" + Markup.Escape(code.Content) + "[/]",
            EmphasisInline emphasis => "[" + (emphasis.DelimiterCount >= 2 ? "bold" : "italic") + "]" + Inline(emphasis) + "[/]",
            LinkInline link => "[deepskyblue1]" + Inline(link) + "[/]",
            AutolinkInline link => "[deepskyblue1]" + Markup.Escape(link.Url) + "[/]",
            LineBreakInline => "\n",
            HtmlInline html => Markup.Escape(html.Tag),
            ContainerInline nested => Inline(nested),
            _ => Markup.Escape(inline.ToString() ?? string.Empty),
        }));
    }
}
