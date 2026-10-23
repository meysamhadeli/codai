using FluentAssertions;

namespace Codai.Tools.Tests;

public sealed class SkillsToolTests
{
    [Fact]
    public async Task Loads_skill_body_by_name()
    {
        var files = ToolTestHost.NewProject().AddFile(
            Path.Combine(ToolTestHost.Root, ".codai", "skills", "testing", "SKILL.md"),
            "---\ndescription: testing\n---\nRun focused tests.");
        var tool = new SkillsTool(new Codai.Core.Customization.SkillCatalog(files));

        var result = await tool.InvokeAsync(
            ToolTestHost.Args("{\"name\":\"testing\"}"), ToolTestHost.Context(files));

        result.IsSuccess.Should().BeTrue();
        result.Content.Should().Contain("Run focused tests.");
    }
}