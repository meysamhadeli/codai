using FluentAssertions;

namespace Codai.Tools.Tests;

public class GrepToolTests
{
    private static readonly GrepTool Tool = new();

    private static InMemoryFileSystem SampleProject() =>
        ToolTestHost.NewProject()
            .AddFile(Path.Combine(ToolTestHost.Root, "src", "a.cs"), "class Alpha { }\n// TODO: fix\n")
            .AddFile(Path.Combine(ToolTestHost.Root, "src", "b.designer.cs"), "class Beta { }\n")
            .AddFile(Path.Combine(ToolTestHost.Root, "docs", "notes.md"), "todo later\n")
            .AddFile(Path.Combine(ToolTestHost.Root, "src", "image.png"), "not\0text");

    [Fact]
    public async Task Reports_path_line_and_text_for_each_match()
    {
        var result = await Tool.InvokeAsync(
            ToolTestHost.Args("""{"pattern":"class "}"""),
            ToolTestHost.Context(SampleProject()));

        result.IsSuccess.Should().BeTrue();
        ToolTestHost.Lines(result.Content).Should().Contain(
            "src/a.cs:1: class Alpha { }",
            "src/b.designer.cs:1: class Beta { }");
    }

    [Fact]
    public async Task The_header_counts_matches_and_files()
    {
        var result = await Tool.InvokeAsync(
            ToolTestHost.Args("""{"pattern":"class "}"""),
            ToolTestHost.Context(SampleProject()));

        result.Content.Should().StartWith("2 match(es) for 'class ' in 3 file(s):");
    }

    [Fact]
    public async Task The_include_glob_limits_which_files_are_searched()
    {
        var result = await Tool.InvokeAsync(
            ToolTestHost.Args("""{"pattern":"class ","include":"*.designer.cs"}"""),
            ToolTestHost.Context(SampleProject()));

        result.Content.Should().Contain("src/b.designer.cs").And.NotContain("src/a.cs");
    }

    [Fact]
    public async Task The_path_limits_the_search_to_a_directory()
    {
        var result = await Tool.InvokeAsync(
            ToolTestHost.Args("""{"pattern":"todo","path":"docs"}"""),
            ToolTestHost.Context(SampleProject()));

        result.Content.Should().Contain("docs/notes.md:1: todo later").And.NotContain("src/a.cs");
    }

    [Fact]
    public async Task Matching_is_case_insensitive_by_default()
    {
        var result = await Tool.InvokeAsync(
            ToolTestHost.Args("""{"pattern":"TODO"}"""),
            ToolTestHost.Context(SampleProject()));

        ToolTestHost.Lines(result.Content).Should().Contain(
            "src/a.cs:2: // TODO: fix",
            "docs/notes.md:1: todo later");
    }

    [Fact]
    public async Task Case_sensitivity_can_be_requested()
    {
        var result = await Tool.InvokeAsync(
            ToolTestHost.Args("""{"pattern":"TODO","case_sensitive":true}"""),
            ToolTestHost.Context(SampleProject()));

        ToolTestHost.Lines(result.Content).Should().Contain("src/a.cs:2: // TODO: fix");
        result.Content.Should().NotContain("docs/notes.md");
    }

    [Fact]
    public async Task Binary_files_are_skipped()
    {
        var result = await Tool.InvokeAsync(
            ToolTestHost.Args("""{"pattern":"text"}"""),
            ToolTestHost.Context(SampleProject()));

        result.Content.Should().Be("No matches for 'text'. Searched 3 file(s).");
    }

    [Fact]
    public async Task An_invalid_regular_expression_fails_cleanly()
    {
        var result = await Tool.InvokeAsync(
            ToolTestHost.Args("""{"pattern":"(unclosed"}"""),
            ToolTestHost.Context(SampleProject()));

        result.IsSuccess.Should().BeFalse();
        result.Content.Should().Contain("not a valid regular expression");
    }

    [Fact]
    public async Task A_missing_pattern_fails() =>
        (await Tool.InvokeAsync(
            ToolTestHost.Args("{}"),
            ToolTestHost.Context(SampleProject())))
        .IsSuccess.Should().BeFalse();

    [Fact]
    public async Task No_matches_mentions_the_include_glob()
    {
        var result = await Tool.InvokeAsync(
            ToolTestHost.Args("""{"pattern":"zzz","include":"**/*.cs"}"""),
            ToolTestHost.Context(SampleProject()));

        result.Content.Should().Be("No matches for 'zzz' in files matching '**/*.cs'. Searched 2 file(s).");
    }

    [Fact]
    public async Task A_very_long_line_is_clamped()
    {
        var line = new string('a', 1000) + "needle";
        var fileSystem = ToolTestHost.NewProject()
            .AddFile(Path.Combine(ToolTestHost.Root, "big.txt"), line + "\n");

        var result = await Tool.InvokeAsync(
            ToolTestHost.Args("""{"pattern":"needle"}"""),
            ToolTestHost.Context(fileSystem));

        var matchLine = ToolTestHost.Lines(result.Content)[1];
        matchLine.Length.Should().BeLessThan(400);
        matchLine.Should().EndWith("...");
    }

    [Fact]
    public void Declares_itself_read_only() => Tool.IsReadOnly.Should().BeTrue();

    [Fact]
    public void Exposes_the_expected_schema()
    {
        Tool.Name.Should().Be("grep");
        Tool.Schema.GetProperty("required")[0].GetString().Should().Be("pattern");
    }
}
