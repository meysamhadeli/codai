using FluentAssertions;

namespace Codai.Tools.Tests;

public class ListFilesToolTests
{
    private static readonly ListFilesTool Tool = new();

    private static InMemoryFileSystem SampleProject() =>
        ToolTestHost.NewProject()
            .AddFile(Path.Combine(ToolTestHost.Root, "README.md"), "# readme")
            .AddFile(Path.Combine(ToolTestHost.Root, "src", "a.cs"), "class A { }")
            .AddFile(Path.Combine(ToolTestHost.Root, "src", "nested", "b.cs"), "class B { }")
            .AddFile(Path.Combine(ToolTestHost.Root, "bin", "a.dll"), "binary");

    [Fact]
    public async Task Lists_only_direct_children_by_default()
    {
        var result = await Tool.InvokeAsync(
            ToolTestHost.Args("{}"),
            ToolTestHost.Context(SampleProject()));

        result.IsSuccess.Should().BeTrue();
        ToolTestHost.Lines(result.Content).Should().Equal(
            ".:",
            "src/",
            "README.md");
    }

    [Fact]
    public async Task Directories_are_listed_before_files()
    {
        var result = await Tool.InvokeAsync(
            ToolTestHost.Args("{}"),
            ToolTestHost.Context(SampleProject()));

        var lines = ToolTestHost.Lines(result.Content);

        lines[1].Should().EndWith("/");
        lines[2].Should().Be("README.md");
    }

    [Fact]
    public async Task Lists_a_subdirectory()
    {
        var result = await Tool.InvokeAsync(
            ToolTestHost.Args("""{"path":"src"}"""),
            ToolTestHost.Context(SampleProject()));

        ToolTestHost.Lines(result.Content).Should().Equal(
            "src:",
            "nested/",
            "a.cs");
    }

    [Fact]
    public async Task Recursive_listing_returns_every_file_below_the_path()
    {
        var result = await Tool.InvokeAsync(
            ToolTestHost.Args("""{"path":"src","recursive":true}"""),
            ToolTestHost.Context(SampleProject()));

        result.Content.Should().Contain("src/a.cs").And.Contain("src/nested/b.cs");
    }

    [Fact]
    public async Task Ignored_directories_are_never_shown()
    {
        var result = await Tool.InvokeAsync(
            ToolTestHost.Args("""{"recursive":true}"""),
            ToolTestHost.Context(SampleProject()));

        result.Content.Should().NotContain("bin");
    }

    [Fact]
    public async Task A_missing_directory_fails()
    {
        var result = await Tool.InvokeAsync(
            ToolTestHost.Args("""{"path":"nope"}"""),
            ToolTestHost.Context(SampleProject()));

        result.IsSuccess.Should().BeFalse();
        result.Content.Should().Contain("does not exist");
    }

    [Fact]
    public async Task An_empty_directory_is_reported_as_empty()
    {
        var fileSystem = ToolTestHost.NewProject().AddDirectory(Path.Combine(ToolTestHost.Root, "empty"));

        var result = await Tool.InvokeAsync(
            ToolTestHost.Args("""{"path":"empty"}"""),
            ToolTestHost.Context(fileSystem));

        result.IsSuccess.Should().BeTrue();
        result.Content.Should().Contain("is empty");
    }

    [Fact]
    public void Declares_itself_read_only() => Tool.IsReadOnly.Should().BeTrue();
}
