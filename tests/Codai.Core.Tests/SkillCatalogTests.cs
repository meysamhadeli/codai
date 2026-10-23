using Codai.Core.Customization;
using Codai.Core.Tests.Fakes;
using FluentAssertions;

namespace Codai.Core.Tests;

/// <summary>
/// Covers discovery of the skills a project keeps in <c>.codai/skills/&lt;name&gt;</c>.
/// </summary>
public class SkillCatalogTests
{
    [Fact]
    public async Task Identical_global_and_project_skill_roots_keep_project_origin()
    {
        var fileSystem = Project().AddFile(SkillPath("review"), "Project skill.");
        var catalog = new SkillCatalog(fileSystem, Path.Combine(Root, ".codai", "."));

        var skills = await catalog.LoadAsync(Root);

        skills.Should().ContainSingle().Which.IsGlobal.Should().BeFalse();
        skills[0].SourcePath.Should().Be(".codai/skills/review/SKILL.md");
        fileSystem.ReadPaths.Should().ContainSingle().Which.Should()
            .Be(InMemoryFileSystem.Normalize(SkillPath("review")));
        (await catalog.FindAsync(Root, "review"))!.Body.Should().Be("Project skill.");
    }

    [Fact]
    public async Task Empty_project_skill_does_not_hide_valid_global_fallback()
    {
        var global = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "codai-global-skills"));
        var fileSystem = Project().AddFile(SkillPath("review"), "---\ndescription: Empty\n---\n")
            .AddFile(Path.Combine(global, "skills", "review", "SKILL.md"), "Global skill.");
        var catalog = new SkillCatalog(fileSystem, global);

        var skills = await catalog.LoadAsync(Root);

        skills.Should().ContainSingle().Which.IsGlobal.Should().BeTrue();
        (await catalog.FindAsync(Root, "review"))!.Body.Should().Be("Global skill.");
    }

    [Fact]
    public async Task Combined_skill_limit_reserves_project_entries_and_cancellation_is_not_swallowed()
    {
        var global = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "codai-global-skills"));
        var fileSystem = Project().AddFile(SkillPath("project"), "Project.");
        for (var index = 0; index < SkillCatalog.MaxSkills; index++)
        {
            fileSystem.AddFile(Path.Combine(global, "skills", $"global-{index:D3}", "SKILL.md"), "Global.");
        }

        var catalog = new SkillCatalog(fileSystem, global);
        var skills = await catalog.LoadAsync(Root);
        skills.Should().HaveCount(SkillCatalog.MaxSkills);
        skills.Should().Contain(skill => skill.Name == "project" && !skill.IsGlobal);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Func<Task> load = () => catalog.LoadAsync(Root, cancellation.Token);
        await load.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Global_skills_are_discovered_but_project_names_override_case_insensitively()
    {
        var global = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "codai-global-skills"));
        var fileSystem = Project().AddFile(SkillPath("REVIEW"), "Project skill.")
            .AddFile(Path.Combine(global, "skills", "review", "SKILL.md"), "Shadowed skill.")
            .AddFile(Path.Combine(global, "skills", "ship", "SKILL.md"), "Global skill.");
        var catalog = new SkillCatalog(fileSystem, global);

        var skills = await catalog.LoadAsync(Root);

        skills.Select(skill => skill.Name).Should().Equal("review", "ship");
        skills[0].IsGlobal.Should().BeFalse();
        skills[1].IsGlobal.Should().BeTrue();
        (await catalog.FindAsync(Root, "REVIEW"))!.Body.Should().Be("Project skill.");
        (await catalog.FindAsync(Root, "ship"))!.SourcePath.Should()
            .Be(Path.Combine(global, "skills", "ship", "SKILL.md").Replace('\\', '/'));
    }

    [Fact]
    public async Task Disabled_project_and_global_skills_are_not_advertised()
    {
        var global = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "codai-global-skills"));
        var fileSystem = Project()
            .AddFile(SkillPath("review"), "Review.")
            .AddFile(SkillPath("ship"), "Ship.")
            .AddFile(Path.Combine(global, "skills", "deploy", "SKILL.md"), "Deploy.")
            .AddFile(Path.Combine(Root, ".codai", "skills.json"), "{\"disabled\":[\"REVIEW\"]}")
            .AddFile(Path.Combine(global, "skills.json"), "{\"disabled\":[\"deploy\"]}");

        var skills = await new SkillCatalog(fileSystem, global).LoadAsync(Root);

        skills.Select(skill => skill.Name).Should().Equal("ship");
    }

    [Fact]
    public async Task Invalid_skill_settings_do_not_hide_valid_skills()
    {
        var fileSystem = Project()
            .AddFile(SkillPath("review"), "Review.")
            .AddFile(Path.Combine(Root, ".codai", "skills.json"), "not json");

        (await new SkillCatalog(fileSystem).LoadAsync(Root)).Should().ContainSingle()
            .Which.Name.Should().Be("review");
    }

    [Fact]
    public async Task Global_skill_fallback_works_without_project_directory_and_requires_opt_in()
    {
        var global = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "codai-global-skills"));
        var fileSystem = Project().AddFile(Path.Combine(global, "skills", "review", "SKILL.md"), "Global.");

        (await new SkillCatalog(fileSystem).LoadAsync(Root)).Should().BeEmpty();
        (await new SkillCatalog(fileSystem, global).LoadAsync(Root)).Should().ContainSingle();
    }

    private const string Root = @"C:\project";

    private static InMemoryFileSystem Project() => new InMemoryFileSystem().AddDirectory(Root);

    private static string SkillPath(string folder) =>
        Path.Combine(Root, ".codai", "skills", folder, "SKILL.md");

    [Fact]
    public async Task A_project_without_the_directory_has_no_skills()
    {
        var catalog = new SkillCatalog(Project());

        (await catalog.LoadAsync(Root)).Should().BeEmpty();
    }

    [Fact]
    public async Task The_folder_names_the_skill_and_the_file_holds_the_instructions()
    {
        var fileSystem = Project().AddFile(
            SkillPath("Api-Style"),
            "---\ndescription: How this project writes endpoints\n---\nAlways return ProblemDetails.");

        var skill = (await new SkillCatalog(fileSystem).LoadAsync(Root)).Should().ContainSingle().Subject;

        skill.Name.Should().Be("api-style");
        skill.Description.Should().Be("How this project writes endpoints");
        skill.Body.Should().Be("Always return ProblemDetails.");
        skill.SourcePath.Should().EndWith("SKILL.md");
    }

    [Fact]
    public async Task A_folder_without_a_skill_file_is_not_a_skill()
    {
        var fileSystem = Project().AddFile(
            Path.Combine(Root, ".codai", "skills", "not-a-skill", "readme.md"),
            "Nothing here.");

        (await new SkillCatalog(fileSystem).LoadAsync(Root)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_skill_file_with_no_body_is_not_a_skill()
    {
        var fileSystem = Project().AddFile(SkillPath("empty"), "---\ndescription: x\n---\n");

        (await new SkillCatalog(fileSystem).LoadAsync(Root)).Should().BeEmpty();
    }

    [Fact]
    public async Task Without_a_description_the_first_line_of_the_body_is_used()
    {
        var fileSystem = Project().AddFile(SkillPath("testing"), "# Write tests this way\n\nDetails.");

        var skill = (await new SkillCatalog(fileSystem).LoadAsync(Root)).Single();

        skill.Description.Should().Be("Write tests this way");
    }

    [Fact]
    public async Task Skills_are_sorted_by_name()
    {
        var fileSystem = Project()
            .AddFile(SkillPath("zebra"), "Body.")
            .AddFile(SkillPath("alpha"), "Body.");

        (await new SkillCatalog(fileSystem).LoadAsync(Root))
            .Select(skill => skill.Name)
            .Should().Equal("alpha", "zebra");
    }

    [Fact]
    public async Task A_folder_name_that_could_not_be_called_by_the_tool_is_ignored()
    {
        // read_skill resolves by name, so a folder whose name cannot be a name is not
        // exposed at all rather than being advertised and then unfetchable.
        var fileSystem = Project().AddFile(SkillPath("has space"), "Body.");

        (await new SkillCatalog(fileSystem).LoadAsync(Root)).Should().BeEmpty();
    }

    [Fact]
    public async Task Find_is_case_insensitive_but_refuses_a_name_it_would_never_resolve()
    {
        var fileSystem = Project().AddFile(SkillPath("testing"), "Body.");
        var catalog = new SkillCatalog(fileSystem);

        (await catalog.FindAsync(Root, "TESTING")).Should().NotBeNull();
        (await catalog.FindAsync(Root, "not a name")).Should().BeNull();
        (await catalog.FindAsync(Root, "nope")).Should().BeNull();
    }
}
