using Codai.Core.Customization;
using Codai.Core.Tools;
using FluentAssertions;

namespace Codai.Tools.Tests;

/// <summary>
/// Covers <c>read_skill</c>: the half of progressive disclosure that fetches a skill body
/// only once the model has decided the skill applies.
/// </summary>
public class ReadSkillToolTests
{
    private static string SkillPath(string name) =>
        Path.Combine(ToolTestHost.Root, ".codai", "skills", name, "SKILL.md");

    private static (ReadSkillTool Tool, ToolContext Context) Project(params string[] skills)
    {
        var fileSystem = ToolTestHost.NewProject();

        foreach (var skill in skills)
        {
            fileSystem.AddFile(SkillPath(skill), $"---\ndescription: The {skill} skill\n---\nInstructions for {skill}.");
        }

        return (new ReadSkillTool(new SkillCatalog(fileSystem)), ToolTestHost.Context(fileSystem));
    }

    [Fact]
    public void Reading_a_skill_changes_nothing() =>
        Project().Tool.IsReadOnly.Should().BeTrue();

    [Fact]
    public async Task Returns_the_body_of_the_named_skill()
    {
        var (tool, context) = Project("testing");

        var result = await tool.InvokeAsync(
            ToolTestHost.Args("""{"name":"testing"}"""),
            context);

        result.IsSuccess.Should().BeTrue();
        result.Content.Should().Contain("# Skill: testing");
        result.Content.Should().Contain("Instructions for testing.");
    }

    [Fact]
    public async Task Says_where_the_skill_came_from_so_the_user_can_open_it()
    {
        var (tool, context) = Project("testing");

        var result = await tool.InvokeAsync(ToolTestHost.Args("""{"name":"testing"}"""), context);

        result.Content.Should().Contain("SKILL.md");
    }

    [Fact]
    public async Task An_unknown_name_lists_what_is_available()
    {
        // An empty failure would leave the model guessing; the list turns the mistake
        // into a correction.
        var (tool, context) = Project("testing", "reviewing");

        var result = await tool.InvokeAsync(ToolTestHost.Args("""{"name":"nope"}"""), context);

        result.IsSuccess.Should().BeFalse();
        result.Content.Should().Contain("No skill named 'nope'");
        result.Content.Should().Contain("reviewing, testing");
    }

    [Fact]
    public async Task An_unknown_name_in_a_project_with_no_skills_says_so()
    {
        var (tool, context) = Project();

        var result = await tool.InvokeAsync(ToolTestHost.Args("""{"name":"nope"}"""), context);

        result.IsSuccess.Should().BeFalse();
        result.Content.Should().Contain("defines none");
    }

    [Fact]
    public async Task Refuses_a_missing_name()
    {
        var (tool, context) = Project("testing");

        var result = await tool.InvokeAsync(ToolTestHost.Args("""{}"""), context);

        result.IsSuccess.Should().BeFalse();
        result.Content.Should().Contain("name");
    }
}
