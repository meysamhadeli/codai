using Codai.Core.Agents;
using Codai.Core.Tokens;
using Codai.Session.Tests.Fakes;
using FluentAssertions;

namespace Codai.Session.Tests;

public class SlashCommandTests
{
    [Theory]
    [InlineData("/help", SlashCommand.Help)]
    [InlineData("/preferences theme", SlashCommand.Settings)]
    [InlineData("/session history", SlashCommand.Session)]
    [InlineData(" /mode plan ", SlashCommand.Mode)]
    public void Forward_slash_commands_share_existing_handlers(string input, SlashCommand expected)
    {
        SlashCommandParser.Parse(input).Should().Be(expected);
    }

    [Fact]
    public async Task Forward_slash_commands_execute_locally_without_model_requests()
    {
        var harness = new SessionHarness();
        harness.QueueInputs("/mode plan", "/config session", "/exit");
        (await harness.Build().RunAsync()).Should().Be(0);
        harness.Chat.Requests.Should().BeEmpty();
        harness.Ui.Lines.Should().Contain(line => line.Contains("mode: plan", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Config_skills_and_workflows_list_metadata_not_executable_bodies()
    {
        var harness = new SessionHarness();
        harness.FileSystem.AddFile(Path.Combine(harness.Root, ".codai", "skills", "testing", "SKILL.md"),
            "---\nname: testing\ndescription: Test guidance\n---\nSecret skill body.");
        harness.FileSystem.AddFile(Path.Combine(harness.Root, ".codai", "commands", "review.md"),
            "---\ndescription: Review guidance\n---\nSecret workflow body.");
        harness.QueueInputs(":config skills", ":config workflows", ":exit");

        (await harness.Build().RunAsync()).Should().Be(0);

        harness.Chat.Requests.Should().BeEmpty();
        var lines = string.Join('\n', harness.Ui.Lines);
        lines.Should().Contain("Test guidance").And.Contain("Review guidance").And.NotContain("Secret");
    }

    [Theory]
    [InlineData(Codai.Application.Inspection.ConfigurationTarget.Model)]
    [InlineData(Codai.Application.Inspection.ConfigurationTarget.Tools)]
    [InlineData(Codai.Application.Inspection.ConfigurationTarget.Hooks)]
    public async Task Config_inspector_honors_cancellation_for_every_kind_of_target(
        Codai.Application.Inspection.ConfigurationTarget target)
    {
        var harness = new SessionHarness();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        Func<Task> inspect = () => harness.Inspector.InspectAsync(target, cancellation.Token);

        await inspect.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Config_reads_current_mode_and_tools_without_sending_a_model_request()
    {
        var harness = new SessionHarness();
        harness.QueueInputs(":mode plan", ":config session", ":config tools", ":exit");

        (await harness.Build().RunAsync()).Should().Be(0);

        harness.Chat.Requests.Should().BeEmpty();
        harness.Ui.Lines.Should().Contain(line => line.Contains("mode: plan", StringComparison.Ordinal));
        harness.Ui.Lines.Should().Contain("  available (1): read_file");
        harness.Ui.Lines.Should().Contain(line => line.Contains("withheld", StringComparison.Ordinal) &&
            line.Contains("write_file", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Config_rules_lists_sources_and_scopes_without_exposing_rule_body()
    {
        var harness = new SessionHarness();
        harness.FileSystem.AddFile(Path.Combine(harness.Root, ".codai", "rules", "csharp.md"),
            "---\napplyTo: src/**/*.cs\n---\nNever display this rule body.");
        harness.QueueInputs(":config rules", ":exit");

        (await harness.Build().RunAsync()).Should().Be(0);

        harness.Chat.Requests.Should().BeEmpty();
        harness.Ui.Lines.Should().Contain(line => line.Contains("src/**/*.cs", StringComparison.Ordinal) &&
            line.Contains(".codai/rules/csharp.md", StringComparison.Ordinal));
        harness.Ui.Lines.Should().NotContain(line => line.Contains("Never display", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Config_unknown_target_reports_error_without_calling_model()
    {
        var harness = new SessionHarness();
        harness.QueueInputs(":config agents", ":exit");

        (await harness.Build().RunAsync()).Should().Be(0);

        harness.Chat.Requests.Should().BeEmpty();
        harness.Ui.Errors.Should().ContainSingle().Which.Should().Contain("Unknown configuration target");
    }

    [Fact]
    public async Task Exit_stops_the_session_without_sending_a_request()
    {
        var harness = new SessionHarness();
        harness.QueueInputs(":exit", "never reached");

        await harness.Build().RunAsync();

        harness.Chat.Requests.Should().BeEmpty();
        harness.Ui.ReadRequests.Should().ContainSingle();
    }

    [Fact]
    public async Task Help_prints_the_command_list()
    {
        var harness = new SessionHarness();
        harness.QueueInputs(":help");

        await harness.Build().RunAsync();

        harness.Ui.Lines.Should().Contain(SlashCommandParser.HelpText);
    }

    [Fact]
    public async Task Clear_clears_the_screen()
    {
        var harness = new SessionHarness();
        harness.QueueInputs(":clear");

        await harness.Build().RunAsync();

        harness.Ui.ClearCount.Should().Be(1);
    }

    [Fact]
    public async Task Clear_history_forgets_the_conversation()
    {
        var harness = new SessionHarness();
        harness.Chat.Enqueue("first", new TokenUsage(5, 5));
        harness.Chat.Enqueue("second");
        harness.QueueInputs("question", ":clear-history", "another question");

        await harness.Build().RunAsync();

        harness.History.Turns.Should().ContainSingle();
        harness.History.Turns[0].UserRequest.Should().Be("another question");
    }

    [Fact]
    public async Task Clear_token_resets_the_usage_counters()
    {
        var harness = new SessionHarness();
        harness.Chat.Enqueue("first", new TokenUsage(100, 100));
        harness.Chat.Enqueue("second", new TokenUsage(1, 1));
        harness.QueueInputs("question", ":clear-token", "another question");

        await harness.Build().RunAsync();

        harness.Ui.TokenDisplays.Should().HaveCount(2);
        harness.Ui.TokenDisplays[^1].Session.Should().Be(new TokenUsage(1, 1));
    }

    [Fact]
    public async Task Token_shows_usage_without_calling_the_model()
    {
        var harness = new SessionHarness();
        harness.Chat.Enqueue("first", new TokenUsage(7, 3));
        harness.QueueInputs("question", ":token");

        await harness.Build().RunAsync();

        harness.Chat.Requests.Should().ContainSingle();
        harness.Ui.TokenDisplays.Should().HaveCount(2);
        harness.Ui.TokenDisplays[^1].Session.TotalTokens.Should().Be(10);
    }

    [Fact]
    public async Task End_of_input_stops_the_loop()
    {
        var harness = new SessionHarness();
        harness.Ui.EnqueueInput(null);

        var exitCode = await harness.Build().RunAsync();

        exitCode.Should().Be(0);
        harness.Chat.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(":help", SlashCommand.Help)]
    [InlineData(":HELP", SlashCommand.Help)]
    [InlineData("  :clear  ", SlashCommand.Clear)]
    [InlineData(":exit", SlashCommand.Exit)]
    [InlineData(":quit", SlashCommand.Exit)]
    [InlineData(":token", SlashCommand.Token)]
    [InlineData(":clear-token", SlashCommand.ClearToken)]
    [InlineData(":clear-history", SlashCommand.ClearHistory)]
    [InlineData(":compact", SlashCommand.Compact)]
    [InlineData(":new-task", SlashCommand.NewTask)]
    [InlineData(":new-rule coding", SlashCommand.NewRule)]
    [InlineData(":report-bug broken request", SlashCommand.ReportBug)]
    [InlineData(":deep-plan build a feature", SlashCommand.DeepPlan)]
    [InlineData(":deep-planning build a feature", SlashCommand.DeepPlan)]
    [InlineData(":model", SlashCommand.Model)]
    [InlineData(":model plan provider model", SlashCommand.Model)]
    [InlineData(":mode", SlashCommand.Mode)]
    [InlineData(":mode agent", SlashCommand.Mode)]
    [InlineData(":team list", SlashCommand.Team)]
    [InlineData(":config", SlashCommand.Config)]
    [InlineData(":config tools", SlashCommand.Config)]
    [InlineData(":settings model", SlashCommand.Config)]
    [InlineData("hello", SlashCommand.None)]
    [InlineData("", SlashCommand.None)]
    [InlineData(":unknown", SlashCommand.None)]
    public void Parses_known_commands(string input, SlashCommand expected) =>
        SlashCommandParser.Parse(input).Should().Be(expected);

    [Theory]
    [InlineData(":mode agent", "agent")]
    [InlineData(":mode", "")]
    [InlineData(":mode   plan   ", "plan")]
    [InlineData(":compact", "")]
    [InlineData(":compact keep the file paths", "keep the file paths")]
    [InlineData("not a command", "")]
    public void Reads_the_arguments_after_the_command_name(string input, string expected) =>
        SlashCommandParser.Arguments(input).Should().Be(expected);

    [Fact]
    public async Task Mode_with_no_argument_reports_the_current_mode_without_calling_the_model()
    {
        var harness = new SessionHarness();
        harness.QueueInputs(":mode", ":exit");

        await harness.Build().RunAsync();

        harness.Chat.Requests.Should().BeEmpty();
        harness.Ui.Infos.Should().ContainSingle()
            .Which.Should().Contain("Mode: agent");
    }

    [Fact]
    public async Task Model_command_changes_and_reports_the_selected_mode_model()
    {
        var harness = new SessionHarness();
        harness.QueueInputs(":model plan deepseek plan-model", ":model plan", ":exit");

        await harness.Build().RunAsync();

        harness.Chat.Requests.Should().BeEmpty();
        harness.Ui.Lines.Should().Contain(line => line.Contains("plan-model", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Mode_changes_what_the_assistant_is_allowed_to_do_for_the_next_request()
    {
        var harness = new SessionHarness();
        harness.Chat.Enqueue("answer");
        harness.QueueInputs(":mode agent", "do something", ":exit");

        await harness.Build().RunAsync();

        // The mode is read per request rather than captured when the service was built,
        // which is the whole reason it lives in a holder.
        harness.Chat.Requests.Should().ContainSingle();
        harness.Chat.Requests[0].SystemPrompt.Should().Contain("expected to finish the task");
    }

    [Fact]
    public async Task An_unknown_mode_is_reported_and_the_current_mode_is_kept()
    {
        var harness = new SessionHarness();
        harness.Chat.Enqueue("answer");
        harness.QueueInputs(":mode plan", ":mode yolo", "question", ":exit");

        await harness.Build().RunAsync();

        harness.Ui.Errors.Should().ContainSingle().Which.Should().Contain("Unknown mode 'yolo'");

        // The rejected name changed nothing, so the request still carries the guidance
        // of the last mode that was accepted.
        harness.Chat.Requests[0].SystemPrompt.Should().Contain("must not change any file");
    }

    [Theory]
    [InlineData(":mode ask")]
    [InlineData("/mode ASK")]
    [InlineData(":model ask provider model")]
    public async Task Removed_ask_selection_is_rejected_without_changing_mode(string command)
    {
        var harness = new SessionHarness();
        harness.Mode.Mode = AgentMode.Plan;
        harness.QueueInputs(command, ":exit");

        (await harness.Build().RunAsync()).Should().Be(0);

        harness.Mode.Mode.Should().Be(AgentMode.Plan);
        harness.Chat.Requests.Should().BeEmpty();
        harness.Ui.Errors.Should().ContainSingle().Which.Should().Contain("plan").And.Contain("agent");
    }

    [Fact]
    public async Task Model_listing_contains_only_plan_and_agent()
    {
        var harness = new SessionHarness();
        harness.QueueInputs(":model", ":exit");

        await harness.Build().RunAsync();

        harness.Ui.Lines.Should().HaveCount(2);
        harness.Ui.Lines[0].Should().StartWith("plan:");
        harness.Ui.Lines[1].Should().StartWith("agent:");
    }

    [Fact]
    public async Task An_unknown_command_is_reported_rather_than_sent_to_the_model()
    {
        var harness = new SessionHarness();
        harness.QueueInputs(":nope", ":exit");

        await harness.Build().RunAsync();

        harness.Chat.Requests.Should().BeEmpty();
        harness.Ui.Errors.Should().ContainSingle().Which.Should().Contain("Unknown command ':nope'");
    }

    [Fact]
    public async Task A_project_command_becomes_the_request()
    {
        var harness = new SessionHarness();
        harness.Chat.Enqueue("answer");
        harness.FileSystem.AddFile(
            System.IO.Path.Combine(harness.Root, ".codai", "commands", "review.md"),
            "---\ndescription: Reviews a change\n---\nReview the working tree: $ARGUMENTS");
        harness.QueueInputs(":review src/a.cs", ":exit");

        await harness.Build().RunAsync();

        harness.Chat.Requests.Should().ContainSingle()
            .Which.UserMessage.Should().Contain("Review the working tree: src/a.cs");
    }

    [Fact]
    public async Task Compact_replaces_the_history_with_a_summary()
    {
        var harness = new SessionHarness();
        harness.Chat.Enqueue("first answer");
        harness.Chat.Enqueue("second answer");
        harness.Chat.Enqueue("third answer");
        harness.Chat.Enqueue("the summary");
        harness.QueueInputs("first question", "second question", "third question", ":compact", ":exit");

        await harness.Build().RunAsync();

        // The summary replaces the older turns and the recent ones are kept as they were.
        harness.History.Turns.Should().HaveCount(3);
        harness.History.Turns[0].UserRequest.Should().Be(HistoryCompactor.SummaryTurnLabel);
        harness.History.Turns[0].AiResponse.Should().Be("the summary");
        harness.History.Turns[2].AiResponse.Should().Be("third answer");
    }

    [Fact]
    public async Task Compact_carries_the_focus_the_user_typed()
    {
        var harness = new SessionHarness();
        harness.Chat.Enqueue("first answer");
        harness.Chat.Enqueue("second answer");
        harness.Chat.Enqueue("third answer");
        harness.Chat.Enqueue("the summary");
        harness.QueueInputs("first question", "second question", "third question", ":compact keep the file paths", ":exit");

        await harness.Build().RunAsync();

        harness.Chat.Requests[^1].UserMessage
            .Should()
            .Contain("keep the file paths");
    }

    [Fact]
    public async Task New_task_compacts_history_into_a_handoff()
    {
        var harness = new SessionHarness();
        harness.Chat.Enqueue("first answer");
        harness.Chat.Enqueue("second answer");
        harness.Chat.Enqueue("third answer");
        harness.Chat.Enqueue("the handoff");
        harness.QueueInputs("first question", "second question", "third question", ":new-task", ":exit");

        await harness.Build().RunAsync();

        harness.History.Turns.Should().Contain(turn => turn.AiResponse == "the handoff");
        harness.Ui.Successes.Should().Contain(line => line.Contains("Task handoff prepared", StringComparison.Ordinal));
        harness.Chat.Requests[^1].UserMessage.Should().Contain("Prepare a concise handoff");
    }

    [Fact]
    public async Task New_rule_writes_confirmed_rule_under_project_codai_directory()
    {
        var harness = new SessionHarness();
        harness.Ui.EnqueueConfirmation(true);
        harness.QueueInputs(":new-rule coding | Prefer small focused methods", ":exit");

        await harness.Build().RunAsync();

        var path = Path.Combine(harness.Root, ".codai", "rules", "coding.md");
        harness.FileSystem.Files.Should().ContainKey(InMemoryFileSystem.Normalize(path));
        (await harness.FileSystem.ReadAllTextAsync(path)).Should().Contain("Prefer small focused methods");
    }

    [Fact]
    public async Task Report_bug_writes_local_diagnostics_without_credentials()
    {
        var harness = new SessionHarness();
        harness.Ui.EnqueueConfirmation(true);
        harness.QueueInputs(":report-bug request failed", ":exit");

        await harness.Build().RunAsync();

        var report = harness.FileSystem.Files
            .Where(pair => pair.Key.Contains("/.codai/reports/bug-", StringComparison.OrdinalIgnoreCase))
            .Select(pair => pair.Value)
            .Should().ContainSingle().Subject;
        report.Should().Contain("request failed").And.Contain("Provider: deepseek").And.NotContain("API_KEY");
    }

    [Fact]
    public async Task The_mode_of_the_request_reaches_the_executor()
    {
        // The whole chain in one test: the mode narrows the tools the prompt builder
        // produces, the request carries that list, the exchange service publishes it, and
        // the executor refuses anything outside it. Without the last link a read-only mode
        // would be a sentence in the prompt and nothing more.
        var harness = new SessionHarness();
        harness.Mode.Mode = AgentMode.Plan;
        harness.Chat.Enqueue("answer");
        harness.QueueInputs("question", ":exit");

        await harness.Build().RunAsync();

        harness.Chat.Requests[0].AllowedTools.Should().Equal("read_file");

        var refused = await harness.Executor.ExecuteAsync(
            "write_file",
            System.Text.Json.JsonDocument.Parse("""{"path":"a.txt"}""").RootElement.Clone());

        refused.IsSuccess.Should().BeFalse();
        refused.Content.Should().Contain("not available for this request");

        var allowed = await harness.Executor.ExecuteAsync(
            "read_file",
            System.Text.Json.JsonDocument.Parse("""{"path":"a.txt"}""").RootElement.Clone());

        allowed.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task A_response_that_proposes_a_change_is_not_applied_in_a_read_only_mode()
    {
        var harness = new SessionHarness();
        harness.Mode.Mode = AgentMode.Plan;
        harness.Chat.Enqueue("File: src/Program.cs\n```csharp\n-old\n+new\n```\n");
        harness.FileSystem.AddFile(Path.Combine(harness.Root, "src", "Program.cs"), "old");
        harness.QueueInputs("change it", ":exit");

        await harness.Build().RunAsync();

        harness.Ui.Errors.Should().Contain(message => message.Contains("were not applied", StringComparison.Ordinal));
        (await harness.FileSystem.ReadAllTextAsync(Path.Combine(harness.Root, "src", "Program.cs"))).Should().Be("old");

        // Not one confirmation prompt: nothing was offered, so nothing is asked about.
        harness.Ui.Prompts.Should().BeEmpty();
    }

    [Fact]
    public async Task The_same_response_is_offered_and_applied_in_agent_mode()
    {
        var harness = new SessionHarness();
        harness.Mode.Mode = AgentMode.Agent;
        harness.Chat.Enqueue("File: src/Program.cs\n```csharp\n-old\n+new\n```\n");
        harness.FileSystem.AddFile(Path.Combine(harness.Root, "src", "Program.cs"), "old");
        harness.Ui.EnqueueConfirmation(true);
        harness.QueueInputs("change it", ":exit");

        await harness.Build().RunAsync();

        harness.Ui.Prompts.Should().ContainSingle()
            .Which.Should().Contain("src/Program.cs");
        (await harness.FileSystem.ReadAllTextAsync(Path.Combine(harness.Root, "src", "Program.cs"))).Should().Be("new");
    }

    [Fact]
    public async Task Compact_is_reported_in_the_help_text()
    {
        SlashCommandParser.HelpText.Should().Contain("/compact");
        SlashCommandParser.HelpText.Should().Contain("/mode");
        SlashCommandParser.HelpText.Should().Contain("/team start");
        SlashCommandParser.HelpText.Should().Contain("/team add");
        SlashCommandParser.HelpText.Should().Contain("/team complete");
    }

    [Fact]
    public async Task Task_export_and_import_use_metadata_without_loading_conversation_or_files()
    {
        var harness = new SessionHarness();
        var store = new FileAgentTaskStore(harness.FileSystem);
        harness.TaskStore = store;
        var task = AgentTask.Create("preserve task metadata").Complete();
        await store.SaveAsync(harness.Root, task);
        harness.FileSystem.AddFile(Path.Combine(harness.Root, "src", "existing.cs"), "original");
        harness.QueueInputs(":task export " + task.Id + " C:\\exports\\task.json",
            ":task import C:\\exports\\task.json", ":exit");

        (await harness.Build().RunAsync()).Should().Be(0);

        harness.History.Turns.Should().BeEmpty();
        (await harness.FileSystem.ReadAllTextAsync(Path.Combine(harness.Root, "src", "existing.cs")))
            .Should().Be("original");
        (await store.LoadAsync(harness.Root)).Should().BeEquivalentTo(task);
        harness.Ui.Errors.Should().BeEmpty();
    }
}
