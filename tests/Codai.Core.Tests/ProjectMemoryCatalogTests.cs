using Codai.Core.Customization;
using FluentAssertions;

namespace Codai.Core.Tests;

public sealed class ProjectMemoryCatalogTests
{
    private const string Root = @"C:\project";

    [Fact]
    public async Task Loads_markdown_memory_in_stable_order_with_relative_paths()
    {
        var fileSystem = new Fakes.InMemoryFileSystem()
            .AddDirectory(Path.Combine(Root, ".codai", "memory"))
            .AddFile(Path.Combine(Root, ".codai", "memory", "status.md"), "status")
            .AddFile(Path.Combine(Root, ".codai", "memory", "overview.md"), "overview");

        var documents = await new ProjectMemoryCatalog(fileSystem).LoadAsync(Root);

        documents.Select(document => document.Name).Should().Equal("overview", "status");
        documents[0].SourcePath.Should().Be(".codai/memory/overview.md");
        documents[0].Content.Should().Be("overview");
    }

    [Fact]
    public async Task Ignores_non_markdown_files_and_bounds_large_documents()
    {
        var fileSystem = new Fakes.InMemoryFileSystem()
            .AddDirectory(Path.Combine(Root, ".codai", "memory"))
            .AddFile(Path.Combine(Root, ".codai", "memory", "notes.txt"), "ignore")
            .AddFile(
                Path.Combine(Root, ".codai", "memory", "large.md"),
                new string('x', ProjectMemoryCatalog.MaxCharactersPerDocument + 100));

        var documents = await new ProjectMemoryCatalog(fileSystem).LoadAsync(Root);

        documents.Should().ContainSingle();
        documents[0].Content.Should().Contain("memory document truncated");
        documents[0].Content.Length.Should().BeLessThan(ProjectMemoryCatalog.MaxCharactersPerDocument + 100);
    }
}
