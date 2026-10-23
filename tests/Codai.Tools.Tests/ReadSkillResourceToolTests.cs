using FluentAssertions;

namespace Codai.Tools.Tests;

public sealed class ReadSkillResourceToolTests
{
    [Fact]
    public async Task Reads_resource_inside_skill_directory()
    {
        var root = ToolTestHost.Root;
        var files = ToolTestHost.NewProject()
            .AddFile(Path.Combine(root, ".codai", "skills", "review", "SKILL.md"), "---\ndescription: review\n---\nReview code.")
            .AddFile(Path.Combine(root, ".codai", "skills", "review", "docs", "checklist.md"), "Check tests.");
        var tool = new ReadSkillResourceTool(new Codai.Core.Customization.SkillCatalog(files));

        var result = await tool.InvokeAsync(
            ToolTestHost.Args("{\"name\":\"review\",\"path\":\"docs/checklist.md\"}"),
            ToolTestHost.Context(files));

        result.IsSuccess.Should().BeTrue();
        result.Content.Should().Contain("Check tests.");
    }

    [Fact]
    public async Task Rejects_resource_escape()
    {
        var root = ToolTestHost.Root;
        var files = ToolTestHost.NewProject()
            .AddFile(Path.Combine(root, ".codai", "skills", "review", "SKILL.md"), "---\ndescription: review\n---\nReview code.");
        var tool = new ReadSkillResourceTool(new Codai.Core.Customization.SkillCatalog(files));

        var result = await tool.InvokeAsync(
            ToolTestHost.Args("{\"name\":\"review\",\"path\":\"../../secret.txt\"}"),
            ToolTestHost.Context(files));

        result.IsSuccess.Should().BeFalse();
        result.Content.Should().Contain("inside the skill directory");
    }
}