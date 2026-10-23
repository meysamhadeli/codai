using Codai.Core.Chat;
using Codai.Core.Tests.Fakes;
using FluentAssertions;

namespace Codai.Core.Tests;

/// <summary>
/// Covers the files a compaction puts back: what it restores, what it refuses, and what it
/// does when a file is gone.
/// </summary>
public class ContextRestorerTests
{
    private const string Root = @"C:\project";

    [Fact]
    public async Task Nothing_is_restored_when_the_session_touched_nothing()
    {
        var restorer = new ContextRestorer(new InMemoryFileSystem());

        var restored = await restorer.BuildAsync(Root, []);

        restored.Should().BeNull();
    }

    [Fact]
    public async Task A_read_file_is_restored_with_its_path_and_its_contents()
    {
        var files = new InMemoryFileSystem().AddFile(@"C:\project\src\Parser.cs", "class Parser { }");
        var restorer = new ContextRestorer(files);

        var restored = await restorer.BuildAsync(Root, ["src/Parser.cs"]);

        restored.Should().StartWith(ContextRestorer.Heading);
        restored.Should().Contain("### src/Parser.cs");
        restored.Should().Contain("class Parser { }");
    }

    [Fact]
    public async Task A_path_that_no_longer_exists_is_skipped()
    {
        var files = new InMemoryFileSystem().AddFile(@"C:\project\src\Parser.cs", "class Parser { }");
        var restorer = new ContextRestorer(files);

        var restored = await restorer.BuildAsync(Root, ["src/Gone.cs", "src/Parser.cs"]);

        restored.Should().NotContain("Gone.cs");
        restored.Should().Contain("class Parser { }");
    }

    [Fact]
    public async Task A_path_outside_the_project_is_never_read()
    {
        var files = new InMemoryFileSystem().AddFile(@"C:\secrets.txt", "not yours");
        var restorer = new ContextRestorer(files);

        var restored = await restorer.BuildAsync(Root, [@"..\secrets.txt"]);

        restored.Should().BeNull();
        files.ReadPaths.Should().BeEmpty();
    }

    [Fact]
    public async Task At_most_the_allowed_number_of_files_is_restored()
    {
        var files = new InMemoryFileSystem();
        var paths = new List<string>();

        for (var index = 0; index < ContextRestorer.MaxFiles + 2; index++)
        {
            var path = $@"C:\project\src\File{index}.cs";
            files.AddFile(path, "content");
            paths.Add($"src/File{index}.cs");
        }

        var restored = await new ContextRestorer(files).BuildAsync(Root, paths);

        restored.Should().NotBeNull();
        restored!.Split("### ").Skip(1).Should().HaveCount(ContextRestorer.MaxFiles);
    }

    [Fact]
    public async Task A_file_larger_than_the_per_file_cap_is_truncated()
    {
        var content = new string('x', (ContextRestorer.MaxTokensPerFile * 4) + 500);
        var files = new InMemoryFileSystem().AddFile(@"C:\project\src\Big.cs", content);
        var restorer = new ContextRestorer(files);

        var restored = await restorer.BuildAsync(Root, ["src/Big.cs"]);

        restored.Should().Contain("(truncated)");
        restored!.Length.Should().BeLessThan(content.Length);
    }
}
