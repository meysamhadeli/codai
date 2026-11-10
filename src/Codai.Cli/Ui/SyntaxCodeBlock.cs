using System.Globalization;
using ColorCode;
using ColorCode.Parsing;
using ColorCode.Styling;
using Spectre.Console;
using Spectre.Console.Rendering;
using TerminalStyle = Spectre.Console.Style;

namespace Codai.Cli.Ui;

internal sealed class SyntaxCodeBlock : Renderable
{
    private static readonly TerminalStyle _surface = TerminalStyle.Parse("white on grey7");
    private static readonly TerminalStyle _gutter = TerminalStyle.Parse("grey on grey7");
    private static readonly TerminalStyle _rail = TerminalStyle.Parse("grey23 on grey7");
    private readonly string _source;
    private readonly List<(string Text, TerminalStyle Style)> _elements;

    public SyntaxCodeBlock(string content, string language)
    {
        _source = TranscriptMarkdown.Clean(content).TrimEnd('\n');
        _elements = new SourceFormatter().Format(_source, language);
    }

    protected override Measurement Measure(RenderOptions options, int maxWidth) => new(1, Math.Max(1, maxWidth));

    protected override IEnumerable<Segment> Render(RenderOptions options, int maxWidth)
    {
        var width = Math.Max(1, maxWidth);
        var digits = (_source.Count(character => character == '\n') + 1).ToString(CultureInfo.InvariantCulture).Length;
        var gutterWidth = width >= digits + 8 ? digits + 4 : 0;
        var contentWidth = Math.Max(1, width - gutterWidth - 1);
        var lineNumber = 1;
        var column = gutterWidth;

        yield return new Segment(new string(' ', width), _surface);
        yield return Segment.LineBreak;
        foreach (var segment in Gutter(lineNumber))
        {
            yield return segment;
        }
        foreach (var (text, style) in _elements)
        {
            if (text == "\n")
            {
                yield return new Segment(new string(' ', Math.Max(0, width - column)), _surface);
                yield return Segment.LineBreak;
                foreach (var segment in Gutter(++lineNumber))
                {
                    yield return segment;
                }
                column = gutterWidth;
                continue;
            }
            var cells = text.GetCellWidth();
            if (column > gutterWidth && column - gutterWidth + cells > contentWidth)
            {
                yield return new Segment(new string(' ', Math.Max(0, width - column)), _surface);
                yield return Segment.LineBreak;
                foreach (var segment in Gutter(null))
                {
                    yield return segment;
                }
                column = gutterWidth;
            }
            yield return new Segment(text, style);
            column += cells;
        }
        yield return new Segment(new string(' ', Math.Max(0, width - column)), _surface);
        yield return Segment.LineBreak;
        yield return new Segment(new string(' ', width), _surface);

        IEnumerable<Segment> Gutter(int? number)
        {
            if (gutterWidth == 0)
            {
                yield break;
            }
            yield return new Segment("│", _rail);
            yield return new Segment(" " + (number?.ToString(CultureInfo.InvariantCulture) ?? string.Empty).PadLeft(digits) + "  ", _gutter);
        }
    }

    private sealed class SourceFormatter() : CodeColorizerBase(StyleDictionary.DefaultDark, null)
    {
        private readonly List<(string Text, TerminalStyle Style)> _output = [];
        private readonly Dictionary<string, TerminalStyle> _styles = new(StringComparer.Ordinal);

        public List<(string Text, TerminalStyle Style)> Format(string source, string language)
        {
            var id = language.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToLowerInvariant() ?? string.Empty;
            id = id switch
            {
                "cs" or "csharp" => "c#",
                "js" => "javascript",
                "ts" => "typescript",
                "py" => "python",
                "ps1" or "pwsh" => "powershell",
                _ => id,
            };
            var grammar = id.Length == 0 ? null : Languages.FindById(id);
            if (grammar is null)
            {
                Write(source, []);
            }
            else
            {
                try
                {
                    languageParser.Parse(source, grammar, Write);
                }
                catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.Text.RegularExpressions.RegexMatchTimeoutException)
                {
                    _output.Clear();
                    Write(source, []);
                }
            }
            return _output;
        }

        protected override void Write(string parsedSourceCode, IList<Scope> scopes)
        {
            var colors = new TerminalStyle?[parsedSourceCode.Length];
            Apply(scopes);
            var elements = StringInfo.GetTextElementEnumerator(parsedSourceCode);
            while (elements.MoveNext())
            {
                _output.Add((elements.GetTextElement(), colors[elements.ElementIndex] ?? _surface));
            }

            void Apply(IList<Scope> items)
            {
                foreach (var scope in items)
                {
                    if (Styles.Contains(scope.Name) && Styles[scope.Name].Foreground is { Length: > 0 } foreground)
                    {
                        if (!_styles.TryGetValue(scope.Name, out var style))
                        {
                            var color = foreground.Length == 9 ? "#" + foreground[3..] : foreground;
                            style = TerminalStyle.Parse(color + " on grey7");
                            _styles.Add(scope.Name, style);
                        }
                        Array.Fill(colors, style, scope.Index, scope.Length);
                    }
                    Apply(scope.Children);
                }
            }
        }
    }
}