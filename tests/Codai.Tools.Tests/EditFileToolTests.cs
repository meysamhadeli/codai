using FluentAssertions;

namespace Codai.Tools.Tests;

/// <summary>
/// Covers <c>edit_file</c>. The property that matters most is the refusal: an ambiguous
/// edit must fail loudly rather than change one of several identical spots.
/// </summary>
public class EditFileToolTests
{
    private static readonly EditFileTool Tool = new();

    private static string PathOf(string relative) => Path.Combine(ToolTestHost.Root, relative);

    private static InMemoryFileSystem ProjectWith(string contents) =>
        ToolTestHost.NewProject().AddFile(PathOf("a.cs"), contents);

    [Fact]
    public void Editing_a_file_is_a_mutating_operation() =>
        Tool.IsReadOnly.Should().BeFalse();

    [Fact]
    public async Task Replaces_the_text_and_reports_where()
    {
        var fileSystem = ProjectWith("line one\nline two\nline three\n");

        var result = await Tool.InvokeAsync(
            ToolTestHost.Args("""{"path":"a.cs","old_string":"line two","new_string":"LINE TWO"}"""),
            ToolTestHost.Context(fileSystem));

        result.IsSuccess.Should().BeTrue();
        result.Content.Should().Contain("Edited a.cs at line 2");
        (await fileSystem.ReadAllTextAsync(PathOf("a.cs"))).Should().Be("line one\nLINE TWO\nline three\n");
    }

    [Fact]
    public async Task An_empty_replacement_deletes_the_text()
    {
        var fileSystem = ProjectWith("keep\ndrop\nkeep\n");

        var result = await Tool.InvokeAsync(
            ToolTestHost.Args("""{"path":"a.cs","old_string":"drop\n","new_string":""}"""),
            ToolTestHost.Context(fileSystem));

        result.IsSuccess.Should().BeTrue();
        (await fileSystem.ReadAllTextAsync(PathOf("a.cs"))).Should().Be("keep\nkeep\n");
    }

    [Fact]
    public async Task Refuses_text_that_cannot_be_found()
    {
        var fileSystem = ProjectWith("actually present");

        var result = await Tool.InvokeAsync(
            ToolTestHost.Args("""{"path":"a.cs","old_string":"not present","new_string":"x"}"""),
            ToolTestHost.Context(fileSystem));

        result.IsSuccess.Should().BeFalse();
        result.Content.Should().Contain("does not contain");
        (await fileSystem.ReadAllTextAsync(PathOf("a.cs"))).Should().Be("actually present");
    }

    [Fact]
    public async Task Refuses_an_ambiguous_edit_and_says_how_many_matches_there_are()
    {
        var fileSystem = ProjectWith("value = 1;\nvalue = 1;\n");

        var result = await Tool.InvokeAsync(
            ToolTestHost.Args("""{"path":"a.cs","old_string":"value = 1;","new_string":"value = 2;"}"""),
            ToolTestHost.Context(fileSystem));

        result.IsSuccess.Should().BeFalse();
        result.Content.Should().Contain("appears 2 times");
        result.Content.Should().Contain("replace_all");

        // Nothing was written: a refusal that still edited would be worse than a failure.
        (await fileSystem.ReadAllTextAsync(PathOf("a.cs"))).Should().Be("value = 1;\nvalue = 1;\n");
    }

    [Fact]
    public async Task Replaces_every_occurrence_when_asked()
    {
        var fileSystem = ProjectWith("value = 1;\nvalue = 1;\n");

        var result = await Tool.InvokeAsync(
            ToolTestHost.Args(
                """{"path":"a.cs","old_string":"value = 1;","new_string":"value = 2;","replace_all":true}"""),
            ToolTestHost.Context(fileSystem));

        result.IsSuccess.Should().BeTrue();
        result.Content.Should().Contain("2 occurrences replaced");
        (await fileSystem.ReadAllTextAsync(PathOf("a.cs"))).Should().Be("value = 2;\nvalue = 2;\n");
    }

    [Fact]
    public async Task An_identical_replacement_is_refused_as_a_no_op()
    {
        var fileSystem = ProjectWith("same");

        var result = await Tool.InvokeAsync(
            ToolTestHost.Args("""{"path":"a.cs","old_string":"same","new_string":"same"}"""),
            ToolTestHost.Context(fileSystem));

        result.IsSuccess.Should().BeFalse();
        result.Content.Should().Contain("identical");
    }

    [Fact]
    public async Task An_empty_search_string_is_refused_with_a_pointer_to_the_right_tool()
    {
        var fileSystem = ProjectWith("anything");

        var result = await Tool.InvokeAsync(
            ToolTestHost.Args("""{"path":"a.cs","old_string":"","new_string":"x"}"""),
            ToolTestHost.Context(fileSystem));

        result.IsSuccess.Should().BeFalse();
        result.Content.Should().Contain("write_file");
    }

    [Fact]
    public async Task Editing_a_file_that_does_not_exist_points_at_write_file()
    {
        var result = await Tool.InvokeAsync(
            ToolTestHost.Args("""{"path":"missing.cs","old_string":"a","new_string":"b"}"""),
            ToolTestHost.Context(ToolTestHost.NewProject()));

        result.IsSuccess.Should().BeFalse();
        result.Content.Should().Contain("write_file");
    }

    [Fact]
    public async Task Text_copied_with_different_line_endings_still_matches()
    {
        // The model sees the file as it is on disk but has no reliable way to reproduce
        // CRLF, so the search text is normalised to the file's convention first.
        var fileSystem = ProjectWith("first\r\nsecond\r\n");

        var result = await Tool.InvokeAsync(
            ToolTestHost.Args("""{"path":"a.cs","old_string":"first\nsecond","new_string":"FIRST\nSECOND"}"""),
            ToolTestHost.Context(fileSystem));

        result.IsSuccess.Should().BeTrue();
        (await fileSystem.ReadAllTextAsync(PathOf("a.cs"))).Should().Be("FIRST\r\nSECOND\r\n");
    }

    [Fact]
    public async Task Refuses_a_path_that_leaves_the_project()
    {
        var result = await Tool.InvokeAsync(
            ToolTestHost.Args("""{"path":"../outside.cs","old_string":"a","new_string":"b"}"""),
            ToolTestHost.Context(ToolTestHost.NewProject()));

        result.IsSuccess.Should().BeFalse();
        result.Content.Should().Contain("outside the project");
    }
}
