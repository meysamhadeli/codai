using Codai.Configuration;
using Codai.Core.Chat;
using Codai.Core.IO;
using Codai.Core.Tools;
using Codai.Mcp.Client;
using Codai.Providers.Tests.Fakes;
using FluentAssertions;
using Microsoft.Extensions.AI;

namespace Codai.Providers.Tests;

/// <summary>
/// Verifies that advertising tools drives the whole call loop: the model asks for a
/// tool, the executor runs it, and the result is fed back so the model can answer.
/// </summary>
public class ToolCallingTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Tool_rounds_compact_old_payloads_without_losing_call_ids_or_latest_evidence(bool stream)
    {
        var client = new ToolCallingChatClient(
            ToolCallingChatClient.CallsTool("first", "read_file", new Dictionary<string, object?> { ["pattern"] = "first.cs" }),
            ToolCallingChatClient.CallsTool("second", "read_file", new Dictionary<string, object?> { ["pattern"] = "second.cs" }),
            ToolCallingChatClient.Says("Answer from latest evidence."));
        var output = "BEGIN " + new string('x', 260_000) + " END";
        var executor = new RecordingToolExecutor(new StubTool("read_file")) { Outcome = ToolResult.Success(output) };

        await DrainAsync(new ChatCompletionService(client, Options(stream), executor));

        var results = client.SeenMessages[2].SelectMany(message => message.Contents).OfType<FunctionResultContent>().ToArray();
        results.Select(result => result.CallId).Should().Equal("first", "second");
        results[0].Result.Should().BeOfType<string>().Which.Should().Contain("Earlier tool output omitted");
        results[1].Result.Should().Be(output);
        client.SeenMessages[1].SelectMany(message => message.Contents).OfType<FunctionResultContent>()
            .Single().Result.Should().Be(output);
        executor.Calls.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task Latest_tool_payload_respects_lower_window_cap_and_explicit_compaction_disable(bool stream, bool compact)
    {
        var client = new ToolCallingChatClient(
            ToolCallingChatClient.CallsTool("read", "read_file", new Dictionary<string, object?> { ["pattern"] = "large.cs" }),
            ToolCallingChatClient.Says("Evidence-qualified answer."));
        var output = "BEGIN " + new string('x', 260_000) + " END";
        var executor = new RecordingToolExecutor(new StubTool("read_file")) { Outcome = ToolResult.Success(output) };
        var service = new ChatCompletionService(client, Options(stream), executor);
        await foreach (var update in service.StreamAsync(new ChatRequest("system", "user") { AutoCompact = compact, ContextWindowTokens = 32_000 }))
        {
        }

        var result = client.SeenMessages[1].SelectMany(message => message.Contents).OfType<FunctionResultContent>()
            .Single().Result.Should().BeOfType<string>().Subject;
        result.Should().StartWith("BEGIN").And.EndWith("END");
        if (compact)
        {
            result.Length.Should().BeLessThan(32_000);
            result.Should().Contain("Tool output compacted");
        }
        else
        {
            result.Should().Be(output);
        }
    }

    [Fact]
    public async Task A_multi_result_batch_stops_compacting_once_it_fits()
    {
        var client = new ToolCallingChatClient(ToolCallingChatClient.Says("Answer"));
        var provider = Options();
        var budgeted = new ContextBudgetChatClient(client, provider, 0);
        var output = new string('x', 260_000);
        ChatMessage[] messages = [new(ChatRole.System, "system"), new(ChatRole.User, "user"),
            new(ChatRole.Assistant, [new FunctionCallContent("first", "read_file"), new FunctionCallContent("second", "read_file")]),
            new(ChatRole.Tool, [new FunctionResultContent("first", output), new FunctionResultContent("second", output)])];

        await budgeted.GetResponseAsync(messages);

        var results = client.SeenMessages[0].SelectMany(message => message.Contents).OfType<FunctionResultContent>().ToArray();
        results[0].Result.Should().BeOfType<string>().Which.Should().Contain("Tool output compacted");
        results[1].Result.Should().Be(output);
    }

    [Theory]
    [InlineData("read_skill", "Skill instructions: ")]
    [InlineData("read_file", "Tool was not executed. Review newly applicable rules. ")]
    public async Task Tool_compaction_preserves_skill_instructions_and_required_rule_review(string name, string prefix)
    {
        var client = new ToolCallingChatClient(
            ToolCallingChatClient.CallsTool("guidance", name, new Dictionary<string, object?> { ["pattern"] = "guidance" }),
            ToolCallingChatClient.CallsTool("evidence", "grep", new Dictionary<string, object?> { ["pattern"] = "implementation" }),
            ToolCallingChatClient.Says("Answer respecting loaded guidance."));
        var instructions = prefix + new string('x', 260_000);
        var executor = new RecordingToolExecutor(new StubTool(name), new StubTool("grep"));
        executor.Outcomes.Enqueue(ToolResult.Success(instructions));
        executor.Outcomes.Enqueue(ToolResult.Success(new string('y', 260_000)));

        await DrainAsync(new ChatCompletionService(client, Options(), executor));

        var results = client.SeenMessages[2].SelectMany(message => message.Contents).OfType<FunctionResultContent>().ToArray();
        results[0].Result.Should().Be(instructions);
        results[1].Result.Should().BeOfType<string>().Which.Should().Contain("Tool output compacted");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Documentation_research_discovers_tools_resolves_identifier_and_queries_docs_without_unchecked_search(bool stream)
    {
        const string libraryId = "/fixture/product";
        var calls = new List<string>();
        var resolver = new McpToolAdapter(new McpToolDefinition("mcp_resolve", "Resolve product library for documentation",
            "{\"type\":\"object\",\"properties\":{\"libraryName\":{\"type\":\"string\"}},\"required\":[\"libraryName\"],\"additionalProperties\":false}",
            true, false, (arguments, _, _) =>
            {
                arguments.GetProperty("libraryName").GetString().Should().Be("fixture-product");
                calls.Add("resolve");
                return Task.FromResult(libraryId);
            }));
        var docs = new McpToolAdapter(new McpToolDefinition("mcp_docs", "Query product library documentation",
            "{\"type\":\"object\",\"properties\":{\"libraryId\":{\"type\":\"string\"},\"query\":{\"type\":\"string\"}},\"required\":[\"libraryId\",\"query\"],\"additionalProperties\":false}",
            true, false, (arguments, _, _) =>
            {
                arguments.GetProperty("libraryId").GetString().Should().Be(libraryId);
                arguments.GetProperty("query").GetString().Should().Be("correlation header");
                calls.Add("docs");
                return Task.FromResult("Verified fixture documentation. Source: https://docs.example.com/product/correlation");
            }));
        var runner = new ToolRunner(new ToolRegistry([resolver, docs, McpTool("mcp_unchecked_search", "UNCHECKED_SEARCH_DESCRIPTION")]),
            DenyAllToolApprover.Instance, NullToolObserver.Instance, new PhysicalFileSystem(), Path.GetTempPath());
        var client = new ToolCallingChatClient(
            ToolCallingChatClient.CallsTool("discover", "tool_search", new Dictionary<string, object?> { ["query"] = "product documentation" }),
            ToolCallingChatClient.CallsTool("resolve", "mcp_resolve", new Dictionary<string, object?> { ["libraryName"] = "fixture-product" }),
            ToolCallingChatClient.CallsTool("docs", "mcp_docs", new Dictionary<string, object?> { ["libraryId"] = libraryId, ["query"] = "correlation header" }),
            ToolCallingChatClient.Says("Evidence-qualified report."));
        var service = new ChatCompletionService(client, Options(stream), runner);

        await foreach (var update in service.StreamAsync(new ChatRequest("Research available product documentation.", "Explain correlation headers", ["mcp_resolve", "mcp_docs"])))
        {
        }

        calls.Should().Equal("resolve", "docs");
        client.SeenToolNames[0].Should().Equal("tool_search");
        client.SeenToolNames.Should().OnlyContain(names => !names.Contains("mcp_unchecked_search"));
        client.SeenMessages[3].SelectMany(message => message.Contents).OfType<FunctionResultContent>()
            .Single(result => result.CallId == "docs").Result.Should().BeOfType<string>()
            .Which.Should().Contain("https://docs.example.com/product/correlation");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Mcp_search_loads_relevant_schemas_then_executes_through_existing_executor(bool stream)
    {
        var client = new ToolCallingChatClient(
            ToolCallingChatClient.CallsTool("discover", "tool_search", new Dictionary<string, object?> { ["query"] = "React documentation" }),
            ToolCallingChatClient.CallsTool("lookup", "mcp_docs", new Dictionary<string, object?> { ["pattern"] = "React" }),
            ToolCallingChatClient.Says("Documentation found."));
        var executor = new RecordingToolExecutor(new StubTool("grep"), McpTool("mcp_docs", "React documentation"),
            McpTool("mcp_database", "Query customer records"));

        await DrainAsync(new ChatCompletionService(client, Options(stream), executor));

        client.SeenToolNames[0].Should().Equal("grep", "tool_search");
        client.SeenToolNames[1].Should().Equal("grep", "tool_search", "mcp_docs");
        executor.Calls.Should().ContainSingle().Which.Name.Should().Be("mcp_docs");
        var discovery = client.SeenMessages[1].SelectMany(message => message.Contents)
            .OfType<FunctionResultContent>().Single().Result.Should().BeOfType<string>().Subject;
        discovery.Should().Contain("inputSchema").And.Contain("pattern").And.NotContain("mcp_database");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Mcp_search_cannot_discover_tools_excluded_from_the_request(bool stream)
    {
        var client = new ToolCallingChatClient(
            ToolCallingChatClient.CallsTool("discover", "tool_search", new Dictionary<string, object?> { ["query"] = "mcp_secret" }),
            ToolCallingChatClient.Says("Unavailable."));
        var executor = new RecordingToolExecutor(McpTool("mcp_docs", "Documentation"), McpTool("mcp_secret", "PRIVATE_DESCRIPTION"));
        var service = new ChatCompletionService(client, Options(stream), executor);

        await foreach (var update in service.StreamAsync(new ChatRequest("system", "user", ["mcp_docs"])))
        {
        }

        client.SeenToolNames.Should().OnlyContain(names => names.Length == 1 && names[0] == "tool_search");
        client.LastOptions!.Tools!.Single().Description.Should().NotContain("mcp_secret").And.NotContain("PRIVATE_DESCRIPTION");
        client.SeenMessages[1].SelectMany(message => message.Contents).OfType<FunctionResultContent>()
            .Single().Result.Should().BeOfType<string>().Which.Should().Contain("No matching available MCP tools");
        executor.Calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Mcp_discovery_deduplicates_schemas_and_resets_between_requests(bool stream)
    {
        var client = new ToolCallingChatClient(
            ToolCallingChatClient.CallsTool("first", "tool_search", new Dictionary<string, object?> { ["query"] = "mcp_docs" }),
            ToolCallingChatClient.CallsTool("again", "tool_search", new Dictionary<string, object?> { ["query"] = "mcp_docs" }),
            ToolCallingChatClient.Says("done"), ToolCallingChatClient.Says("next request"));
        var executor = new RecordingToolExecutor(McpTool("mcp_docs", "Documentation"));
        var service = new ChatCompletionService(client, Options(stream), executor);

        await DrainAsync(service);
        await DrainAsync(service);

        client.SeenToolNames[2].Should().Equal("tool_search", "mcp_docs");
        client.SeenToolNames[3].Should().Equal("tool_search");
        executor.Calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Mcp_discovery_rejects_empty_queries_without_loading_or_running_tools(string query)
    {
        var client = new ToolCallingChatClient(
            ToolCallingChatClient.CallsTool("invalid", "tool_search", new Dictionary<string, object?> { ["query"] = query }),
            ToolCallingChatClient.Says("done"));
        var executor = new RecordingToolExecutor(McpTool("mcp_docs", "Documentation"));

        await DrainAsync(new ChatCompletionService(client, Options(), executor));

        client.SeenToolNames[1].Should().Equal("tool_search");
        executor.Calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Discovering_an_mcp_schema_does_not_grant_tool_approval(bool stream)
    {
        var invocations = 0;
        var tool = new McpToolAdapter(new McpToolDefinition("mcp_action", "External action",
            new StubTool("mcp_action").ParametersSchema, false, true, (_, _, _) =>
            {
                invocations++;
                return Task.FromResult("must not run");
            }));
        var runner = new ToolRunner(new ToolRegistry([tool]), DenyAllToolApprover.Instance,
            NullToolObserver.Instance, new PhysicalFileSystem(), Path.GetTempPath());
        var client = new ToolCallingChatClient(
            ToolCallingChatClient.CallsTool("discover", "tool_search", new Dictionary<string, object?> { ["query"] = "mcp_action" }),
            ToolCallingChatClient.CallsTool("action", "mcp_action", new Dictionary<string, object?> { ["pattern"] = "change" }),
            ToolCallingChatClient.Says("Permission declined."));

        await DrainAsync(new ChatCompletionService(client, Options(stream), runner));

        invocations.Should().Be(0);
        client.SeenMessages[2].SelectMany(message => message.Contents).OfType<FunctionResultContent>()
            .Single(result => result.CallId == "action").Result.Should().BeOfType<string>()
            .Which.Should().Contain("The user declined");
    }

    [Fact]
    public async Task Empty_allowlist_does_not_offer_mcp_discovery()
    {
        var client = new ToolCallingChatClient(ToolCallingChatClient.Says("Summary"));
        var service = new ChatCompletionService(client, Options(), new RecordingToolExecutor(McpTool("mcp_docs", "Documentation")));

        await foreach (var update in service.StreamAsync(new ChatRequest("system", "summary", [])))
        {
        }

        client.SeenToolNames.Should().ContainSingle().Which.Should().BeEmpty();
    }

    [Fact]
    public async Task Existing_tool_search_name_preserves_registered_tools_without_collision()
    {
        var client = new ToolCallingChatClient(ToolCallingChatClient.Says("done"));
        var executor = new RecordingToolExecutor(new StubTool("tool_search"), McpTool("mcp_docs", "Documentation"));

        await DrainAsync(new ChatCompletionService(client, Options(), executor));

        client.SeenToolNames[0].Should().Equal("tool_search", "mcp_docs");
        client.LastOptions!.Tools![0].Description.Should().Be("Stub tool_search.");
    }

    [Fact]
    public async Task Mcp_discovery_bounds_results_queries_and_repeated_searches()
    {
        var deferred = Enumerable.Range(0, 8).Select(index => (AIFunction)new Codai.Providers.Tools.ToolAIFunction(
            McpTool($"mcp_docs_{index}", "Documentation"), new RecordingToolExecutor())).ToArray();
        var selected = new List<AITool>();
        var search = new Codai.Providers.Tools.ToolSearchAIFunction(deferred, selected);

        var oversized = await search.InvokeAsync(new AIFunctionArguments { ["query"] = new string('x', 513) });
        oversized.Should().BeOfType<string>().Which.Should().Contain("1 to 512 characters");
        selected.Should().BeEmpty();
        await search.InvokeAsync(new AIFunctionArguments { ["query"] = "documentation" });
        selected.Should().HaveCount(5);
        for (var iteration = 0; iteration < 10; iteration++)
        {
            await search.InvokeAsync(new AIFunctionArguments { ["query"] = "documentation" });
        }
        var limited = await search.InvokeAsync(new AIFunctionArguments { ["query"] = "documentation" });
        limited.Should().BeOfType<string>().Which.Should().Contain("discovery limit reached");
        selected.Should().HaveCount(5);
    }

    private static McpToolAdapter McpTool(string name, string description) => new(new McpToolDefinition(
        name, description, new StubTool(name).ParametersSchema, true, false,
        (_, _, _) => Task.FromResult("Documentation")));

    private static ResolvedProviderOptions Options(bool stream = true) =>
        ResolvedProviderOptions.From(new AiProviderOptions
        {
            Provider = "openai",
            Model = "gpt-4o",
            ApiKey = "test-key",
            Stream = stream,
        });

    private static async Task<List<ChatStreamUpdate>> DrainAsync(ChatCompletionService service)
    {
        var updates = new List<ChatStreamUpdate>();
        await foreach (var update in service.StreamAsync(new ChatRequest("system", "user")))
        {
            updates.Add(update);
        }

        return updates;
    }

    [Fact]
    public async Task Advertises_the_executor_tools_to_the_provider()
    {
        var client = new ToolCallingChatClient(ToolCallingChatClient.Says("hi"));
        var executor = new RecordingToolExecutor(new StubTool("grep"), new StubTool("glob"));
        var service = new ChatCompletionService(client, Options(), executor);

        await DrainAsync(service);

        client.LastOptions!.Tools.Should().NotBeNull();
        client.LastOptions.Tools!.Select(tool => tool.Name).Should().Equal("grep", "glob");
    }

    [Fact]
    public async Task Offers_no_tools_when_there_is_no_executor()
    {
        var client = new ToolCallingChatClient(ToolCallingChatClient.Says("hi"));
        var service = new ChatCompletionService(client, Options());

        await DrainAsync(service);

        // No executor means no tools are advertised, which is observable on the request.
        client.LastOptions!.Tools.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task Advertised_tool_carries_its_description_and_schema()
    {
        var client = new ToolCallingChatClient(ToolCallingChatClient.Says("hi"));
        var service = new ChatCompletionService(
            client,
            Options(),
            new RecordingToolExecutor(new StubTool("grep")));

        await DrainAsync(service);

        var tool = client.LastOptions!.Tools!.Single().Should().BeAssignableTo<AIFunction>().Subject;
        tool.Description.Should().Be("Stub grep.");
        tool.JsonSchema.GetProperty("required")[0].GetString().Should().Be("pattern");
    }

    [Fact]
    public async Task Runs_the_requested_tool_and_answers_with_its_result()
    {
        var client = new ToolCallingChatClient(
            ToolCallingChatClient.CallsTool("call-1", "grep", new Dictionary<string, object?> { ["pattern"] = "TODO" }),
            ToolCallingChatClient.Says("Found nothing."));

        var executor = new RecordingToolExecutor(new StubTool("grep"));
        var service = new ChatCompletionService(client, Options(), executor);

        var updates = await DrainAsync(service);

        var call = executor.Calls.Should().ContainSingle().Subject;
        call.Name.Should().Be("grep");
        call.Arguments.GetProperty("pattern").GetString().Should().Be("TODO");

        updates.Select(u => u.ContentDelta).Where(d => d is not null)
            .Should().Equal("Found nothing.");
        updates[^1].IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task Feeds_the_tool_output_back_as_a_tool_message()
    {
        var client = new ToolCallingChatClient(
            ToolCallingChatClient.CallsTool("call-1", "grep", new Dictionary<string, object?> { ["pattern"] = "TODO" }),
            ToolCallingChatClient.Says("done"));

        var executor = new RecordingToolExecutor(new StubTool("grep"))
        {
            Outcome = ToolResult.Success("src/a.cs:2: // TODO"),
        };

        var service = new ChatCompletionService(client, Options(), executor);

        await DrainAsync(service);

        client.CallCount.Should().Be(2);

        var result = client.SeenMessages[1]
            .Where(message => message.Role == ChatRole.Tool)
            .SelectMany(message => message.Contents)
            .OfType<FunctionResultContent>()
            .Should().ContainSingle().Subject;

        result.CallId.Should().Be("call-1");
        result.Result.Should().Be("src/a.cs:2: // TODO");
    }

    [Fact]
    public async Task A_failed_tool_is_reported_to_the_model_as_text()
    {
        var client = new ToolCallingChatClient(
            ToolCallingChatClient.CallsTool("call-1", "read_file", new Dictionary<string, object?> { ["path"] = "nope" }),
            ToolCallingChatClient.Says("The file is missing."));

        var executor = new RecordingToolExecutor(new StubTool("read_file"))
        {
            Outcome = ToolResult.Failure("File 'nope' does not exist."),
        };

        var service = new ChatCompletionService(client, Options(), executor);

        await DrainAsync(service);

        var result = client.SeenMessages[1]
            .SelectMany(message => message.Contents)
            .OfType<FunctionResultContent>()
            .Single();

        result.Result.Should().Be("File 'nope' does not exist.");
        client.CallCount.Should().Be(2);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Real_runner_returns_validation_feedback_and_executes_corrected_call(bool stream)
    {
        var client = new ToolCallingChatClient(
            ToolCallingChatClient.CallsTool("invalid", "grep", new Dictionary<string, object?> { ["pattern"] = 42 }),
            ToolCallingChatClient.CallsTool("corrected", "grep", new Dictionary<string, object?> { ["pattern"] = "TODO" }),
            ToolCallingChatClient.Says("Found TODO."));
        var tool = new StubTool("grep", schema: StubTool.RichSchema);
        var runner = new ToolRunner(new ToolRegistry([tool]), DenyAllToolApprover.Instance,
            NullToolObserver.Instance, new PhysicalFileSystem(), Path.GetTempPath());

        var updates = await DrainAsync(new ChatCompletionService(client, Options(stream), runner));

        tool.Invocations.Should().Be(1);
        client.CallCount.Should().Be(3);
        var feedback = client.SeenMessages[1].SelectMany(message => message.Contents)
            .OfType<FunctionResultContent>().Single();
        feedback.CallId.Should().Be("invalid");
        feedback.Result.Should().BeOfType<string>().Which.Should().Contain("Invalid arguments").And.Contain("/pattern");
        updates.Select(update => update.ContentDelta).Should().Contain("Found TODO.");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Repeated_invalid_calls_stop_before_any_tool_runs(bool stream)
    {
        var client = new ToolCallingChatClient(
            ToolCallingChatClient.CallsTool("invalid-1", "grep", new Dictionary<string, object?> { ["pattern"] = 1 }),
            ToolCallingChatClient.CallsTool("invalid-2", "grep", new Dictionary<string, object?> { ["pattern"] = 2 }),
            ToolCallingChatClient.Says("must not continue"));
        var tool = new StubTool("grep");
        var runner = new ToolRunner(new ToolRegistry([tool]), DenyAllToolApprover.Instance,
            NullToolObserver.Instance, new PhysicalFileSystem(), Path.GetTempPath(),
            governor: new ToolCallGovernor { MaxConsecutiveFailures = 2 });
        var service = new ChatCompletionService(client, Options(stream), runner);

        Func<Task> act = async () => await DrainAsync(service);

        await act.Should().ThrowAsync<Codai.Core.Abstractions.ChatProviderException>()
            .WithMessage("*Invalid arguments*2 consecutive failures*");
        tool.Invocations.Should().Be(0);
        client.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task Failure_stop_does_not_leak_into_the_next_request()
    {
        var client = new ToolCallingChatClient(
            ToolCallingChatClient.CallsTool("call-1", "grep", new Dictionary<string, object?> { ["pattern"] = "TODO" }),
            ToolCallingChatClient.Says("next request succeeded"));
        var executor = new RecordingToolExecutor(new StubTool("grep"))
        {
            Outcome = ToolResult.Failure("limit reached") with { StopsExecution = true },
        };
        var service = new ChatCompletionService(client, Options(), executor);

        Func<Task> act = async () => await DrainAsync(service);
        await act.Should().ThrowAsync<Codai.Core.Abstractions.ChatProviderException>();

        var updates = await DrainAsync(service);

        updates.Select(update => update.ContentDelta).Should().Contain("next request succeeded");
        client.CallCount.Should().Be(2);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Real_runner_stops_after_limit_and_skips_remaining_batch_calls(bool stream)
    {
        var client = new ToolCallingChatClient(
            ToolCallingChatClient.CallsTool("call-1", "grep", new Dictionary<string, object?> { ["pattern"] = "first" }),
            [
                new ChatResponseUpdate(ChatRole.Assistant, [new FunctionCallContent("call-2", "grep", new Dictionary<string, object?> { ["pattern"] = "second" })]),
                new ChatResponseUpdate(ChatRole.Assistant, [new FunctionCallContent("call-3", "grep", new Dictionary<string, object?> { ["pattern"] = "third" })]),
            ],
            ToolCallingChatClient.Says("must not continue"));
        var tool = new StubTool("grep") { Outcome = ToolResult.Failure("search failed") };
        var runner = new ToolRunner(
            new ToolRegistry([tool]),
            DenyAllToolApprover.Instance,
            NullToolObserver.Instance,
            new PhysicalFileSystem(),
            Path.GetTempPath(),
            governor: new ToolCallGovernor { MaxConsecutiveFailures = 2 });
        var service = new ChatCompletionService(client, Options(stream), runner);

        Func<Task> act = async () => await DrainAsync(service);
        await act.Should().ThrowAsync<Codai.Core.Abstractions.ChatProviderException>()
            .WithMessage("*search failed*2 consecutive failures*");

        client.CallCount.Should().Be(2);
        tool.Invocations.Should().Be(2);
    }

    [Fact]
    public async Task Terminal_tool_failure_stops_the_model_loop()
    {
        var client = new ToolCallingChatClient(
            ToolCallingChatClient.CallsTool("call-1", "grep", new Dictionary<string, object?> { ["pattern"] = "TODO" }),
            ToolCallingChatClient.Says("must not continue"));
        var executor = new RecordingToolExecutor(new StubTool("grep"))
        {
            Outcome = ToolResult.Failure("Tool failure limit reached.") with { StopsExecution = true },
        };
        var service = new ChatCompletionService(client, Options(), executor);

        Func<Task> act = async () => await DrainAsync(service);
        await act.Should().ThrowAsync<Codai.Core.Abstractions.ChatProviderException>()
            .WithMessage("Tool failure limit reached.");

        client.CallCount.Should().Be(1);
        executor.Calls.Should().ContainSingle();
    }

    [Fact]
    public async Task Terminal_failure_also_stops_non_streaming_requests()
    {
        var client = new ToolCallingChatClient(
            ToolCallingChatClient.CallsTool("call-1", "grep", new Dictionary<string, object?> { ["pattern"] = "TODO" }),
            ToolCallingChatClient.Says("must not continue"));
        var executor = new RecordingToolExecutor(new StubTool("grep"))
        {
            Outcome = ToolResult.Failure("Tool failure limit reached.") with { StopsExecution = true },
        };
        var service = new ChatCompletionService(client, Options(stream: false), executor);

        Func<Task> act = async () => await DrainAsync(service);
        await act.Should().ThrowAsync<Codai.Core.Abstractions.ChatProviderException>()
            .WithMessage("Tool failure limit reached.");

        client.CallCount.Should().Be(1);
        executor.Calls.Should().ContainSingle();
    }

    [Fact]
    public async Task The_non_streaming_path_also_runs_the_loop()
    {
        var client = new ToolCallingChatClient(
            ToolCallingChatClient.CallsTool("call-1", "grep", new Dictionary<string, object?> { ["pattern"] = "TODO" }),
            ToolCallingChatClient.Says("Found it."));

        var executor = new RecordingToolExecutor(new StubTool("grep"));
        var service = new ChatCompletionService(client, Options(stream: false), executor);

        var updates = await DrainAsync(service);

        executor.Calls.Should().HaveCount(1);
        updates.Select(u => u.ContentDelta).Where(d => d is not null).Should().Equal("Found it.");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Successful_terminal_tool_submits_content_without_another_model_round(bool stream)
    {
        var client = new ToolCallingChatClient(
            ToolCallingChatClient.CallsTool("finish", "submit_task", new Dictionary<string, object?> { ["result"] = "Finished." }),
            ToolCallingChatClient.Says("must not continue"));
        var executor = new RecordingToolExecutor(new StubTool("submit_task"))
        {
            Outcome = ToolResult.Success("Finished.") with { StopsExecution = true },
        };
        var service = new ChatCompletionService(client, Options(stream), executor);

        var updates = await DrainAsync(service);

        updates.Select(update => update.ContentDelta).Where(delta => delta is not null).Should().Equal("Finished.");
        client.CallCount.Should().Be(1);
        executor.Calls.Should().ContainSingle();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Submission_skips_remaining_batch_tools_and_does_not_leak_to_next_request(bool stream)
    {
        var client = new ToolCallingChatClient(
            [
                new ChatResponseUpdate(ChatRole.Assistant, [new FunctionCallContent("finish", "submit_task", new Dictionary<string, object?> { ["result"] = "Finished." })]),
                new ChatResponseUpdate(ChatRole.Assistant, [new FunctionCallContent("later", "grep", new Dictionary<string, object?> { ["pattern"] = "must not run" })]),
            ],
            ToolCallingChatClient.Says("next request"));
        var executor = new RecordingToolExecutor(new StubTool("submit_task"), new StubTool("grep"))
        {
            Outcome = ToolResult.Success("Finished.") with { StopsExecution = true },
        };
        var service = new ChatCompletionService(client, Options(stream), executor);

        await DrainAsync(service);
        executor.Calls.Should().ContainSingle();
        var next = await DrainAsync(service);

        next.Select(update => update.ContentDelta).Should().Contain("next request");
        client.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task Several_tool_calls_in_one_turn_all_run()
    {
        var client = new ToolCallingChatClient(
            [
                new ChatResponseUpdate(ChatRole.Assistant, [new FunctionCallContent("c1", "grep", new Dictionary<string, object?> { ["pattern"] = "a" })]),
                new ChatResponseUpdate(ChatRole.Assistant, [new FunctionCallContent("c2", "glob", new Dictionary<string, object?> { ["pattern"] = "*.cs" })]),
            ],
            ToolCallingChatClient.Says("done"));

        var executor = new RecordingToolExecutor(new StubTool("grep"), new StubTool("glob"));
        var service = new ChatCompletionService(client, Options(), executor);

        await DrainAsync(service);

        executor.Calls.Should().HaveCount(2);
    }

    [Fact]
    public async Task The_loop_stops_when_the_model_stops_asking_for_tools()
    {
        var client = new ToolCallingChatClient(ToolCallingChatClient.Says("nothing to do"));
        var executor = new RecordingToolExecutor(new StubTool("grep"));
        var service = new ChatCompletionService(client, Options(), executor);

        await DrainAsync(service);

        client.CallCount.Should().Be(1);
        executor.Calls.Should().BeEmpty();
    }
}
