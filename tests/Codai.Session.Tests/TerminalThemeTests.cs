using Codai.Cli.Ui;
using Codai.Core;
using FluentAssertions;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Codai.Session.Tests;

public class TerminalThemeTests
{
    [Fact]
    public void Theme_palette_contains_the_expected_themes()
    {
        TerminalThemes.All.Select(theme => theme.Id).Should().Equal(
            "graphite", "ocean", "azure", "violet", "magenta", "forest", "mint", "ember", "sunset", "coral");
    }

    [Fact]
    public void Theme_ids_are_unique_and_selectable()
    {
        TerminalThemes.All.Select(theme => theme.Id).Should().OnlyHaveUniqueItems();
        TerminalThemes.All.Should().OnlyContain(theme => TerminalThemes.IsKnown(theme.Id));
    }

    [Fact]
    public void Every_theme_accent_and_highlight_parses_as_a_spectre_style()
    {
        foreach (var theme in TerminalThemes.All)
        {
            var accent = Style.Parse(theme.Accent);
            accent.Should().NotBeNull();
            var highlight = Style.Parse(theme.Highlight);
            highlight.Should().NotBeNull();
        }
    }

    [Fact]
    public void Markdown_headings_and_links_render_in_the_theme_accent()
    {
        foreach (var theme in TerminalThemes.All)
        {
            var accentCode = AccentCode(theme.Accent);
            var rendered = RenderAnsi(TranscriptMarkdown.Render("## Heading\n\n[link](https://example.com)", theme.Accent));
            rendered.Should().Contain(accentCode, "the heading and link accent should match the theme");
        }
    }

    [Fact]
    public void Inline_code_and_diff_lines_render_in_the_theme_accent()
    {
        var accentCode = AccentCode("darkorange");
        var inline = RenderAnsi(TranscriptMarkdown.Render("Use `dotnet run` now.", "darkorange"));
        inline.Should().Contain(accentCode).And.NotContain(AccentCode("green"));

        var diff = RenderAnsi(TranscriptMarkdown.Code("+added\n-removed\nplain", accent: "darkorange"));
        diff.Should().Contain(AccentCode("darkorange on grey11"));
        diff.Should().NotContain(AccentCode("green")).And.NotContain(AccentCode("red"));
    }

    [Fact]
    public void Sunset_uses_the_warm_orange_accent()
    {
        var sunset = TerminalThemes.Find("sunset");
        sunset.Accent.Should().Be("#D97757");
        RenderAnsi(TranscriptMarkdown.Render("## Heading", sunset.Accent)).Should().Contain("38;2;217;119;87");
    }

    [Fact]
    public void Transcript_step_headers_render_in_the_theme_accent()
    {
        var transcript = new ChatTranscript { Accent = "purple" };
        transcript.StartTool("grep", "pattern");
        transcript.CompleteTool("grep", true, "finished");
        string.Join('\n', transcript.GetLines(80)).Should().Contain(AccentCode("bold purple"));
    }

    [Fact]
    public void Invalidating_the_transcript_reapplies_a_changed_accent()
    {
        var transcript = new ChatTranscript { Accent = "purple" };
        transcript.StartTool("grep", "pattern");
        transcript.CompleteTool("grep", true, "finished");
        string.Join('\n', transcript.GetLines(80)).Should().Contain(AccentCode("bold purple"));

        transcript.Accent = "darkorange";
        transcript.Invalidate();
        string.Join('\n', transcript.GetLines(80)).Should().Contain(AccentCode("bold darkorange"));
    }

    [Theory]
    [InlineData("GRAPHITE", "graphite")]
    [InlineData("Sunset", "sunset")]
    [InlineData("coral", "coral")]
    public void Find_is_case_insensitive(string requested, string expectedId)
    {
        TerminalThemes.Find(requested).Id.Should().Be(expectedId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("dracula")]
    [InlineData("unknown")]
    public void Find_falls_back_to_the_default_for_unknown_values(string? requested)
    {
        TerminalThemes.Find(requested).Should().Be(TerminalThemes.Default);
        TerminalThemes.IsKnown(requested).Should().BeFalse();
    }

    private static string RenderAnsi(IRenderable renderable)
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer),
            Ansi = AnsiSupport.Yes,
            ColorSystem = ColorSystemSupport.TrueColor,
        });
        console.Write(renderable);
        return writer.ToString();
    }

    private static string RenderAnsi(string markup) => RenderAnsi(new Markup(markup));

    private static string AccentCode(string accent)
    {
        var sample = RenderAnsi($"[{accent}]x[/]");
        return sample[..sample.IndexOf('x')];
    }
}
