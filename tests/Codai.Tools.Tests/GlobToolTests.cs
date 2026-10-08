using FluentAssertions;

namespace Codai.Tools.Tests;

public class GlobToolTests
{
    private static readonly GlobTool Tool = new();

    private static InMemoryFileSystem SampleProject() =>
        ToolTestHost.NewProject()
            .AddFile(Path.Combine(ToolTestHost.Root, "src", "a.cs"), "class A { }")
            .AddFile(Path.Combine(ToolTestHost.Root, "src", "nested", "b.cs"), "class B { }")
            .AddFile(Path.Combine(ToolTestHost.Root, "src", "notes.md"), "# notes")
            .AddFile(Path.Combine(ToolTestHost.Root, "bin", "ignored.cs"), "class Ignored { }");

    [Fact]
    public async Task Matches_files_recursively()
    {
        var result = await Tool.InvokeAsync(
            ToolTestHost.Args("""{"pattern":"**/*.cs"}"""),
            ToolTestHost.Context(SampleProject()));

        result.IsSuccess.Should().BeTrue();
        result.Content.Should().Contain("2 file(s) match").And.Contain("src/a.cs").And.Contain("src/nested/b.cs");
    }

    [Fact]
    public async Task Does_not_report_ignored_directories()
    {
        var result = await Tool.InvokeAsync(
            ToolTestHost.Args("""{"pattern":"**/*.cs"}"""),
            ToolTestHost.Context(SampleProject()));

        result.Content.Should().NotContain("bin");
    }

    [Fact]
    public async Task Restricts_the_search_to_a_subdirectory()
    {
        var result = await Tool.InvokeAsync(
            ToolTestHost.Args("""{"pattern":"**/*.cs","path":"src/nested"}"""),
            ToolTestHost.Context(SampleProject()));

        result.Content.Should().Contain("src/nested/b.cs").And.NotContain("src/a.cs");
    }

    [Fact]
    public async Task A_pattern_that_matches_nothing_says_so()
    {
        var result = await Tool.InvokeAsync(
            ToolTestHost.Args("""{"pattern":"**/*.vb"}"""),
            ToolTestHost.Context(SampleProject()));

        result.IsSuccess.Should().BeTrue();
        result.Content.Should().Be("No files match '**/*.vb'.");
    }

    [Fact]
    public async Task A_missing_pattern_fails() =>
        (await Tool.InvokeAsync(
            ToolTestHost.Args("{}"),
            ToolTestHost.Context(SampleProject())))
        .IsSuccess.Should().BeFalse();

    [Fact]
    public async Task An_escaping_path_fails() =>
        (await Tool.InvokeAsync(
            ToolTestHost.Args("""{"pattern":"*","path":"../.."}"""),
            ToolTestHost.Context(SampleProject())))
        .IsSuccess.Should().BeFalse();

    [Fact]
    public void Declares_itself_read_only() => Tool.IsReadOnly.Should().BeTrue();

    [Fact]
    public void Exposes_the_expected_schema()
    {
        Tool.Name.Should().Be("glob");
        Tool.Schema.GetProperty("required")[0].GetString().Should().Be("pattern");
    }
}
