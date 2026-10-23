using FluentAssertions;

namespace Codai.Tools.Tests;

/// <summary>
/// Covers <c>write_file</c>: creating a file, replacing one, and the cases where it must
/// refuse instead of guessing.
/// </summary>
public class WriteFileToolTests
{
    private static readonly WriteFileTool Tool = new();

    [Fact]
    public void Creating_a_file_is_a_mutating_operation() =>
        Tool.IsReadOnly.Should().BeFalse();

    [Fact]
    public async Task Creates_a_file_and_says_so()
    {
        var fileSystem = ToolTestHost.NewProject();

        var result = await Tool.InvokeAsync(
            ToolTestHost.Args("""{"path":"src/new.cs","content":"class New { }\n"}"""),
            ToolTestHost.Context(fileSystem));

        result.IsSuccess.Should().BeTrue();
        result.Content.Should().Contain("Created src/new.cs");
        result.FileChanges.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new { Path = "src/new.cs", AddedLines = 1, RemovedLines = 0 });
        fileSystem.FileExists(Path.Combine(ToolTestHost.Root, "src", "new.cs")).Should().BeTrue();
    }

    [Fact]
    public async Task Creates_the_directories_the_path_needs()
    {
        var fileSystem = ToolTestHost.NewProject();

        await Tool.InvokeAsync(
            ToolTestHost.Args("""{"path":"a/b/c/deep.cs","content":"x"}"""),
            ToolTestHost.Context(fileSystem));

        fileSystem.DirectoryExists(Path.Combine(ToolTestHost.Root, "a", "b", "c")).Should().BeTrue();
    }

    [Fact]
    public async Task Replacing_an_existing_file_says_updated_rather_than_created()
    {
        var fileSystem = ToolTestHost.NewProject().AddFile(
            Path.Combine(ToolTestHost.Root, "a.cs"),
            "old contents");

        var result = await Tool.InvokeAsync(
            ToolTestHost.Args("""{"path":"a.cs","content":"new contents"}"""),
            ToolTestHost.Context(fileSystem));

        result.Content.Should().StartWith("Updated a.cs");
        result.FileChanges.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new { AddedLines = 1, RemovedLines = 1 });
        (await fileSystem.ReadAllTextAsync(Path.Combine(ToolTestHost.Root, "a.cs")))
            .Should().Be("new contents");
    }

    [Fact]
    public async Task Keeps_the_line_endings_the_file_already_uses()
    {
        var path = Path.Combine(ToolTestHost.Root, "crlf.cs");
        var fileSystem = ToolTestHost.NewProject().AddFile(path, "line one\r\nline two\r\n");

        await Tool.InvokeAsync(
            ToolTestHost.Args("""{"path":"crlf.cs","content":"line one\nline two\nline three\n"}"""),
            ToolTestHost.Context(fileSystem));

        (await fileSystem.ReadAllTextAsync(path)).Should().Be("line one\r\nline two\r\nline three\r\n");
    }

    [Fact]
    public async Task An_empty_content_creates_an_empty_file()
    {
        // An empty string is a legitimate request, unlike a missing one, so the tool must
        // not fall back to the "missing argument" message.
        var fileSystem = ToolTestHost.NewProject();

        var result = await Tool.InvokeAsync(
            ToolTestHost.Args("""{"path":"empty.txt","content":""}"""),
            ToolTestHost.Context(fileSystem));

        result.IsSuccess.Should().BeTrue();
        (await fileSystem.ReadAllTextAsync(Path.Combine(ToolTestHost.Root, "empty.txt"))).Should().BeEmpty();
    }

    [Fact]
    public async Task Refuses_a_missing_path()
    {
        var result = await Tool.InvokeAsync(
            ToolTestHost.Args("""{"content":"x"}"""),
            ToolTestHost.Context(ToolTestHost.NewProject()));

        result.IsSuccess.Should().BeFalse();
        result.Content.Should().Contain("path");
        result.FileChanges.Should().BeEmpty();
    }

    [Fact]
    public async Task Refuses_a_missing_content()
    {
        var result = await Tool.InvokeAsync(
            ToolTestHost.Args("""{"path":"a.cs"}"""),
            ToolTestHost.Context(ToolTestHost.NewProject()));

        result.IsSuccess.Should().BeFalse();
        result.Content.Should().Contain("content");
    }

    [Fact]
    public async Task Refuses_a_path_that_leaves_the_project()
    {
        var result = await Tool.InvokeAsync(
            ToolTestHost.Args("""{"path":"../escape.cs","content":"x"}"""),
            ToolTestHost.Context(ToolTestHost.NewProject()));

        result.IsSuccess.Should().BeFalse();
        result.Content.Should().Contain("outside the project");
    }

    [Fact]
    public async Task Refuses_content_too_large_to_be_a_file_edit()
    {
        var huge = new string('x', (int)ToolWrites.MaxWriteBytes + 1);

        var result = await Tool.InvokeAsync(
            ToolTestHost.Args($$"""{"path":"big.txt","content":{{System.Text.Json.JsonSerializer.Serialize(huge)}}}"""),
            ToolTestHost.Context(ToolTestHost.NewProject()));

        result.IsSuccess.Should().BeFalse();
        result.Content.Should().Contain("larger than");
    }

    [Fact]
    public async Task Rewriting_unchanged_content_reports_zero_changed_lines()
    {
        var files = ToolTestHost.NewProject().AddFile(Path.Combine(ToolTestHost.Root, "same.txt"), "same\n");
        var result = await Tool.InvokeAsync(
            ToolTestHost.Args("""{"path":"same.txt","content":"same\n"}"""), ToolTestHost.Context(files));

        result.IsSuccess.Should().BeTrue();
        result.FileChanges.Should().ContainSingle().Which.Should().BeEquivalentTo(new { AddedLines = 0, RemovedLines = 0 });
    }

    [Fact]
    public async Task Creating_forty_lines_reports_the_exact_counts()
    {
        var content = string.Join('\n', Enumerable.Range(1, 40).Select(index => $"line {index}")) + "\n";
        var result = await Tool.InvokeAsync(
            ToolTestHost.Args(System.Text.Json.JsonSerializer.Serialize(new { path = "forty.txt", content })),
            ToolTestHost.Context(ToolTestHost.NewProject()));

        result.FileChanges.Should().ContainSingle().Which.Should().BeEquivalentTo(new { AddedLines = 40, RemovedLines = 0 });
    }

    [Fact]
    public async Task Refuses_to_overwrite_when_existing_contents_cannot_be_read()
    {
        var path = Path.Combine(ToolTestHost.Root, "existing.txt");
        var files = ToolTestHost.NewProject().AddFile(path, "original");
        files.AfterRead = _ => throw new UnauthorizedAccessException("Read denied.");
        var result = await Tool.InvokeAsync(
            ToolTestHost.Args("""{"path":"existing.txt","content":"replacement"}"""), ToolTestHost.Context(files));

        result.IsSuccess.Should().BeFalse();
        result.Content.Should().Contain("could not be read").And.Contain("Read denied");
        result.FileChanges.Should().BeEmpty();
        files.AfterRead = null;
        (await files.ReadAllTextAsync(path)).Should().Be("original");
    }

    [Fact]
    public async Task Refuses_to_overwrite_an_existing_file_above_the_read_limit()
    {
        var path = Path.Combine(ToolTestHost.Root, "large.txt");
        var original = new string('x', (int)ToolWrites.MaxWriteBytes + 1);
        var files = ToolTestHost.NewProject().AddFile(path, original);
        var result = await Tool.InvokeAsync(
            ToolTestHost.Args("""{"path":"large.txt","content":"replacement"}"""), ToolTestHost.Context(files));

        result.IsSuccess.Should().BeFalse();
        result.Content.Should().Contain("larger than").And.Contain("not opened for editing");
        result.FileChanges.Should().BeEmpty();
        files.ReadPaths.Should().BeEmpty();
        (await files.ReadAllTextAsync(path)).Should().Be(original);
    }
}
