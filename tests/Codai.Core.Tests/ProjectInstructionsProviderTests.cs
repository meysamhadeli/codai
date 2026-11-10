using Codai.Core.Prompting;
using Codai.Core.Tests.Fakes;
using FluentAssertions;

namespace Codai.Core.Tests;

public class ProjectInstructionsProviderTests
{
    [Fact]
    public async Task Filesystem_root_is_a_valid_instruction_scope()
    {
        var root = Path.GetPathRoot(Path.GetTempPath())!;
        var fileSystem = new InMemoryFileSystem();
        fileSystem.AddFile(Path.Combine(root, "AGENTS.md"), "DRIVE_ROOT_GUIDANCE");

        var result = await new ProjectInstructionsProvider(fileSystem).LoadAsync(root);

        result!.Content.Should().Contain("DRIVE_ROOT_GUIDANCE");
    }

    [Theory]
    [InlineData("bytes")]
    [InlineData("characters")]
    [InlineData("documents")]
    [InlineData("directories")]
    public async Task Instruction_discovery_fails_closed_when_bounded_context_limits_are_exceeded(string limit)
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "codai-instruction-limits"));
        var fileSystem = new InMemoryFileSystem();
        var files = new List<string>();
        if (limit == "bytes")
        {
            fileSystem.AddFile(Path.Combine(root, "AGENTS.md"), new string('x', 1024 * 1024 + 1));
        }
        else if (limit == "characters")
        {
            foreach (var name in ProjectInstructionsProvider.CandidateFileNames)
            {
                fileSystem.AddFile(Path.Combine(root, name.Replace('/', Path.DirectorySeparatorChar)), new string('x', ProjectInstructionsProvider.MaxCharacters));
            }
        }
        else
        {
            var count = limit == "documents" ? 22 : 129;
            for (var index = 0; index < count; index++)
            {
                var directory = Path.Combine(root, $"scope{index}");
                files.Add(Path.Combine(directory, "Main.cs"));
                if (limit == "documents")
                {
                    foreach (var name in new[] { "AGENTS.md", "CLAUDE.md", "instructions.md" })
                    {
                        fileSystem.AddFile(Path.Combine(directory, name), "guidance");
                    }
                }
            }
        }

        var load = () => new ProjectInstructionsProvider(fileSystem).LoadForFilesAsync(root, files);

        await load.Should().ThrowAsync<IOException>();
    }

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
    public async Task Combines_standard_instruction_files_in_stable_order()
    {
        var (provider, fileSystem) = Create();
        fileSystem.AddFile(Path.Combine(Root, "AGENTS.md"), "agents");
        fileSystem.AddFile(Path.Combine(Root, "CLAUDE.md"), "claude");
        fileSystem.AddFile(Path.Combine(Root, "instructions.md"), "instructions");

        var result = await provider.LoadAsync(Root);

        result!.Content.Should().Contain("agents").And.Contain("claude").And.Contain("instructions");
        result.Content.IndexOf("agents", StringComparison.Ordinal).Should().BeLessThan(result.Content.IndexOf("claude", StringComparison.Ordinal));
        result.Content.IndexOf("claude", StringComparison.Ordinal).Should().BeLessThan(result.Content.IndexOf("instructions", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Codai_specific_instruction_filename_is_not_discovered()
    {
        var (provider, fileSystem) = Create();
        fileSystem.AddFile(Path.Combine(Root, "CODAI.md"), "obsolete");

        (await provider.LoadAsync(Root)).Should().BeNull();
    }

    [Theory]
    [InlineData("AGENTS.md")]
    [InlineData("CLAUDE.md")]
    [InlineData("instructions.md")]
    public async Task Nested_instructions_apply_only_to_referenced_subtrees(string name)
    {
        var (provider, fileSystem) = Create();
        fileSystem.AddFile(Path.Combine(Root, "AGENTS.md"), "ROOT_GUIDANCE");
        fileSystem.AddFile(Path.Combine(Root, "src", name), "SOURCE_GUIDANCE");
        fileSystem.AddFile(Path.Combine(Root, "src", "Main.cs"), "code");
        fileSystem.AddFile(Path.Combine(Root, "other", name), "UNRELATED_GUIDANCE");

        (await provider.LoadAsync(Root))!.Content.Should().NotContain("SOURCE_GUIDANCE").And.NotContain("UNRELATED_GUIDANCE");
        var instructions = await provider.LoadForFilesAsync(Root, ["src/Main.cs"]);

        instructions!.Content.Should().Contain("ROOT_GUIDANCE").And.Contain("SOURCE_GUIDANCE").And.NotContain("UNRELATED_GUIDANCE");
        instructions.Documents.Should().Contain(document => document.ScopePath == "src" && document.Content == "SOURCE_GUIDANCE");
    }

    [Fact]
    public async Task Repository_ancestors_are_loaded_before_workspace_without_crossing_git_boundary()
    {
        var (provider, fileSystem) = Create();
        var repository = Path.Combine(Root, "repo");
        var workspace = Path.Combine(repository, "src");
        fileSystem.AddFile(Path.Combine(Root, "AGENTS.md"), "OUTSIDE_GUIDANCE");
        fileSystem.AddFile(Path.Combine(repository, ".git"), "gitdir: metadata");
        fileSystem.AddFile(Path.Combine(repository, "AGENTS.md"), "REPOSITORY_GUIDANCE");
        fileSystem.AddFile(Path.Combine(workspace, "CLAUDE.md"), "WORKSPACE_GUIDANCE");

        var instructions = await provider.LoadAsync(workspace);

        instructions!.Content.Should().Contain("REPOSITORY_GUIDANCE").And.Contain("WORKSPACE_GUIDANCE").And.NotContain("OUTSIDE_GUIDANCE");
        instructions.Documents.Select(document => document.ScopePath).Should().Equal("..", ".");
    }

    [Fact]
    public async Task Instruction_changes_and_deletions_are_visible_on_next_load()
    {
        var (provider, fileSystem) = Create();
        var file = Path.Combine(Root, "instructions.md");
        fileSystem.AddFile(file, "FIRST_GUIDANCE");
        (await provider.LoadAsync(Root))!.Content.Should().Be("FIRST_GUIDANCE");

        fileSystem.AddFile(file, "UPDATED_GUIDANCE");
        (await provider.LoadAsync(Root))!.Content.Should().Be("UPDATED_GUIDANCE");
        fileSystem.DeleteFile(file);
        (await provider.LoadAsync(Root)).Should().BeNull();
    }

    [Fact]
    public async Task Linked_instruction_files_outside_repository_are_rejected()
    {
        var (provider, fileSystem) = Create();
        var file = Path.GetFullPath(Path.Combine(Root, "AGENTS.md"));
        fileSystem.AddFile(file, "UNTRUSTED_GUIDANCE");
        fileSystem.CanonicalPaths[InMemoryFileSystem.Normalize(file)] = Path.Combine(Path.GetTempPath(), "outside-instructions", "AGENTS.md");

        Func<Task> load = async () => await provider.LoadAsync(Root);

        await load.Should().ThrowAsync<IOException>().WithMessage("*outside*boundary*");
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
