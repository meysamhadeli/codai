using Codai.Cli.Ui;
using Codai.Core;
using FluentAssertions;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Codai.Session.Tests;

public class TerminalThemeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Informational_notices_are_white_and_errors_are_red_in_every_theme(bool transcript)
    {
        foreach (var theme in TerminalThemes.All)
        {
            using var writer = new StringWriter();
            var console = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Out = new AnsiConsoleOutput(writer),
                Ansi = AnsiSupport.Yes,
                Interactive = InteractionSupport.Yes,
                ColorSystem = ColorSystemSupport.TrueColor,
            });
            console.Profile.Width = 100;
            console.Profile.Height = 30;
            using var ui = new SpectreUserInterface(console, new NoticeInput());
            ui.SetTheme(theme.Id);
            if (transcript)
            {
                await ui.ReadInputAsync();
            }
            writer.GetStringBuilder().Clear();

            ui.WriteInfo("Request stopped. Session remains open.");
            ui.WriteError("Provider request failed.");

            var infoMarker = TerminalIcons.Prefix(TerminalIcon.Info, console.Profile.Capabilities.Unicode);
            var errorMarker = TerminalIcons.Prefix(TerminalIcon.Error, console.Profile.Capabilities.Unicode);
            writer.ToString().Should().MatchRegex(System.Text.RegularExpressions.Regex.Escape(AccentCode("white"))
                + " *" + System.Text.RegularExpressions.Regex.Escape(infoMarker) + "Request stopped\\. Session remains open\\.");
            writer.ToString().Should().MatchRegex(System.Text.RegularExpressions.Regex.Escape(AccentCode("red"))
                + " *" + (errorMarker.Length > 0 ? string.Empty : "! ") + "Provider request failed\\.");
            if (errorMarker.Length > 0)
            {
                writer.ToString().Should().Contain(AccentCode("white") + errorMarker.TrimEnd());
            }
        }
    }

    private sealed class NoticeInput : Codai.Cli.IConsoleInput
    {
        public string? ReadLine() => "hello";
    }

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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Decision_choices_use_the_active_theme_foreground_without_a_selection_background(bool approval)
    {
        foreach (var theme in TerminalThemes.All)
        {
            using var writer = new StringWriter();
            var console = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Out = new AnsiConsoleOutput(writer),
                Ansi = AnsiSupport.Yes,
                ColorSystem = ColorSystemSupport.TrueColor,
            });
            console.Profile.Width = 100;
            console.Profile.Height = 30;
            if (approval)
            {
                new ApprovalPanel(new Codai.Core.Tools.ToolApprovalRequest("run_command", "Run command", "echo test"))
                    .Draw(console, theme.Id);
            }
            else
            {
                new WorkspaceTrustPanel("/project").Draw(console, theme.Id);
            }

            writer.ToString().Should().Contain(AccentCode("bold " + theme.Accent))
                .And.NotContain(AccentCode("bold white on " + theme.Accent));
        }
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Input_marker_uses_active_theme_without_fill_and_keeps_one_space_inset(bool unicode)
    {
        foreach (var theme in TerminalThemes.All)
        {
            var rendered = RenderAnsi(PromptBlock.Row("Draft", 44, theme.Accent, unicode, isInput: true), 44);
            var plain = System.Text.RegularExpressions.Regex.Replace(rendered, "\u001b\\[[0-?]*[ -/]*[@-~]", string.Empty);

            rendered.Should().Contain(AccentCode(theme.Accent)).And.NotContain(AccentCode($"{theme.Accent} on grey11"));
            plain.Should().StartWith(unicode ? "\u276f Draft" : "> Draft");
        }
    }

    [Fact]
    public void History_uses_the_input_chevron_with_a_darker_theme_accent()
    {
        foreach (var theme in TerminalThemes.All)
        {
            foreach (var unicode in new[] { true, false })
            {
                var marker = unicode ? "\u276f" : ">";
                var accent = Style.Parse(theme.Accent).Foreground;
                var darker = $"#{accent.R * 3 / 4:X2}{accent.G * 3 / 4:X2}{accent.B * 3 / 4:X2}";
                var history = RenderAnsi(PromptBlock.Row("History", 44, theme.Accent, unicode), 44);
                var input = RenderAnsi(PromptBlock.Row("Draft", 44, theme.Accent, unicode, isInput: true), 44);

                history.Should().StartWith(AccentCode(darker + " on grey7") + marker);
                input.Should().StartWith(AccentCode(theme.Accent) + marker);
                var transcript = new ChatTranscript { Accent = theme.Accent, Unicode = unicode };
                transcript.AddUser("History");
                var plain = System.Text.RegularExpressions.Regex.Replace(string.Join('\n', transcript.GetLines(44)),
                    "\u001b\\[[0-?]*[ -/]*[@-~]", string.Empty);
                plain.Should().Contain(marker + " History");
            }
        }
    }

    [Fact]
    public void Markdown_headings_are_neutral_and_links_use_the_theme_accent()
    {
        foreach (var theme in TerminalThemes.All)
        {
            var accentCode = AccentCode(theme.Accent);
            var heading = RenderAnsi(TranscriptMarkdown.Render("## Heading", theme.Accent));
            heading.Should().Contain(AccentCode("bold white"));
            var link = RenderAnsi(TranscriptMarkdown.Render("[link](https://example.com)", theme.Accent));
            link.Should().Contain(accentCode, "links should match the theme");
        }
    }

    [Fact]
    public void Inline_code_uses_theme_accent_and_diff_lines_keep_semantic_colors()
    {
        foreach (var theme in TerminalThemes.All)
        {
            var inline = RenderAnsi(TranscriptMarkdown.Render("Use `dotnet run` now.", theme.Accent));
            inline.Should().Contain(AccentCode(theme.Accent) + "dotnet run");
        }

        var diff = RenderAnsi(TranscriptMarkdown.Code("+added\n-removed\nplain", accent: "darkorange"));
        diff.Should().Contain(AccentCode("#9be9a8 on #102819"))
            .And.Contain(AccentCode("#ff9d9d on #30191b"))
            .And.NotContain(AccentCode("darkorange on grey11"));
    }

    [Fact]
    public void Sunset_uses_the_warm_orange_accent()
    {
        var sunset = TerminalThemes.Find("sunset");
        sunset.Accent.Should().Be("#D97757");
        RenderAnsi(TranscriptMarkdown.Render("[link](https://example.com)", sunset.Accent)).Should().Contain("38;2;217;119;87");
    }

    [Fact]
    public void Answer_code_uses_numbered_dark_surface_and_preserves_literal_source()
    {
        var rendered = RenderAnsi(TranscriptMarkdown.Render("File: snake.html\n\n```html\n<!DOCTYPE html>\n<html lang=\"en\">\n[red]literal[/]\n</html>\n```"));
        var plain = System.Text.RegularExpressions.Regex.Replace(rendered, "\u001b\\[[0-?]*[ -/]*[@-~]", string.Empty);

        plain.Should().Contain("File: snake.html").And.Contain("<!DOCTYPE html>")
            .And.Contain("<html lang=\"en\">").And.Contain("[red]literal[/]")
            .And.MatchRegex(@"1\s+<!DOCTYPE html>").And.MatchRegex(@"4\s+</html>");
        rendered.Should().Contain("48;").And.Contain(AccentCode("grey on grey7"));
        rendered.Should().Contain("38;2;", "source tokens should have syntax colors");
    }

    [Theory]
    [InlineData(24)]
    [InlineData(80)]
    [InlineData(140)]
    public void Answer_code_panes_have_matching_unpainted_left_and_right_margins(int width)
    {
        var transcript = new ChatTranscript();
        transcript.AppendMarkdown($"```python\nprint(\"example\")\n{new string('x', width * 2)}\n```");
        var rows = transcript.GetLines(width).Where(row => row.Contains('\u2502')).ToArray();

        rows.Should().NotBeEmpty();
        foreach (var row in rows)
        {
            var plain = System.Text.RegularExpressions.Regex.Replace(row, "\u001b\\[[0-?]*[ -/]*[@-~]", string.Empty);
            var right = System.Text.RegularExpressions.Regex.Match(row, "\u001b\\[0m(?<padding> +)$");
            plain.IndexOf('\u2502').Should().Be(2);
            right.Success.Should().BeTrue("the right margin must be outside the painted code surface");
            right.Groups["padding"].Length.Should().Be(plain.IndexOf('\u2502'));
            plain.GetCellWidth().Should().BeLessThanOrEqualTo(width);
        }
    }

    [Theory]
    [InlineData(12)]
    [InlineData(24)]
    [InlineData(60)]
    public void Code_wraps_to_available_cells_without_numbering_continuations(int width)
    {
        var rendered = RenderAnsi(TranscriptMarkdown.Render("```unknown-language\nfirst = \"界[red]\";\n+keep\n-last\n```"), width);
        var plain = System.Text.RegularExpressions.Regex.Replace(rendered, "\u001b\\[[0-?]*[ -/]*[@-~]", string.Empty);
        var unwrapped = System.Text.RegularExpressions.Regex.Replace(plain, @" +\r?\n│ {4}", string.Empty);

        plain.Split('\n').Should().OnlyContain(line => line.TrimEnd('\r').GetCellWidth() <= width);
        unwrapped.Should().Contain("界").And.Contain("+keep").And.Contain("-last");
        System.Text.RegularExpressions.Regex.Count(plain, @"(?m)^│ 1  ").Should().Be(1);
        unwrapped.Should().MatchRegex(@"2\s+\+keep").And.MatchRegex(@"3\s+-last");
        rendered.Should().Contain("48;");
    }

    [Theory]
    [InlineData("html", "<style>body { color: red; }</style>\n<script>const message = \"hello\";</script>")]
    [InlineData("cs", "public string Name = \"literal\";")]
    [InlineData("js", "const message = \"literal\";")]
    [InlineData("python", "print(\"literal\")")]
    public void Known_languages_and_aliases_render_syntax_colors(string language, string source)
    {
        var rendered = RenderAnsi(TranscriptMarkdown.Render($"```{language}\n{source}\n```"));
        var plain = System.Text.RegularExpressions.Regex.Replace(rendered, "\u001b\\[[0-?]*[ -/]*[@-~]", string.Empty);

        foreach (var line in source.Split('\n'))
        {
            plain.Should().Contain(line);
        }
        rendered.Should().Contain("38;2;");
    }

    [Theory]
    [InlineData("```\n[red]literal[/]\n+keep")]
    [InlineData("```unrecognized\n[red]literal[/]\n+keep\n```")]
    public void Unknown_languages_and_incomplete_streamed_fences_preserve_source(string markdown)
    {
        var rendered = RenderAnsi(TranscriptMarkdown.Render(markdown));
        var plain = System.Text.RegularExpressions.Regex.Replace(rendered, "\u001b\\[[0-?]*[ -/]*[@-~]", string.Empty);

        plain.Should().Contain("[red]literal[/]").And.Contain("+keep");
        rendered.Should().Contain("48;");
    }

    [Fact]
    public void Prose_flows_across_soft_breaks_and_list_items_stay_compact()
    {
        var rendered = RenderAnsi(TranscriptMarkdown.Render("First line\ncontinues here.\n\n- One\n- Two\n\nNext paragraph."));
        var plain = System.Text.RegularExpressions.Regex.Replace(rendered, "\u001b\\[[0-?]*[ -/]*[@-~]", string.Empty);

        plain.Should().Contain("First line continues here.").And.MatchRegex(@"-\s+One\r?\n-\s+Two")
            .And.Contain("Next paragraph.");
    }

    [Theory]
    [InlineData("First  \nSecond")]
    [InlineData("First\\\nSecond")]
    public void Explicit_markdown_line_breaks_are_preserved(string markdown)
    {
        var rendered = RenderAnsi(TranscriptMarkdown.Render(markdown));
        var plain = System.Text.RegularExpressions.Regex.Replace(rendered, "\u001b\\[[0-?]*[ -/]*[@-~]", string.Empty);

        plain.Should().MatchRegex(@"First\r?\nSecond");
    }

    [Fact]
    public void Nerd_tool_markers_and_disclosures_stay_white()
    {
        var transcript = new ChatTranscript { Accent = "purple" };
        transcript.StartTool("grep", "pattern");
        transcript.CompleteTool("grep", true, "finished");
        transcript.ToolResult("grep", "one\ntwo\nthree\nfour");
        var rendered = string.Join('\n', transcript.GetLines(80));
        rendered.Should().Contain(AccentCode(TerminalIcons.NerdFontEnabled ? "white" : "purple")).And.NotContain("38;2;78;186;111")
            .And.Contain("\u001b[38;5;15m    \u25b8 1 more lines");
    }

    [Theory]
    [InlineData("Thinking")]
    [InlineData("Ruminating")]
    [InlineData("Working")]
    [InlineData("Reasoning")]
    [InlineData("Considering")]
    [InlineData("Loading")]
    public void Activity_labels_use_solid_theme_color_ellipsis_and_time_first_metrics(string label)
    {
        var transcript = new ChatTranscript { Accent = "cyan1" };
        transcript.StartActivity(label);
        var rendered = string.Join('\n', transcript.GetLines(80, 1));

        rendered.Should().Contain("\u001b[38;5;51m" + label + "...");
        var plain = System.Text.RegularExpressions.Regex.Replace(rendered, "\u001b\\[[0-?]*[ -/]*[@-~]", string.Empty);
        plain.Should().MatchRegex(@"\(\d+s · esc interrupt\)");
    }

    [Fact]
    public void Waiting_model_activity_shows_zero_tokens_instead_of_hiding_count()
    {
        SpectreUserInterface.FormatActivityMetrics(TimeSpan.FromSeconds(15), 0, true, true, 110)
            .Should().Contain("15s").And.Contain("\u2193 ~0 tokens");
    }

    [Fact]
    public void Model_activity_uses_elapsed_and_output_count_without_thought_duration()
    {
        SpectreUserInterface.FormatActivityMetrics(TimeSpan.FromSeconds(15), 2_100, true, true, 110,
            estimated: false)
            .Should().Be("(15s · \u2193 2.1k tokens · esc interrupt)");
        SpectreUserInterface.FormatActivityMetrics(TimeSpan.FromSeconds(15), 2_100, false, false, 110,
            estimated: false, inputTokens: 3_000)
            .Should().Contain("out 2.1k tokens").And.Contain("in 3.0k").And.NotContain("3.0k in").And.NotContain("~");
    }

    [Theory]
    [InlineData(24, false)]
    [InlineData(44, false)]
    [InlineData(80, true)]
    [InlineData(110, true)]
    public void Activity_token_estimates_are_compact_and_only_shown_when_space_permits(int width, bool tokensVisible)
    {
        var transcript = new ChatTranscript();
        transcript.StartActivity("Considering");
        transcript.OutputTokens = 50_400;
        var lines = transcript.GetLines(width).Select(line =>
            System.Text.RegularExpressions.Regex.Replace(line, "\u001b\\[[0-?]*[ -/]*[@-~]", string.Empty)).ToArray();

        string.Join('\n', lines).Contains("\u2193 ~50.4k tokens", StringComparison.Ordinal).Should().Be(tokensVisible);
        lines.Should().OnlyContain(line => line.GetCellWidth() <= width);
    }

    [Fact]
    public void Invalidating_the_transcript_reapplies_a_changed_accent()
    {
        var transcript = new ChatTranscript { Accent = "purple" };
        transcript.AppendMarkdown("Use `example.cs`.");
        string.Join('\n', transcript.GetLines(80)).Should().Contain(AccentCode("purple"));

        transcript.Accent = "darkorange";
        transcript.Invalidate();
        string.Join('\n', transcript.GetLines(80)).Should().Contain(AccentCode("darkorange"));
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

    private static string RenderAnsi(IRenderable renderable, int width = 80)
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer),
            Ansi = AnsiSupport.Yes,
            ColorSystem = ColorSystemSupport.TrueColor,
        });
        console.Profile.Width = width;
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
