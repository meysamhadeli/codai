using FluentAssertions;

namespace Codai.Tools.Tests;

public class ReadFileToolTests
{
    private static readonly ReadFileTool Tool = new();

    private static InMemoryFileSystem ProjectWithFile(string relativePath, string contents) =>
        ToolTestHost.NewProject().AddFile(Path.Combine(ToolTestHost.Root, relativePath), contents);

    [Fact]
    public async Task Reads_a_file_with_line_numbers()
    {
        var fileSystem = ProjectWithFile("src/a.cs", "alpha\nbravo\ncharlie\n");

        var result = await Tool.InvokeAsync(
            ToolTestHost.Args("""{"path":"src/a.cs"}"""),
            ToolTestHost.Context(fileSystem));

        result.IsSuccess.Should().BeTrue();
        ToolTestHost.Lines(result.Content).Should().Equal(
            "src/a.cs (lines 1-3 of 3):",
            "1\talpha",
            "2\tbravo",
            "3\tcharlie");
    }

    [Fact]
    public async Task A_trailing_newline_does_not_add_a_line()
    {
        var fileSystem = ProjectWithFile("a.txt", "only\n");

        var result = await Tool.InvokeAsync(
            ToolTestHost.Args("""{"path":"a.txt"}"""),
            ToolTestHost.Context(fileSystem));

        result.Content.Should().Contain("(lines 1-1 of 1):");
    }

    [Fact]
    public async Task Crlf_and_lf_files_number_identically()
    {
        var fileSystem = ProjectWithFile("crlf.txt", "one\r\ntwo\r\n");
        var lfSystem = ProjectWithFile("lf.txt", "one\ntwo\n");

        var crlf = await Tool.InvokeAsync(
            ToolTestHost.Args("""{"path":"crlf.txt"}"""),
            ToolTestHost.Context(fileSystem));

        var lf = await Tool.InvokeAsync(
            ToolTestHost.Args("""{"path":"lf.txt"}"""),
            ToolTestHost.Context(lfSystem));

        ToolTestHost.Lines(crlf.Content).Skip(1).Should().Equal(
            ToolTestHost.Lines(lf.Content).Skip(1));
    }

    [Fact]
    public async Task Offset_and_limit_select_a_window_and_report_the_rest()
    {
        var fileSystem = ProjectWithFile("a.txt", "1\n2\n3\n4\n5\n");

        var result = await Tool.InvokeAsync(
            ToolTestHost.Args("""{"path":"a.txt","offset":2,"limit":2}"""),
            ToolTestHost.Context(fileSystem));

        ToolTestHost.Lines(result.Content).Should().Equal(
            "a.txt (lines 2-3 of 5):",
            "2\t2",
            "3\t3",
            "[... 2 more line(s); continue with offset=4]");
    }

    [Fact]
    public async Task Line_numbers_are_padded_to_the_width_of_the_total()
    {
        var contents = string.Join('\n', Enumerable.Range(1, 10)) + "\n";
        var fileSystem = ProjectWithFile("a.txt", contents);

        var result = await Tool.InvokeAsync(
            ToolTestHost.Args("""{"path":"a.txt","limit":1}"""),
            ToolTestHost.Context(fileSystem));

        result.Content.Should().Contain(" 1\t1");
    }

    [Fact]
    public async Task A_missing_path_argument_fails() =>
        (await Tool.InvokeAsync(
            ToolTestHost.Args("{}"),
            ToolTestHost.Context(ToolTestHost.NewProject())))
        .IsSuccess.Should().BeFalse();

    [Fact]
    public async Task A_missing_file_fails()
    {
        var result = await Tool.InvokeAsync(
            ToolTestHost.Args("""{"path":"nope.cs"}"""),
            ToolTestHost.Context(ToolTestHost.NewProject()));

        result.IsSuccess.Should().BeFalse();
        result.Content.Should().Contain("does not exist");
    }

    [Fact]
    public async Task An_empty_file_is_reported_as_empty()
    {
        var fileSystem = ProjectWithFile("a.txt", string.Empty);

        var result = await Tool.InvokeAsync(
            ToolTestHost.Args("""{"path":"a.txt"}"""),
            ToolTestHost.Context(fileSystem));

        result.IsSuccess.Should().BeTrue();
        result.Content.Should().Be("a.txt is empty.");
    }

    [Fact]
    public async Task An_offset_past_the_end_fails_with_the_real_line_count()
    {
        var fileSystem = ProjectWithFile("a.txt", "1\n2\n3\n");

        var result = await Tool.InvokeAsync(
            ToolTestHost.Args("""{"path":"a.txt","offset":10}"""),
            ToolTestHost.Context(fileSystem));

        result.IsSuccess.Should().BeFalse();
        result.Content.Should().Contain("past the end").And.Contain("3 line(s)");
    }

    [Fact]
    public async Task A_binary_file_is_rejected()
    {
        var fileSystem = ProjectWithFile("a.bin", "a\0b");

        var result = await Tool.InvokeAsync(
            ToolTestHost.Args("""{"path":"a.bin"}"""),
            ToolTestHost.Context(fileSystem));

        result.IsSuccess.Should().BeFalse();
        result.Content.Should().Contain("binary");
    }

    [Fact]
    public async Task Relative_escape_out_of_the_project_is_rejected()
    {
        var result = await Tool.InvokeAsync(
            ToolTestHost.Args("""{"path":"../outside.txt"}"""),
            ToolTestHost.Context(ToolTestHost.NewProject()));

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task An_absolute_path_outside_the_project_is_rejected()
    {
        var outside = Path.Combine(Path.GetTempPath(), "codai-tools-tests", "outside.txt");
        var result = await Tool.InvokeAsync(
            ToolTestHost.Args($$"""{"path":"{{outside.Replace("\\", "\\\\")}}"}"""),
            ToolTestHost.Context(ToolTestHost.NewProject()));

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Declares_itself_read_only() => Tool.IsReadOnly.Should().BeTrue();

    [Fact]
    public void Schema_is_valid_json()
    {
        var schema = Tool.Schema;

        schema.GetProperty("required")[0].GetString().Should().Be("path");
    }
}
