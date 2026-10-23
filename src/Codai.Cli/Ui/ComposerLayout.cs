using System.Globalization;
using System.Text;
using Spectre.Console;

namespace Codai.Cli.Ui;

public sealed record ComposerLayout(IReadOnlyList<string> Lines, int CursorRow, int CursorColumn)
{
    public static ComposerLayout Create(string text, int cursor, int width, int maximumRows = 3)
    {
        width = Math.Max(1, width);
        maximumRows = Math.Max(1, maximumRows);
        cursor = Math.Clamp(cursor, 0, text.Length);
        List<string> lines = [];
        StringBuilder line = new();
        var column = 0;
        var cursorRow = 0;
        var cursorColumn = 0;
        var elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext())
        {
            var element = elements.GetTextElement();
            var cells = element.GetCellWidth();
            if (element != "\n" && column + cells > width)
            {
                lines.Add(line.ToString());
                line.Clear();
                column = 0;
            }
            if (elements.ElementIndex <= cursor)
            {
                cursorRow = lines.Count;
                cursorColumn = column;
            }
            if (element == "\n")
            {
                lines.Add(line.ToString());
                line.Clear();
                column = 0;
            }
            else if (!element.Any(char.IsControl))
            {
                if (cells <= width)
                {
                    line.Append(element);
                    column += cells;
                }
            }
        }
        if (cursor == text.Length)
        {
            if (column == width)
            {
                lines.Add(line.ToString());
                line.Clear();
                column = 0;
            }
            cursorRow = lines.Count;
            cursorColumn = column;
        }
        lines.Add(line.ToString());
        var first = Math.Max(0, cursorRow - maximumRows + 1);
        return new ComposerLayout(lines.Skip(first).Take(maximumRows).ToArray(), cursorRow - first, cursorColumn);
    }

    public static string Clip(string text, int width)
    {
        StringBuilder result = new();
        var cells = 0;
        var elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext())
        {
            var element = elements.GetTextElement();
            if (element.Any(char.IsControl))
            {
                continue;
            }
            var length = element.GetCellWidth();
            if (cells + length > width)
            {
                break;
            }
            result.Append(element);
            cells += length;
        }
        return result.ToString();
    }
}
