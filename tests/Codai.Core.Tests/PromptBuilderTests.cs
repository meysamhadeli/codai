using Codai.Core.Assets;
using Codai.Core.Chat;
using Codai.Core.Projects;
using Codai.Core.Prompting;
using Codai.Core.Tools;
using FluentAssertions;

namespace Codai.Core.Tests;

public class EmbeddedResourcesTests
{
    [Fact]
    public void FullContextTemplate_is_embedded_and_not_empty()
    {
        EmbeddedResources.FullContextTemplate.Should().NotBeNullOrWhiteSpace();
        EmbeddedResources.FullContextTemplate.Should().Contain("You are an AI code assistant");
        EmbeddedResources.FullContextTemplate.Should().Contain("CODE BLOCK");
    }
}

public class PromptBuilderTests
{
    private static PromptBuilder CreateBuilder() =>
        new(EmbeddedResources.FullContextTemplate);

    private static ProjectContext Context(params string[] rawCodes) => new()
    {
        Files = [],
        RawCodes = rawCodes,
    };

    [Fact]
    public void Build_includes_context_history_and_user_request()
    {
        var builder = CreateBuilder();
        var history = new List<ChatTurn>
        {
            new(DateTimeOffset.Parse("2024-01-01T10:00:00Z"), "prev-request-1", "prev-response-1"),
            new(DateTimeOffset.Parse("2024-01-01T11:00:00Z"), "prev-request-2", "prev-response-2"),
        };

        var request = builder.Build(
            Context("code1", "code2"),
            history,
            "User request",
            "Requested context");

        request.SystemPrompt.Should().Contain("code1");
        request.SystemPrompt.Should().Contain("code2");
        request.SystemPrompt.Should().Contain("prev-request-1");
        request.SystemPrompt.Should().Contain("prev-response-2");
        request.SystemPrompt.Should().Contain("Requested context");
        request.SystemPrompt.Should().Contain("## Here is the history of chats");
        request.SystemPrompt.Should().Contain("## Here is the summary of context of project");

        request.UserMessage.Should().Contain("User request");
        request.UserMessage.Should().StartWith("## Here is user request");
    }

    [Fact]
    public void Build_omits_requested_context_section_when_not_provided()
    {
        var request = CreateBuilder().Build(Context("code"), [], "do something");

        request.SystemPrompt.Should().NotContain("Here are the requsted full context files");
    }

    [Fact]
    public void Build_joins_raw_codes_with_separator()
    {
        var request = CreateBuilder().Build(Context("alpha", "beta"), [], "x");

        request.SystemPrompt.Should().Contain("alpha\n---------\n\nbeta");
    }

    [Fact]
    public void Build_uses_template_between_separators()
    {
        var request = CreateBuilder().Build(Context("code"), [], "x");

        request.SystemPrompt.Should().Contain(
            "## Here is the general template prompt for using AI\n\n" +
            EmbeddedResources.FullContextTemplate);
    }

    [Fact]
    public void Constructor_rejects_empty_template()
    {
        var act = () => new PromptBuilder("  ");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Build_omits_the_tools_section_when_no_tools_are_available()
    {
        var request = CreateBuilder().Build(Context("code"), [], "x");

        request.SystemPrompt.Should().NotContain("## Here are the tools you can call");
    }

    [Fact]
    public void Build_describes_each_tool_and_its_required_arguments()
    {
        var builder = new PromptBuilder(
            EmbeddedResources.FullContextTemplate,
            [new StubReadTool("grep", "Searches file contents.")]);

        var prompt = builder.Build(Context("code"), [], "x").SystemPrompt;

        prompt.Should().Contain("## Here are the tools you can call");
        prompt.Should().Contain("- grep: Searches file contents. (required: pattern)");
        prompt.Should().Contain("Prefer calling a tool over assuming what a file contains");
    }

    [Fact]
    public void Build_lists_a_tool_with_no_required_arguments_plainly()
    {
        var builder = new PromptBuilder(
            EmbeddedResources.FullContextTemplate,
            [new StubReadTool("list_files", "Lists directories.", required: [])]);

        var prompt = builder.Build(Context("code"), [], "x").SystemPrompt;

        prompt.Should().Contain("- list_files: Lists directories.");
        prompt.Should().NotContain("(required:");
    }

    [Fact]
    public void Build_keeps_the_tools_section_before_the_requested_context()
    {
        var builder = new PromptBuilder(
            EmbeddedResources.FullContextTemplate,
            [new StubReadTool("grep", "Searches file contents.")]);

        var prompt = builder.Build(Context("code"), [], "x", "requested").SystemPrompt;

        prompt.IndexOf("## Here are the tools you can call", StringComparison.Ordinal)
            .Should().BeLessThan(
                prompt.IndexOf("Here are the requsted full context files", StringComparison.Ordinal));
    }

    /// <summary>A tool declared only to render the prompt section.</summary>
    private sealed class StubReadTool : ITool
    {
        private readonly string _schema;

        public StubReadTool(string name, string description, string[]? required = null)
        {
            Name = name;
            Description = description;
            _schema = required is null
                ? """{"type":"object","properties":{"pattern":{"type":"string"}},"required":["pattern"]}"""
                : $$"""{"type":"object","properties":{},"required":[{{string.Join(',', required.Select(r => $"\"{r}\""))}}]}""";
        }

        public string Name { get; }

        public string Description { get; }

        public bool IsReadOnly => true;

        public string ParametersSchema => _schema;

        public System.Text.Json.JsonElement Schema =>
            Codai.Core.Tools.JsonSchema.Parse(ParametersSchema);

        public Task<ToolResult> InvokeAsync(
            System.Text.Json.JsonElement arguments,
            ToolContext context,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ToolResult.Success("stub"));
    }
}
