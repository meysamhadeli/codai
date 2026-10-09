using Codai.Core.Customization;
using Codai.Core.Tests.Fakes;
using FluentAssertions;

namespace Codai.Core.Tests;

/// <summary>
/// Covers discovery of the skills a project keeps in <c>.codai/skills/&lt;name&gt;</c>.
/// </summary>
public class SkillCatalogTests
{
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
