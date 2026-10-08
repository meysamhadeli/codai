using Codai.Analysis;
using FluentAssertions;

namespace Codai.Tools.Tests;

public class ProjectFileIndexTests
{
    [Fact]
    public async Task Build_excludes_default_ignored_directories()
    {
        var fileSystem = ToolTestHost.NewProject()
            .AddFile(Path.Combine(ToolTestHost.Root, "src", "a.cs"), "class A { }")
            .AddFile(Path.Combine(ToolTestHost.Root, "bin", "a.dll"), "binary")
            .AddFile(Path.Combine(ToolTestHost.Root, "obj", "a.g.cs"), "generated")
            .AddFile(Path.Combine(ToolTestHost.Root, "node_modules", "p", "index.js"), "module.exports = {}");

        var index = await ProjectFileIndex.BuildAsync(ToolTestHost.Root, fileSystem);

        index.Files.Should().Equal("src/a.cs");
    }

    [Fact]
    public async Task Build_reports_sorted_relative_paths_with_forward_slashes()
    {
        var fileSystem = ToolTestHost.NewProject()
            .AddFile(Path.Combine(ToolTestHost.Root, "b.cs"), "b")
            .AddFile(Path.Combine(ToolTestHost.Root, "src", "a.cs"), "a")
            .AddFile(Path.Combine(ToolTestHost.Root, "src", "z", "c.cs"), "c");

        var index = await ProjectFileIndex.BuildAsync(ToolTestHost.Root, fileSystem);

        index.Files.Should().Equal("b.cs", "src/a.cs", "src/z/c.cs");
        index.Files.Should().OnlyContain(file => !file.Contains('\\'));
    }

    [Fact]
    public async Task Build_reports_directories_with_a_trailing_slash()
    {
        var fileSystem = ToolTestHost.NewProject()
            .AddFile(Path.Combine(ToolTestHost.Root, "src", "nested", "a.cs"), "a");

        var index = await ProjectFileIndex.BuildAsync(ToolTestHost.Root, fileSystem);

        index.Directories.Should().Equal("src/", "src/nested/");
    }

    [Fact]
    public async Task Build_skips_files_above_the_size_limit()
    {
        var fileSystem = ToolTestHost.NewProject()
            .AddFile(Path.Combine(ToolTestHost.Root, "big.txt"), new string('x', 2048))
            .AddFile(Path.Combine(ToolTestHost.Root, "small.txt"), "small");

        var index = await ProjectFileIndex.BuildAsync(
            ToolTestHost.Root,
            fileSystem,
            maxFileSizeBytes: 1024);

        index.Files.Should().Equal("small.txt");
    }

    [Fact]
    public async Task Build_applies_user_ignore_patterns()
    {
        var fileSystem = ToolTestHost.NewProject()
            .AddFile(Path.Combine(ToolTestHost.Root, "bin", "x.dll"), "binary")
            .AddFile(Path.Combine(ToolTestHost.Root, "src", "a.generated.cs"), "class A { }")
            .AddFile(Path.Combine(ToolTestHost.Root, "src", "a.cs"), "class A { }");

        var index = await ProjectFileIndex.BuildAsync(ToolTestHost.Root, fileSystem);
        index.Files.Should().Equal("src/a.cs", "src/a.generated.cs");

        fileSystem.AddFile(
            Path.Combine(ToolTestHost.Root, IgnoreRules.IgnoreFileName),
            "# a comment\n*.generated.cs\n\n");

        var filtered = await ProjectFileIndex.BuildAsync(ToolTestHost.Root, fileSystem);

        filtered.Files.Should().Equal("src/a.cs");
    }

    [Fact]
    public async Task FilesUnder_filters_to_a_subdirectory()
    {
        var fileSystem = ToolTestHost.NewProject()
            .AddFile(Path.Combine(ToolTestHost.Root, "root.cs"), "r")
            .AddFile(Path.Combine(ToolTestHost.Root, "src", "a.cs"), "a")
            .AddFile(Path.Combine(ToolTestHost.Root, "src", "b.cs"), "b")
            .AddFile(Path.Combine(ToolTestHost.Root, "other", "c.cs"), "c");

        var index = await ProjectFileIndex.BuildAsync(ToolTestHost.Root, fileSystem);

        index.FilesUnder("src").Should().Equal("src/a.cs", "src/b.cs");
        index.FilesUnder(string.Empty).Should().HaveCount(4);
    }

    [Theory]
    [InlineData(".", ".")]
    [InlineData("./src/", "src")]
    [InlineData(@"src\", "src")]
    [InlineData("src", "src")]
    [InlineData("  src  ", "src")]
    public void Normalize_canonicalises_a_sub_path(string input, string expected) =>
        ProjectFileIndex.Normalize(input).Should().Be(expected);

    [Fact]
    public void Empty_only_reports_the_ignore_rules()
    {
        var index = ProjectFileIndex.Empty(ToolTestHost.Root);

        index.Files.Should().BeEmpty();
        index.Directories.Should().BeEmpty();
        index.IsIgnored("bin/a.dll").Should().BeTrue();
        index.IsIgnored("src/a.cs").Should().BeFalse();
    }
}
