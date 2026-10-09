using Codai.Core.Agents;
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
            new PromptExtras { RequestedContext = "Requested context" });

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

        var prompt = builder.Build(
            Context("code"),
            [],
            "x",
            new PromptExtras { RequestedContext = "requested" }).SystemPrompt;

        prompt.IndexOf("## Here are the tools you can call", StringComparison.Ordinal)
            .Should().BeLessThan(
                prompt.IndexOf("Here are the requsted full context files", StringComparison.Ordinal));
    }

    [Fact]
    public void New_sections_are_absent_when_no_extras_are_supplied()
    {
        var prompt = CreateBuilder().Build(Context("code"), [], "x").SystemPrompt;

        prompt.Should().NotContain("## Here are the project instructions you must follow");
        prompt.Should().NotContain("## Here are the files the user referenced in this request");
    }

    [Fact]
    public void Build_includes_the_project_instructions()
    {
        var prompt = CreateBuilder()
            .Build(Context("code"), [], "x", new PromptExtras { Instructions = "MARKER_INSTRUCTIONS" })
            .SystemPrompt;

        prompt.Should().Contain("## Here are the project instructions you must follow");
        prompt.Should().Contain("MARKER_INSTRUCTIONS");
    }

    [Fact]
    public void Build_includes_the_files_the_user_referenced()
    {
        var prompt = CreateBuilder()
            .Build(Context("code"), [], "x", new PromptExtras { AttachedContext = "MARKER_ATTACHED" })
            .SystemPrompt;

        prompt.Should().Contain("## Here are the files the user referenced in this request");
        prompt.Should().Contain("MARKER_ATTACHED");
    }

    [Fact]
    public void New_sections_are_ordered_between_the_template_and_the_requested_context()
    {
        var builder = new PromptBuilder(
            EmbeddedResources.FullContextTemplate,
            [new StubReadTool("read_file", "Reads a file.")]);

        var prompt = builder.Build(
            Context("code"),
            [],
            "x",
            new PromptExtras
            {
                Instructions = "MARKER_INSTRUCTIONS",
                AttachedContext = "MARKER_ATTACHED",
                RequestedContext = "MARKER_REQUESTED",
            }).SystemPrompt;

        var template = prompt.IndexOf("## Here is the general template prompt for using AI", StringComparison.Ordinal);
        var instructions = prompt.IndexOf("## Here are the project instructions you must follow", StringComparison.Ordinal);
        var tools = prompt.IndexOf("## Here are the tools you can call", StringComparison.Ordinal);
        var attached = prompt.IndexOf("## Here are the files the user referenced in this request", StringComparison.Ordinal);
        var requested = prompt.IndexOf("## Here are the requsted full context files", StringComparison.Ordinal);

        instructions.Should().BeGreaterThan(template);
        tools.Should().BeGreaterThan(instructions);
        attached.Should().BeGreaterThan(tools);
        requested.Should().BeGreaterThan(attached);
    }

    [Fact]
    public void An_empty_instructions_string_does_not_add_a_section()
    {
        var prompt = CreateBuilder()
            .Build(Context("code"), [], "x", new PromptExtras { Instructions = string.Empty })
            .SystemPrompt;

        prompt.Should().NotContain("## Here are the project instructions you must follow");
    }

    [Fact]
    public void A_read_only_mode_is_not_offered_a_mutating_tool()
    {
        var builder = new PromptBuilder(
            EmbeddedResources.FullContextTemplate,
            [
                new StubReadTool("read_file", "Reads a file."),
                new StubReadTool("write_file", "Writes a file.", isReadOnly: false),
            ]);

        var request = builder.Build(
            Context("code"),
            [],
            "x",
            new PromptExtras { Mode = AgentMode.Ask });

        request.SystemPrompt.Should().Contain("- read_file:");
        request.SystemPrompt.Should().NotContain("- write_file:");

        // The same list is what the request carries, which is what the provider filters
        // by and what the executor enforces. If the two ever disagreed, the prompt would
        // promise one thing and the request allow another.
        request.AllowedTools.Should().Equal("read_file");
    }

    [Fact]
    public void Agent_mode_is_offered_every_tool_and_says_so_in_the_request()
    {
        var builder = new PromptBuilder(
            EmbeddedResources.FullContextTemplate,
            [
                new StubReadTool("read_file", "Reads a file."),
                new StubReadTool("write_file", "Writes a file.", isReadOnly: false),
            ]);

        var request = builder.Build(
            Context("code"),
            [],
            "x",
            new PromptExtras { Mode = AgentMode.Agent });

        request.AllowedTools.Should().Equal("read_file", "write_file");
    }

    /// <summary>A tool declared only to render the prompt section.</summary>
    private sealed class StubReadTool : ITool
    {
        private readonly string _schema;

        public StubReadTool(string name, string description, string[]? required = null, bool isReadOnly = true)
        {
            Name = name;
            Description = description;
            IsReadOnly = isReadOnly;
            _schema = required is null
                ? """{"type":"object","properties":{"pattern":{"type":"string"}},"required":["pattern"]}"""
                : $$"""{"type":"object","properties":{},"required":[{{string.Join(',', required.Select(r => $"\"{r}\""))}}]}""";
        }

        public string Name { get; }

        public string Description { get; }

        public bool IsReadOnly { get; }

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
