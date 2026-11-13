using Codai.Core.Agents;
using Codai.Core.Assets;
using Codai.Core.Chat;
using Codai.Core.Customization;
using Codai.Core.Projects;
using Codai.Core.Prompting;
using Codai.Core.Tools;
using FluentAssertions;

namespace Codai.Core.Tests;

public class EmbeddedResourcesTests
{
    [Fact]
    public void Compaction_template_requests_concise_continuation_state_not_full_transcript()
    {
        EmbeddedResources.CompactHistoryPrompt.Should().Contain("## Goal and Constraints")
            .And.Contain("## Remaining Work")
            .And.Contain("Newer user instructions take precedence")
            .And.Contain("Do not call tools")
            .And.NotContain("## All User Messages")
            .And.NotContain("Start with an <analysis> block");
    }

    [Fact]
    public void FullContextTemplate_is_embedded_and_not_empty()
    {
        EmbeddedResources.FullContextTemplate.Should().NotBeNullOrWhiteSpace();
        EmbeddedResources.FullContextTemplate.Should().Contain("You are an AI code assistant");
        EmbeddedResources.FullContextTemplate.Should().Contain("CODE BLOCK");
    }

    [Fact]
    public void Default_template_scopes_coding_workflows_and_missing_context_to_the_actual_request()
    {
        EmbeddedResources.FullContextTemplate.Should().Contain("For a greeting, give a brief greeting")
            .And.Contain("not new tasks")
            .And.Contain("not evidence that the actual implementation is empty or broken")
            .And.Contain("use an available read tool")
            .And.Contain("Only if the user's request needs file contents")
            .And.Contain("Do not repeat full files after applying edits")
            .And.Contain("When delivering edits as text")
            .And.Contain("Do not invent file contents")
            .And.NotContain("You always COMPLETELY IMPLEMENT the needed code");
    }
}

public class PromptBuilderTests
{
    [Theory]
    [InlineData(AgentMode.Agent)]
    [InlineData(AgentMode.Plan)]
    public void Skill_discovery_advertises_descriptions_and_loads_instructions_on_demand(AgentMode mode)
    {
        var builder = new PromptBuilder(EmbeddedResources.FullContextTemplate,
            [new StubReadTool("read_skill", "Read skill instructions", required: ["name"])]);
        var request = builder.Build(Context("code"), [], "Review changes", new PromptExtras
        {
            Mode = mode,
            Skills =
            [
                new("review", "Review changes and risks", "LARGE_REVIEW_BODY", "skills/review/SKILL.md"),
                new("disabled", "DISABLED_DESCRIPTION", "DISABLED_BODY", "skills/disabled/SKILL.md") { IsEnabled = false },
                new("manual", "MANUAL_DESCRIPTION", "MANUAL_BODY", "skills/manual/SKILL.md") { DisableModelInvocation = true },
            ],
        });

        request.SystemPrompt.Should().Contain("review: Review changes and risks")
            .And.Contain("call read_skill with its exact name")
            .And.Contain("When a skill matches, you must call read_skill")
            .And.Contain("before other tools or answering")
            .And.Contain("Do not skip a matching skill because you already know the topic or can call its tools directly")
            .And.Contain("Load supporting resources only when needed")
            .And.NotContain("LARGE_REVIEW_BODY")
            .And.NotContain("DISABLED_DESCRIPTION")
            .And.NotContain("MANUAL_DESCRIPTION");
        request.AllowedTools.Should().Equal("read_skill");
    }

    [Theory]
    [InlineData(AgentMode.Agent)]
    [InlineData(AgentMode.Plan)]
    public void Automatic_capability_selection_uses_offered_metadata_without_expanding_permissions(AgentMode mode)
    {
        var builder = new PromptBuilder(EmbeddedResources.FullContextTemplate,
            [new StubReadTool("docs_lookup", "Looks up library documentation."),
             new StubReadTool("external_action", "Changes external state.", isReadOnly: false)]);
        var request = builder.Build(Context("code"), [], "Research framework documentation",
            new PromptExtras { Mode = mode, Instructions = "PROJECT_PRIORITY" });

        request.SystemPrompt.Should().Contain("Select relevant capabilities automatically")
            .And.Contain("prefer a relevant offered documentation MCP tool")
            .And.Contain("use its offered resolver first")
            .And.Contain("prefer a relevant offered search capability")
            .And.Contain("not as a required first step for general documentation research")
            .And.Contain("call tool_search to discover relevant documentation")
            .And.Contain("do not list or scan unrelated repository directories")
            .And.Contain("a URL supplied by the user or found in actual source or search results")
            .And.Contain("After a 403 or 404, reconsider the source")
            .And.Contain("do not invent progress, expose private reasoning")
            .And.Contain("Use only tools offered for the current request");
        request.AllowedTools.Should().Equal(mode == AgentMode.Agent
            ? ["docs_lookup", "external_action"] : ["docs_lookup"]);
        if (mode == AgentMode.Plan)
        {
            request.SystemPrompt.Should().NotContain("external_action");
        }
        request.SystemPrompt.IndexOf("Select relevant capabilities automatically", StringComparison.Ordinal)
            .Should().BeLessThan(request.SystemPrompt.IndexOf("PROJECT_PRIORITY", StringComparison.Ordinal));
    }

    [Fact]
    public void Automatic_skill_selection_keeps_unchecked_and_model_opted_out_skills_out_of_prompt()
    {
        var request = CreateBuilder().Build(Context("code"), [], "Review this change", new PromptExtras
        {
            Mode = AgentMode.Agent,
            Skills =
            [
                new("review", "Reviews changes", "REVIEW_BODY", "skills/review/SKILL.md"),
                new("manual", "Manual only", "MANUAL_BODY", "skills/manual/SKILL.md") { IsEnabled = false },
                new("private", "No model invocation", "OPTED_OUT_BODY", "skills/private/SKILL.md") { DisableModelInvocation = true },
            ],
        });
        request.SystemPrompt.Should().Contain("Apply relevant enabled skills without requiring the user to name them")
            .And.Contain("REVIEW_BODY").And.NotContain("MANUAL_BODY").And.NotContain("OPTED_OUT_BODY");
        request.AllowedTools.Should().BeEmpty();
    }

    [Fact]
    public void Capability_selection_guidance_is_absent_for_legacy_or_capability_free_requests()
    {
        var builder = new PromptBuilder(EmbeddedResources.FullContextTemplate, [new StubReadTool("docs_lookup", "Documentation")]);
        builder.Build(Context("code"), [], "Hi").SystemPrompt.Should().NotContain("Select relevant capabilities automatically");
        CreateBuilder().Build(Context("code"), [], "Hi", new PromptExtras { Mode = AgentMode.Agent })
            .SystemPrompt.Should().NotContain("Select relevant capabilities automatically");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Only_enabled_skill_instructions_reach_each_request(bool enabled)
    {
        var extras = new PromptExtras
        {
            Mode = AgentMode.Plan,
            Skills =
            [
                new("caveman", "Concise replies", "ACTIVE_SKILL_BODY", "skills/caveman/SKILL.md") { IsEnabled = enabled },
                new("audit", "Disabled review", "DISABLED_SKILL_BODY", "skills/audit/SKILL.md") { IsEnabled = false },
            ],
        };
        var request = CreateBuilder().Build(Context("reference"), [], "Help", extras);
        request.SystemPrompt.Should().NotContain("DISABLED_SKILL_BODY").And.Contain("Only read-only tools are allowed");
        if (enabled)
        {
            request.SystemPrompt.Should().Contain("ACTIVE_SKILL_BODY").And.Contain("active instructions for every request")
                .And.Contain("do not override project instructions");
        }
        else
        {
            request.SystemPrompt.Should().NotContain("ACTIVE_SKILL_BODY").And.NotContain("skills available in this project");
        }
    }

    private static PromptBuilder CreateBuilder() =>
        new(EmbeddedResources.FullContextTemplate);

    private static ProjectContext Context(params string[] rawCodes) => new()
    {
        Files = [],
        RawCodes = rawCodes,
    };

    [Theory]
    [InlineData(AgentMode.Agent)]
    [InlineData(AgentMode.Plan)]
    public void Agent_requests_keep_stable_instructions_before_growing_history(AgentMode mode)
    {
        var builder = CreateBuilder();
        var extras = new PromptExtras { Mode = mode, Instructions = "STABLE_PROJECT_INSTRUCTIONS" };
        var first = builder.Build(Context("STABLE_PROJECT_CONTEXT"), [], "Hi", extras);
        var second = builder.Build(Context("STABLE_PROJECT_CONTEXT"),
            [new ChatTurn(DateTimeOffset.UtcNow, "Hi", "Hello")], "How are you?", extras);

        var historyOffset = first.SystemPrompt.IndexOf("## Here is the history of chats", StringComparison.Ordinal);
        historyOffset.Should().BeGreaterThan(first.SystemPrompt.IndexOf("STABLE_PROJECT_INSTRUCTIONS", StringComparison.Ordinal));
        second.SystemPrompt.Should().StartWith(first.SystemPrompt[..historyOffset]);
        second.SystemPrompt.Should().Contain("Hello").And.Contain("## Current mode:");
        second.UserMessage.Should().EndWith("How are you?");
    }

    [Theory]
    [InlineData(AgentMode.Plan, "Only read-only tools are allowed")]
    [InlineData(AgentMode.Agent, "Historical Plan restrictions do not set the active mode")]
    public void Current_mode_reminder_follows_stale_history_and_attached_context(AgentMode mode, string restriction)
    {
        var history = new[]
        {
            new ChatTurn(DateTimeOffset.UtcNow, "earlier task", "Old mode guidance", ChatTurnKind.CompactionSummary),
        };
        var request = CreateBuilder().Build(Context("code"), history, "current task",
            new PromptExtras { Mode = mode, RequestedContext = "STALE_CONTEXT_MODE" });

        var reminder = request.SystemPrompt.IndexOf($"## Current mode: {mode}", StringComparison.Ordinal);
        reminder.Should().BeGreaterThan(request.SystemPrompt.IndexOf("STALE_CONTEXT_MODE", StringComparison.Ordinal));
        request.SystemPrompt.Should().Contain(restriction)
            .And.Contain("Project constraints and approval requirements still apply");
        request.UserMessage.Should().EndWith("current task");
    }

    [Theory]
    [InlineData(AgentMode.Agent)]
    [InlineData(AgentMode.Plan)]
    public void Mode_guidance_keeps_simple_conversation_brief_without_imposing_a_coding_workflow(AgentMode mode)
    {
        var request = CreateBuilder().Build(Context("repository summary"), [], "Hi",
            new PromptExtras { Mode = mode, Instructions = "Run tests after code changes." });

        request.SystemPrompt.Should().Contain("For greetings and casual conversation")
            .And.Contain("one short, natural sentence")
            .And.Contain("Do not introduce unrelated project details")
            .And.Contain("Do not call tools for greetings")
            .And.Contain("Project context is reference material, not a request to work on it")
            .And.Contain("Never invent file contents")
            .And.Contain("unless the user asks for more detail")
            .And.Contain("use `ask_question` if available")
            .And.Contain("Clarification is never permission to write or execute")
            .And.Contain("Run tests after code changes.");
        request.UserMessage.Should().EndWith("Hi");
    }

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
    public void Build_includes_durable_project_memory()
    {
        var prompt = CreateBuilder()
            .Build(
                Context("code"),
                [],
                "x",
                new PromptExtras
                {
                    Memory = [new ProjectMemoryDocument("status", ".codai/memory/status.md", "MARKER_MEMORY")],
                })
            .SystemPrompt;

        prompt.Should().Contain("## Here is durable project memory");
        prompt.Should().Contain(".codai/memory/status.md");
        prompt.Should().Contain("MARKER_MEMORY");
    }

    [Fact]
    public void Build_includes_the_project_rules_that_apply()
    {
        var prompt = CreateBuilder()
            .Build(Context("code"), [], "x", new PromptExtras { ProjectRules = "MARKER_RULES" })
            .SystemPrompt;

        prompt.Should().Contain("## Here are the project rules that apply to this request");
        prompt.Should().Contain("MARKER_RULES");
    }

    [Fact]
    public void An_empty_rules_block_does_not_add_a_section()
    {
        var prompt = CreateBuilder()
            .Build(Context("code"), [], "x", new PromptExtras { ProjectRules = string.Empty })
            .SystemPrompt;

        prompt.Should().NotContain("## Here are the project rules that apply to this request");
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
            new PromptExtras { Mode = AgentMode.Plan });

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
