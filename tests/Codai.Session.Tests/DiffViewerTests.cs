using Codai.Cli.Ui;
using FluentAssertions;
using Spectre.Console;

namespace Codai.Session.Tests;

public class DiffViewerTests
{
    private const string Patch = "diff --git a/first.cs b/first.cs\n--- a/first.cs\n+++ b/first.cs\n@@ -1 +1 @@\n-old\n+new\n@@ -30 +30 @@\n-before\n+after\ndiff --git a/second.cs b/second.cs\n--- a/second.cs\n+++ b/second.cs\n@@ -1 +1 @@\n-left\n+right\n";

    [Theory]
    [InlineData(44, 22)]
    [InlineData(110, 40)]
    public void Diff_opens_file_overview_then_detail_and_returns_without_closing(int width, int height)
    {
        var viewer = new DiffViewer(Patch, string.Empty);
        var overview = Plain(Render(viewer, width, height));
        overview.Should().Contain("2 files changed").And.Contain("+3").And.Contain("-3")
            .And.Contain("first.cs").And.Contain("second.cs").And.NotContain("+new");

        Press(viewer, ConsoleKey.Enter);
        Plain(Render(viewer, width, height)).Should().Contain("first.cs").And.Contain("+new").And.Contain("-old");
        Press(viewer, ConsoleKey.Escape);
        viewer.Closed.Should().BeFalse();
        Plain(Render(viewer, width, height)).Should().Contain("Enter to view").And.NotContain("+new");
        Press(viewer, ConsoleKey.Escape);
        viewer.Closed.Should().BeTrue();
    }

    [Theory]
    [InlineData(44, 22)]
    [InlineData(110, 40)]
    public void Diff_viewer_renders_empty_state_and_switches_source(int width, int height)
    {
        var viewer = new DiffViewer(string.Empty, Patch);
        Render(viewer, width, height).Should().Contain("Working-tree changes").And.Contain("0 files").And.Contain("No diff!");
        Press(viewer, ConsoleKey.D, 'd');
        Render(viewer, width, height).Should().Contain("Staged changes").And.Contain("2 files").And.Contain("first.cs");
        Press(viewer, ConsoleKey.Escape);
        viewer.Closed.Should().BeTrue();
    }

    [Fact]
    public void Diff_viewer_navigates_hunks_files_review_and_help_without_touching_source()
    {
        var viewer = new DiffViewer(Patch, string.Empty);
        Press(viewer, ConsoleKey.Oem6, ']');
        Press(viewer, ConsoleKey.Oem6, ']');
        Render(viewer, 110, 12).Should().Contain("30").And.Contain("after").And.NotContain("+new");
        Press(viewer, ConsoleKey.N, 'n');
        Press(viewer, ConsoleKey.M, 'm');
        Render(viewer).Should().Contain("second.cs  [reviewed]");
        Press(viewer, ConsoleKey.D, 'd');
        Render(viewer).Should().Contain("No diff!");
        Press(viewer, ConsoleKey.D, 'd');
        Render(viewer).Should().Contain("second.cs  [reviewed]");
        Press(viewer, ConsoleKey.Oem2, '?');
        Render(viewer).Should().Contain("Diff keys").And.Contain("Horizontal scroll");
        Press(viewer, ConsoleKey.Escape);
        viewer.Closed.Should().BeFalse();
        Press(viewer, ConsoleKey.Tab);
        Press(viewer, ConsoleKey.UpArrow);
        Render(viewer).Should().Contain("[x]").And.Contain("Enter to view").And.NotContain("+new");
        Press(viewer, ConsoleKey.Enter);
        Render(viewer).Should().Contain("first.cs").And.Contain("+new");
    }

    [Fact]
    public void Diff_viewer_keeps_quoted_paths_binary_metadata_and_control_text_safe()
    {
        var patch = "diff --git a/old b/old\nrename from old\nrename to new name\ndiff --git a/bin b/bin\nBinary files a/bin and b/bin differ\ndiff --git \"a/tab\\tname\" \"b/tab\\tname\"\n--- \"a/tab\\tname\"\n+++ \"b/tab\\tname\"\n@@ -1 +1 @@\n-old\n+\u001b[2Jnew\n";
        var viewer = new DiffViewer(patch, "");
        Render(viewer).Should().Contain("new name");
        Press(viewer, ConsoleKey.N, 'n');
        Press(viewer, ConsoleKey.Enter);
        Render(viewer).Should().Contain("Binary files");
        Press(viewer, ConsoleKey.N, 'n');
        Render(viewer).Should().Contain("tab\\tname").And.Contain("[2Jnew").And.NotContain("\u001b[2J");
    }

    [Theory]
    [InlineData(32)]
    [InlineData(110)]
    public void Detail_wraps_unicode_content_with_one_real_number_and_blank_continuation_gutters(int width)
    {
        var content = new string('\u754c', 35) + " END-OF-LINE";
        var patch = "diff --git a/file b/file\n--- a/file\n+++ b/file\n@@ -12,2 +20,2 @@\n-old\n+" + content + "\n context\n";
        var viewer = new DiffViewer(patch, "");
        Press(viewer, ConsoleKey.Enter);
        var output = Render(viewer, width, 40);
        var rows = PaintedRows(output);
        rows.Should().OnlyContain(row => row.GetCellWidth() <= width - 2);
        rows.Should().Contain(row => row.Contains("12 -old", StringComparison.Ordinal));
        rows.Should().Contain(row => row.Contains("20 +", StringComparison.Ordinal));
        rows.Should().Contain(row => row.Contains("21  context", StringComparison.Ordinal));
        string.Concat(rows.Select(row => row.Length > 8 ? row[6..^2].TrimEnd() : ""))
            .Should().Contain(content.TrimEnd());
        Plain(output).Should().NotContain("diff --git").And.NotContain("@@ -12");
    }

    [Fact]
    public void Overview_shows_per_file_counts_scroll_indicators_and_preserves_source_selection()
    {
        var patch = string.Concat(Enumerable.Range(1, 12).Select(index =>
            $"diff --git a/file-{index}.cs b/file-{index}.cs\n--- a/file-{index}.cs\n+++ b/file-{index}.cs\n@@ -1 +1 @@\n-old\n+new\n"));
        var viewer = new DiffViewer(patch, Patch);
        var initial = Render(viewer);
        var rows = PaintedRows(initial);
        rows.Should().Contain(row => row.Contains("12 files changed", StringComparison.Ordinal) && row.Contains("+12", StringComparison.Ordinal) && row.Contains("-12", StringComparison.Ordinal));
        rows.Should().Contain(row => row.Contains("file-1.cs", StringComparison.Ordinal) && row.Contains("+1", StringComparison.Ordinal) && row.Contains("-1", StringComparison.Ordinal));
        Plain(initial).Should().Contain("6 more files").And.NotContain("file-7.cs");
        Press(viewer, ConsoleKey.End);
        var bottom = Render(viewer);
        Plain(bottom).Should().Contain("6 more files").And.Contain("file-12.cs").And.NotContain("file-1.cs");
        Press(viewer, ConsoleKey.DownArrow);
        Press(viewer, ConsoleKey.M, 'm');
        Press(viewer, ConsoleKey.D, 'd');
        Plain(Render(viewer)).Should().Contain("first.cs");
        Press(viewer, ConsoleKey.D, 'd');
        Plain(Render(viewer)).Should().Contain("[x] file-12.cs");
    }

    [Fact]
    public void Inline_overview_respects_minimum_top_and_detail_keeps_truncation_notice()
    {
        var viewer = new DiffViewer(Patch + "[Diff truncated]\n", "");
        var overview = Render(viewer, minimumTop: 10);
        var positions = System.Text.RegularExpressions.Regex.Matches(overview, "\u001b\\[(\\d+);\\d+H")
            .Select(match => int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
        positions.Should().OnlyContain(row => row >= 10 && row <= 40);
        viewer.MeasureHeight(110, 30).Should().BeLessThan(30);
        Press(viewer, ConsoleKey.N, 'n');
        Press(viewer, ConsoleKey.Enter);
        Plain(Render(viewer)).Should().Contain("[Diff truncated]");
        Press(viewer, ConsoleKey.End);
        Plain(Render(viewer, 150)).Should().Contain("[Diff truncated]");
    }

    [Fact]
    public void Empty_file_sections_keep_overview_and_detail_navigation_safe()
    {
        var viewer = new DiffViewer("diff --git a/empty b/empty\n", "");
        Plain(Render(viewer)).Should().Contain("1 file changed").And.Contain("+0").And.Contain("-0");
        Press(viewer, ConsoleKey.Enter);
        Press(viewer, ConsoleKey.RightArrow);
        Plain(Render(viewer)).Should().Contain("empty");
        Press(viewer, ConsoleKey.End);
        Press(viewer, ConsoleKey.Escape);
        viewer.Closed.Should().BeFalse();
    }

    private static void Press(DiffViewer viewer, ConsoleKey key, char character = '\0') =>
        viewer.HandleKey(new ConsoleKeyInfo(character, key, false, false, false));

    private static string Plain(string text) =>
        System.Text.RegularExpressions.Regex.Replace(text, "\u001b\\[[0-?]*[ -/]*[@-~]", string.Empty);

    private static string[] PaintedRows(string text) => System.Text.RegularExpressions.Regex
        .Matches(text, "\u001b\\[\\d+;2H(?<body>.*?)(?=\u001b\\[\\d+;\\d+H|$)", System.Text.RegularExpressions.RegexOptions.Singleline)
        .Select(match => Plain(match.Groups["body"].Value)).ToArray();

    private static string Render(DiffViewer viewer, int width = 110, int height = 40, int minimumTop = 1)
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.Yes });
        console.Profile.Width = width;
        console.Profile.Height = height;
        viewer.Draw(console, minimumTop: minimumTop);
        return writer.ToString();
    }
}