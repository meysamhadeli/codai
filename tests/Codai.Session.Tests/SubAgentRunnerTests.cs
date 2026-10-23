using Codai.Configuration;
using Codai.Core.Abstractions;
using Codai.Core.Tokens;
using Codai.Core.Tools;
using Codai.Providers;
using Codai.Session.Tests.Fakes;
using FluentAssertions;

namespace Codai.Session.Tests;

/// <summary>
/// Covers the nested agent loop: what it is given, what it is not given, and what it hands
/// back. The whole value of the feature rests on the window boundary holding, so most of
/// these tests are about what stays out.
/// </summary>
public class SubAgentRunnerTests
{
    private const string Root = @"C:\project";

    /// <summary>Builds a runner over the given tools, with everything else faked.</summary>
    private static (SubAgentRunner Runner, FakeChatClientFactory Factory, TokenTracker Tokens, FakeUserInterface Ui) Create(
        IReadOnlyList<ITool> tools,
        int maxReportChars = SubAgentRunner.DefaultMaxReportChars,
        bool withUi = true)
    {
        var factory = new FakeChatClientFactory();
        var tokens = new TokenTracker();
        var ui = new FakeUserInterface();
        var fileSystem = new InMemoryFileSystem(Root);

        var runner = new SubAgentRunner(
            factory,
            ResolvedProviderOptions.From(new AiProviderOptions
            {
                Provider = "ollama",
                BaseUrl = "http://localhost:11434/api",
            }),
            new ToolRegistry(tools),
            fileSystem,
            tokens,
            withUi ? ui : null,
            Root,
            maxReportChars);

        return (runner, factory, tokens, ui);
    }

    private static SubAgentRequest Question() =>
        new("Where is the token validated?", "Look in the auth code.");

    [Fact]
    public void Naming_the_tool_lives_in_one_place()
    {
        // The tool that answers to the name and the filter that must exclude it read the
        // same constant, so a rename cannot leave a sub-agent able to start another.
        SubAgent.ToolName.Should().Be("task");
    }

    [Fact]
    public async Task A_sub_agent_is_given_only_the_read_only_tools()
    {
        var (runner, factory, _, _) = Create(
        [
            new StubTool("read_file"),
            new StubTool("write_file", isReadOnly: false),
            new StubTool("executor", isReadOnly: false),
        ]);

        factory.Service.Enqueue("Found it.");

        await runner.RunAsync(Question());

        factory.OfferedToolNames.Should().BeEquivalentTo(["read_file"]);
    }

    [Fact]
    public async Task A_sub_agent_is_never_offered_the_tool_that_starts_one()
    {
        // Otherwise a sub-agent could delegate in turn, and two levels of nested model
        // calls cost more than the investigation is worth.
        var (runner, factory, _, _) = Create(
        [
            new StubTool("read_file"),
            new StubTool(SubAgent.ToolName),
        ]);

        factory.Service.Enqueue("Found it.");

        await runner.RunAsync(Question());

        factory.OfferedToolNames.Should().BeEquivalentTo(["read_file"]);
    }

    [Fact]
    public async Task A_sub_agent_cannot_consume_foreground_user_input()
    {
        var (runner, factory, _, _) = Create([new StubTool("read_file"), new ForegroundInputTool()]);
        factory.Service.Enqueue("Found it.");

        await runner.RunAsync(Question());

        factory.OfferedToolNames.Should().BeEquivalentTo(["read_file"]);
    }

    private sealed class ForegroundInputTool : ToolBase
    {
        public override string Name => "foreground_input";
        public override string Description => "Requires user input";
        public override bool RequiresUserInput => true;
        public override string ParametersSchema => "{\"type\":\"object\"}";
        public override Task<ToolResult> InvokeAsync(System.Text.Json.JsonElement arguments, ToolContext context,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("Delegated tools must not read foreground input.");
    }

    [Fact]
    public async Task A_session_with_nothing_to_read_reports_that_instead_of_running()
    {
        // Running a model call with no tools would produce a confident answer built from
        // nothing, which is worse than no answer.
        var (runner, factory, _, _) = Create([new StubTool("write_file", isReadOnly: false)]);

        var result = await runner.RunAsync(Question());

        result.Succeeded.Should().BeFalse();
        result.Report.Should().Contain("no read-only tools");
        factory.Executors.Should().BeEmpty();
    }

    [Fact]
    public async Task The_sub_agent_does_not_receive_the_parents_conversation()
    {
        // The point of delegating is that the search does not accumulate here. Sending the
        // parent's context along would move the cost rather than avoid it.
        var (runner, factory, _, _) = Create([new StubTool("read_file")]);

        factory.Service.Enqueue("Found it.");

        await runner.RunAsync(Question());

        var request = factory.Service.Requests.Should().ContainSingle().Subject;
        request.UserMessage.Should().Contain("Where is the token validated?");
        request.UserMessage.Should().Contain("Look in the auth code.");
        request.SystemPrompt.Should().NotContain(Root);
    }

    [Fact]
    public async Task The_sub_agent_is_told_its_role_and_its_rules()
    {
        var (runner, factory, _, _) = Create([new StubTool("read_file")]);

        factory.Service.Enqueue("Found it.");

        await runner.RunAsync(Question());

        factory.Service.Requests[0].SystemPrompt
            .Should().Contain("You are a sub-agent")
            .And.Contain("Never guess at what a file")
            .And.Contain("- read_file: Stub read_file. (required: path)");
    }

    [Fact]
    public async Task The_mode_does_not_restrict_a_sub_agent()
    {
        // A mode describes what the user asked for in this conversation. The sub-agent is
        // already bounded by its read-only tool set, so narrowing it further would stop it
        // reading the very files it was sent to read.
        var (runner, factory, _, _) = Create([new StubTool("read_file")]);

        factory.Service.Enqueue("Found it.");

        await runner.RunAsync(Question());

        factory.Service.Requests[0].AllowedTools.Should().BeNull();
    }

    [Fact]
    public async Task The_report_says_it_came_from_a_sub_agent_and_was_read_only()
    {
        // The parent model has to know it is reading a summary, and that nobody checked
        // the files were left alone by anything other than the tool filter.
        var (runner, factory, _, _) = Create([new StubTool("read_file")]);

        factory.Service.Enqueue("It is validated in TokenValidator.cs:42.");

        var result = await runner.RunAsync(Question());

        result.Succeeded.Should().BeTrue();
        result.Report
            .Should().StartWith("# Report from a sub-agent")
            .And.Contain("TokenValidator.cs:42")
            .And.Contain("it cannot have changed any file");
    }

    [Fact]
    public async Task An_empty_report_is_a_failure()
    {
        // Handing back an empty string would read to the parent as "nothing to report",
        // which is a claim the sub-agent never made.
        var (runner, factory, _, _) = Create([new StubTool("read_file")]);

        var result = await runner.RunAsync(Question());

        result.Succeeded.Should().BeFalse();
        result.Report.Should().Contain("nothing usable");
    }

    [Fact]
    public async Task A_Long_report_is_cut_and_the_cut_is_admitted()
    {
        var (runner, factory, _, _) = Create([new StubTool("read_file")], maxReportChars: 50);

        factory.Service.Enqueue(new string('x', 500));

        var result = await runner.RunAsync(Question());

        result.Report.Should().Contain("truncated after 50 characters");
        result.Report.Should().Contain(new string('x', 50));
        result.Report.Should().NotContain(new string('x', 51));
    }

    [Fact]
    public async Task A_provider_failure_becomes_a_report_the_parent_can_act_on()
    {
        var (runner, factory, _, _) = Create([new StubTool("read_file")]);

        factory.Service.Failures.Enqueue(new ChatProviderException("rate limited"));

        var result = await runner.RunAsync(Question());

        result.Succeeded.Should().BeFalse();
        result.Report.Should().Contain("rate limited");
        result.Report.Should().Contain("Continue without it");
    }

    [Fact]
    public async Task What_a_failed_run_spent_is_still_counted()
    {
        // The tokens were spent whether or not a report came back, and a cost report that
        // hides a failed run is a lie.
        var (runner, factory, tokens, _) = Create([new StubTool("read_file")]);

        factory.Service.EnqueueThenFail(
            "I was halfway through...",
            new TokenUsage(400, 60),
            new ChatProviderException("rate limited"));

        var result = await runner.RunAsync(Question());

        result.Succeeded.Should().BeFalse();
        tokens.LastUsage.InputTokens.Should().Be(400);
        tokens.SessionUsage.TotalTokens.Should().Be(460);
    }

    [Fact]
    public async Task What_a_run_spent_is_added_to_the_sessions_totals()
    {
        var (runner, factory, tokens, _) = Create([new StubTool("read_file")]);

        factory.Service.Enqueue("Found it.", new TokenUsage(120, 30));

        await runner.RunAsync(Question());

        tokens.LastUsage.InputTokens.Should().Be(120);
        tokens.SessionUsage.TotalTokens.Should().Be(150);
    }

    [Fact]
    public async Task The_run_is_labelled_while_it_happens()
    {
        // A sub-agent that shows nothing for a minute looks like a hang, and the label is
        // the only thing telling the user who is working.
        var (runner, factory, _, ui) = Create([new StubTool("read_file")]);

        factory.Service.Enqueue("Found it.");

        await runner.RunAsync(Question());

        ui.ProgressLabels.Should().ContainSingle()
            .Which.Should().Contain("Where is the token validated?");
    }

    [Fact]
    public async Task A_host_with_no_user_interface_still_runs_the_work()
    {
        var (runner, factory, _, _) = Create([new StubTool("read_file")], withUi: false);

        factory.Service.Enqueue("Found it.");

        var result = await runner.RunAsync(Question());

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void The_run_is_governed_by_a_budget_of_its_own()
    {
        // A sub-agent that needs a user request's whole allowance has misunderstood the
        // question. The bound is what keeps a delegated investigation from quietly
        // becoming a second session.
        SubAgentRunner.DefaultMaxToolCalls.Should().BeLessThan(ToolCallGovernor.DefaultMaxCalls);
    }
}
