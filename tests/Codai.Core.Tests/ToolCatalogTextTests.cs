using Codai.Core.Assets;
using Codai.Core.Projects;
using Codai.Core.Prompting;
using Codai.Core.Tests.Fakes;
using Codai.Core.Tools;
using FluentAssertions;

namespace Codai.Core.Tests;

/// <summary>
/// Covers the tool list that the prompt and the sub-agent prompt both render. It is one
/// function so that a tool is never described two ways, and these tests pin the shape the
/// model is taught to expect.
/// </summary>
public class ToolCatalogTextTests
{
    [Fact]
    public void One_tool_is_one_line()
    {
        var line = ToolCatalogText.Line(new StubTool("read_file", description: "Read a file."));

        line.Should().Be("- read_file: Read a file.");
    }

    [Fact]
    public void Required_arguments_come_from_the_tool_schema()
    {
        // The point of reading the schema rather than a hand-written list: this cannot
        // drift from what the provider validates a call against.
        var tool = new StubTool(
            "edit_file",
            description: "Edit a file.",
            parametersSchema: """{"type":"object","properties":{"path":{},"old_string":{}},"required":["path","old_string"]}""");

        ToolCatalogText.Line(tool)
            .Should().Be("- edit_file: Edit a file. (required: path, old_string)");
    }

    [Fact]
    public void A_schema_with_no_required_array_adds_nothing()
    {
        var tool = new StubTool("list_files", description: "List files.");

        ToolCatalogText.Line(tool).Should().Be("- list_files: List files.");
    }

    [Fact]
    public void A_schema_that_cannot_be_read_is_not_an_error()
    {
        // A tool whose schema is malformed still has to appear in the list: dropping it
        // would leave the model with no way to know it exists. The schema itself is the
        // provider's problem, and it fails loudly there.
        var tool = new StubTool(
            "odd",
            description: "Odd.",
            parametersSchema: """{"required":"path"}""");

        ToolCatalogText.Line(tool).Should().Be("- odd: Odd.");
    }

    [Fact]
    public void Tools_are_listed_in_the_order_they_are_offered()
    {
        var tools = new List<ITool>
        {
            new StubTool("read_file", description: "Read a file."),
            new StubTool("grep", description: "Search.", parametersSchema: """{"type":"object","properties":{"pattern":{}},"required":["pattern"]}"""),
        };

        ToolCatalogText.Lines(tools)
            .Should().Be("- read_file: Read a file.\n- grep: Search. (required: pattern)");
    }

    [Fact]
    public void No_tools_is_an_empty_list_rather_than_a_blank_line()
    {
        // A blank line in the prompt would read as an empty section; an empty string
        // lets the caller decide whether to render the heading at all.
        ToolCatalogText.Lines([]).Should().BeEmpty();
    }

    [Fact]
    public void Lines_is_the_same_text_the_prompt_uses()
    {
        // The reason this type exists: the prompt and the sub-agent prompt must describe
        // one tool with one sentence, or the two assistants learn two versions of it.
        var tools = new List<ITool>
        {
            new StubTool("read_file", description: "Read a file."),
            new StubTool("write_file", isReadOnly: false, description: "Write a file."),
        };

        var prompt = new PromptBuilder(EmbeddedResources.FullContextTemplate, tools)
            .Build(new ProjectContext { Files = [], RawCodes = [] }, [], "x")
            .SystemPrompt;

        prompt.Should().Contain(ToolCatalogText.Lines(tools));
    }
}
