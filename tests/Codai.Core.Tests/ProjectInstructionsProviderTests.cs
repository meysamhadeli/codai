using Codai.Core.Prompting;
using Codai.Core.Tests.Fakes;
using FluentAssertions;

namespace Codai.Core.Tests;

public class ProjectInstructionsProviderTests
{
    private const string Root = @"C:\project";

    private static (ProjectInstructionsProvider Provider, InMemoryFileSystem FileSystem) Create()
    {
        var fileSystem = new InMemoryFileSystem();
        return (new ProjectInstructionsProvider(fileSystem), fileSystem);
    }

    [Fact]
    public async Task Returns_null_when_the_project_has_no_instruction_file()
    {
        var (provider, _) = Create();

        var result = await provider.LoadAsync(Root);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Reads_AGENTS_md()
    {
        var (provider, fileSystem) = Create();
        fileSystem.AddFile(Path.Combine(Root, "AGENTS.md"), "Always write tests.");

        var result = await provider.LoadAsync(Root);

        result.Should().NotBeNull();
        result!.SourcePath.Should().Be("AGENTS.md");
        result.Content.Should().Be("Always write tests.");
    }

    [Fact]
    public async Task Prefers_AGENTS_md_over_the_other_candidates()
    {
        var (provider, fileSystem) = Create();
        fileSystem.AddFile(Path.Combine(Root, "AGENTS.md"), "agents");
        fileSystem.AddFile(Path.Combine(Root, "CLAUDE.md"), "claude");
        fileSystem.AddFile(Path.Combine(Root, "CODAI.md"), "codai");

        var result = await provider.LoadAsync(Root);

        result!.Content.Should().Be("agents");
    }

    [Fact]
    public async Task Falls_back_to_the_next_candidate_when_the_first_is_blank()
    {
        var (provider, fileSystem) = Create();
        fileSystem.AddFile(Path.Combine(Root, "AGENTS.md"), "   \n");
        fileSystem.AddFile(Path.Combine(Root, "CLAUDE.md"), "claude");

        var result = await provider.LoadAsync(Root);

        result!.SourcePath.Should().Be("CLAUDE.md");
        result.Content.Should().Be("claude");
    }

    [Fact]
    public async Task Reads_nested_instruction_files()
    {
        var (provider, fileSystem) = Create();
        fileSystem.AddFile(Path.Combine(Root, ".codai", "instructions.md"), "nested");

        var result = await provider.LoadAsync(Root);

        result!.SourcePath.Should().Be(".codai/instructions.md");
    }

    [Fact]
    public async Task Trims_surrounding_whitespace()
    {
        var (provider, fileSystem) = Create();
        fileSystem.AddFile(Path.Combine(Root, "AGENTS.md"), "\n\nkeep me\n\n");

        var result = await provider.LoadAsync(Root);

        result!.Content.Should().Be("keep me");
    }

    [Fact]
    public async Task Truncates_instructions_that_are_too_long()
    {
        var (provider, fileSystem) = Create();
        fileSystem.AddFile(
            Path.Combine(Root, "AGENTS.md"),
            new string('x', ProjectInstructionsProvider.MaxCharacters + 100));

        var result = await provider.LoadAsync(Root);

        result!.Content.Length.Should().BeLessThan(ProjectInstructionsProvider.MaxCharacters + 200);
        result.Content.Should().EndWith("[... instructions truncated, keep this file shorter ...]");
    }

    [Fact]
    public async Task Does_not_leave_the_project_root()
    {
        var (provider, fileSystem) = Create();
        fileSystem.AddFile(Path.Combine(Root, "AGENTS.md"), "inside");

        var result = await provider.LoadAsync(Root);

        result!.SourcePath.Should().NotContain("..");
    }
}
