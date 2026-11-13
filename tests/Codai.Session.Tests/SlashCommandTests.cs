using Codai.Core.Agents;
using Codai.Core.Chat;
using Codai.Core.Tokens;
using Codai.Session.Tests.Fakes;
using FluentAssertions;

namespace Codai.Session.Tests;

public class SlashCommandTests
{
    [Fact]
    public void Update_is_a_builtin_command_advertised_in_help()
    {
        SlashCommandParser.Parse("/update").Should().NotBe(SlashCommand.None);
        SlashCommandParser.HelpText.Should().Contain("/update");
    }

    [Theory]
    [InlineData("/init Focus on tests", "AGENTS.md", AgentMode.Agent)]
    public async Task Prompt_commands_run_through_normal_request_pipeline(string input, string expected, AgentMode mode)
    {
        var harness = new SessionHarness();
        harness.Chat.Enqueue("Done.");
        harness.QueueInputs(input, "/exit");

        await harness.Build().RunAsync();

        var request = harness.Chat.Requests.Should().ContainSingle().Subject;
        request.UserMessage.Should().Contain(expected);
        request.AllowedTools.Should().BeEquivalentTo(mode == AgentMode.Plan ? ["read_file"] : new[] { "read_file", "write_file" });
        harness.Mode.Mode.Should().Be(AgentMode.Agent);
    }

    [Theory]
    [InlineData("/debug")]
    [InlineData("/editor")]
    [InlineData("/review")]
    [InlineData("/status")]
    [InlineData("/variants")]
    [InlineData("/connect")]
    public async Task Removed_commands_do_not_call_model_edit_files_or_open_dialogs(string command)
    {
        var harness = new SessionHarness();
        var path = Path.Combine(harness.Root, "Program.cs");
        harness.FileSystem.AddFile(path, "original");
        harness.Chat.Enqueue("File: Program.cs\n```csharp\n-original\n+changed\n```\n");
        harness.Ui.EnqueueConfirmation(true);
        harness.QueueInputs(command, "/exit");

        await harness.Build().RunAsync();

        (await harness.FileSystem.ReadAllTextAsync(path)).Should().Be("original");
        harness.Ui.Prompts.Should().BeEmpty();
        harness.Ui.Dialogs.Should().BeEmpty();
        harness.Chat.Requests.Should().BeEmpty();
        harness.Ui.Errors.Should().ContainSingle().Which.Should().Contain("Unknown command");
        harness.Mode.Mode.Should().Be(AgentMode.Agent);
    }

    [Theory]
    [InlineData("/agents", "plan")]
    [InlineData("/models", "model:chosen-model")]
    public async Task Selection_commands_open_local_pickers(string command, string selection)
    {
        var harness = new SessionHarness(model: "chosen-model");
        harness.Ui.EnqueueChoice(selection);
        harness.QueueInputs(command, "/exit");

        await harness.Build().RunAsync();

        harness.Chat.Requests.Should().BeEmpty();
        harness.Ui.Dialogs.Should().ContainSingle();
        harness.Ui.Successes.Should().ContainSingle();
    }

    [Theory]
    [InlineData("/agents")]
    [InlineData("/models")]
    public async Task Cancelled_selection_does_not_change_session_or_send_requests(string command)
    {
        var harness = new SessionHarness();
        harness.Ui.EnqueueChoice(null);
        harness.Ui.EnqueueValue(null);
        harness.QueueInputs(command, "/exit");

        await harness.Build().RunAsync();

        harness.Chat.Requests.Should().BeEmpty();
        harness.Ui.Successes.Should().BeEmpty();
        harness.Ui.Insertions.Should().BeEmpty();
        harness.Mode.Mode.Should().Be(AgentMode.Agent);
    }

    [Theory]
    [InlineData("agent", AgentMode.Agent)]
    [InlineData("plan", AgentMode.Plan)]
    public async Task Agents_picker_offers_agent_and_plan_and_applies_selected_mode(string selection, AgentMode expected)
    {
        var harness = new SessionHarness();
        harness.Mode.Mode = AgentMode.Plan;
        harness.Ui.EnqueueSelection(items =>
        {
            items.Select(item => item.Label).Should().Equal("agent", "plan");
            items.Select(item => item.Value).Should().Equal("agent", "plan");
            items.Should().ContainSingle(item => item.IsCurrent).Which.Value.Should().Be("plan");
            return selection;
        });
        harness.QueueInputs("/agents", "/exit");

        await harness.Build().RunAsync();

        harness.Mode.Mode.Should().Be(expected);
        harness.Chat.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task New_archives_current_history_and_clears_conversation()
    {
        var harness = new SessionHarness();
        harness.TaskStore = new FileAgentTaskStore(harness.FileSystem);
        harness.Chat.Enqueue("answer");
        harness.Ui.EnqueueChoice("confirm");
        harness.QueueInputs("question", "/new", "/exit");

        await harness.Build().RunAsync();

        harness.History.Turns.Should().BeEmpty();
        var tasks = await harness.TaskStore.ListAsync(harness.Root);
        var source = tasks.Single(task => task.Request == "question");
        (await harness.SessionStore.LoadAsync(harness.Root, source.Id)).Should().ContainSingle();
        harness.Tokens.SessionUsage.TotalTokens.Should().Be(0);
    }

    [Theory]
    [InlineData("/session")]
    [InlineData("/sessions")]
    public async Task Sessions_restores_selected_conversation_without_a_model_call(string command)
    {
        var harness = new SessionHarness();
        harness.TaskStore = new FileAgentTaskStore(harness.FileSystem);
        var source = (AgentTask.Create("Earlier session") with { Mode = AgentMode.Plan }).Complete();
        await harness.TaskStore.SaveAsync(harness.Root, source);
        await harness.SessionStore.SaveAsync(harness.Root, source.Id, [new ChatTurn(DateTimeOffset.UtcNow, "saved", "answer")]);
        harness.Ui.EnqueueChoice(source.Id);
        harness.QueueInputs(command, "/exit");

        await harness.Build().RunAsync();

        harness.History.Turns.Should().ContainSingle().Which.UserRequest.Should().Be("saved");
        harness.Mode.Mode.Should().Be(AgentMode.Plan);
        harness.Chat.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Retired_slash_command_is_not_forwarded_to_model()
    {
        var harness = new SessionHarness();
        harness.QueueInputs("/task list", "/exit");

        await harness.Build().RunAsync();

        harness.Chat.Requests.Should().BeEmpty();
        harness.Ui.Errors.Should().ContainSingle().Which.Should().Contain("Unknown command");
    }

    [Fact]
    public void Slash_menu_matches_the_supported_command_set()
    {
        string[] names = ["agents", "auth", "autocompact", "compact", "diff", "exit", "help", "init", "mcps", "plugins", "models", "new", "session", "settings", "skills", "themes", "update"];
        var advertised = SlashCommandParser.HelpText.Split('\n')
            .Select(line => line.Split(' ', 2)[0].TrimStart('/')).ToArray();

        advertised.Should().BeEquivalentTo(names);
        foreach (var name in names)
        {
            SlashCommandParser.Parse("/" + name).Should().NotBe(SlashCommand.None, name);
        }
    }

    [Theory]
    [InlineData("mode")]
    [InlineData("model")]
    [InlineData("preferences")]
    [InlineData("task")]
    [InlineData("team")]
    [InlineData("repair")]
    [InlineData("act")]
    [InlineData("deep-plan")]
    [InlineData("config")]
    [InlineData("clear")]
    [InlineData("token")]
    [InlineData("new-rule")]
    [InlineData("report-bug")]
    [InlineData("connect")]
    [InlineData("debug")]
    [InlineData("editor")]
    [InlineData("review")]
    [InlineData("status")]
    [InlineData("variants")]
    public void Retired_slash_names_are_not_built_in_commands(string name) =>
        SlashCommandParser.Parse("/" + name).Should().Be(SlashCommand.None);

    [Fact]
    public async Task Settings_opens_local_configuration_without_a_model_request()
    {
        var harness = new SessionHarness();
        harness.Settings = new SessionSettingsWorkflow(harness.Ui, harness.Inspector,
            new Codai.Application.Inspection.ConfigurationInspectionContext
            {
                ProjectRoot = harness.Root,
                ConfigurationDirectory = harness.Root,
                Provider = harness.Options.Provider,
                Model = harness.Options.Model,
            }, harness.FileSystem);
        harness.Ui.EnqueueSelection(items =>
        {
            items.Single(item => item.Value == "ai_provider_config.model").RightLabel.Should().Be(harness.Options.Model);
            items.Single(item => item.Value == "ai_provider_config.api_key").RightLabel.Should().Be("Hidden");
            return null;
        });
        harness.QueueInputs("/settings", "/exit");

        await harness.Build().RunAsync();

        harness.Chat.Requests.Should().BeEmpty();
        harness.Ui.Dialogs.Should().ContainSingle().Which.Should().Contain("Edit configuration");
    }

    [Theory]
    [InlineData("status", "Mode")]
    [InlineData("usage", "Token budget")]
    [InlineData("stats", "Available tools")]
    public async Task Settings_tabs_show_real_inspection_data_and_return_to_configuration(string section, string label)
    {
        var harness = new SessionHarness();
        var workflow = new SessionSettingsWorkflow(harness.Ui, harness.Inspector,
            new Codai.Application.Inspection.ConfigurationInspectionContext
            {
                ProjectRoot = harness.Root,
                ConfigurationDirectory = harness.Root,
                Provider = harness.Options.Provider,
                Model = harness.Options.Model,
            }, harness.FileSystem);
        harness.Ui.EnqueueSelection(items =>
        {
            items.Should().Contain(item => item.Label == label && !string.IsNullOrEmpty(item.RightLabel));
            return "@settings:config";
        });
        harness.Ui.EnqueueSelection(items =>
        {
            items.Should().Contain(item => item.Value == "ai_provider_config.model");
            return null;
        });

        await workflow.RunAsync(section);

        harness.Chat.Requests.Should().BeEmpty();
        harness.Ui.Dialogs.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("/help", SlashCommand.Help)]
    [InlineData("/auth", SlashCommand.Connect)]
    [InlineData("/themes", SlashCommand.Themes)]
    [InlineData("/sessions", SlashCommand.Sessions)]
    [InlineData(" /agents plan ", SlashCommand.Agents)]
    public void Forward_slash_commands_use_OpenCode_handlers(string input, SlashCommand expected)
    {
        SlashCommandParser.Parse(input).Should().Be(expected);
    }

    [Fact]
    public async Task Forward_slash_commands_execute_locally_without_model_requests()
    {
        var harness = new SessionHarness();
        harness.QueueInputs("/agents plan", "/exit");
        (await harness.Build().RunAsync()).Should().Be(0);
        harness.Chat.Requests.Should().BeEmpty();
        harness.Mode.Mode.Should().Be(AgentMode.Plan);
        harness.Ui.Errors.Should().BeEmpty();
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
    [InlineData("/agents ASK")]
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
            System.IO.Path.Combine(harness.Root, ".codai", "commands", "inspect.md"),
            "---\ndescription: Reviews a change\n---\nReview the working tree: $ARGUMENTS");
        harness.QueueInputs("/inspect src/a.cs", ":exit");

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
        harness.QueueInputs("first question", "second question", "third question", "/compact", "/exit");

        await harness.Build().RunAsync();

        // The summary replaces the older turns and the recent ones are kept as they were.
        harness.History.Turns.Should().HaveCount(3);
        harness.History.Turns[0].UserRequest.Should().Be(HistoryCompactor.SummaryTurnLabel);
        harness.History.Turns[0].AiResponse.Should().Be("the summary");
        harness.History.Turns[2].AiResponse.Should().Be("third answer");
        harness.Ui.ModelRequests.Should().HaveCount(4).And.OnlyHaveUniqueItems();
        harness.Ui.CompletedModelRequests.Should().Equal(harness.Ui.ModelRequests);
        harness.Ui.ModelUpdates.Should().Contain(update => update.Update.IsCompleted);
    }

    [Fact]
    public async Task Compact_carries_the_focus_the_user_typed()
    {
        var harness = new SessionHarness();
        harness.Chat.Enqueue("first answer");
        harness.Chat.Enqueue("second answer");
        harness.Chat.Enqueue("third answer");
        harness.Chat.Enqueue("the summary");
        harness.QueueInputs("first question", "second question", "third question", "/compact keep the file paths", "/exit");

        await harness.Build().RunAsync();

        harness.Chat.Requests[^1].UserMessage
            .Should()
            .Contain("keep the file paths");
    }

    [Theory]
    [InlineData("auto", 0)]
    [InlineData("800k", 800_000)]
    [InlineData("1m", 1_000_000)]
    [InlineData("64K", 64_000)]
    [InlineData("32768", 32_768)]
    public async Task Auto_compact_command_changes_live_window_without_model_call(string value, int expected)
    {
        var harness = new SessionHarness();
        harness.QueueInputs("/autocompact " + value, "/exit");

        await harness.Build().RunAsync();

        harness.Options.AutoCompactWindow.Should().Be(expected);
        harness.Chat.Requests.Should().BeEmpty();
        harness.Ui.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("12k")]
    [InlineData("off")]
    [InlineData("99999999999999999999999999999m")]
    public async Task Invalid_auto_compact_window_does_not_change_live_setting(string value)
    {
        var harness = new SessionHarness();
        harness.Options.AutoCompactWindow = 64_000;
        harness.QueueInputs("/autocompact " + value, "/exit");

        await harness.Build().RunAsync();

        harness.Options.AutoCompactWindow.Should().Be(64_000);
        harness.Ui.Errors.Should().ContainSingle();
        harness.Chat.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Auto_compact_picker_can_cancel_or_apply_a_saved_window()
    {
        var harness = new SessionHarness();
        harness.Settings = new SessionSettingsWorkflow(harness.Ui, harness.Inspector,
            new()
            {
                ProjectRoot = harness.Root,
                ConfigurationDirectory = harness.Root,
                Provider = harness.Options.Provider,
                Model = harness.Options.Model
            }, harness.FileSystem);
        harness.Ui.EnqueueChoice(null);
        harness.Ui.EnqueueChoice("800000");
        harness.QueueInputs("/autocompact", "/autocompact", "/exit");

        await harness.Build().RunAsync();

        harness.Options.AutoCompactWindow.Should().Be(800_000);
        harness.Ui.Dialogs.Should().HaveCount(2);
        harness.Ui.Menus.Should().OnlyContain(menu => menu.HorizontalSelection);
        (await harness.FileSystem.ReadAllTextAsync(Path.Combine(harness.Root, ".codai.json")))
            .Should().Contain("800000");
        harness.Chat.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Failed_auto_compact_save_preserves_configuration_and_live_window()
    {
        var harness = new SessionHarness();
        var directory = Path.Combine(harness.Root, "settings");
        var path = Path.Combine(directory, ".codai.json");
        harness.FileSystem.AddFile(path, "{invalid");
        harness.Options.AutoCompactWindow = 64_000;
        harness.Settings = new SessionSettingsWorkflow(harness.Ui, harness.Inspector,
            new()
            {
                ProjectRoot = harness.Root,
                ConfigurationDirectory = directory,
                Provider = harness.Options.Provider,
                Model = harness.Options.Model
            }, harness.FileSystem);
        harness.QueueInputs("/autocompact 800k", "/exit");

        await harness.Build().RunAsync();

        harness.Options.AutoCompactWindow.Should().Be(64_000);
        (await harness.FileSystem.ReadAllTextAsync(path)).Should().Be("{invalid");
        harness.Ui.Errors.Should().ContainSingle().Which.Should().Contain("Could not save auto-compact window");
        harness.Chat.Requests.Should().BeEmpty();
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
    public void Retired_commands_are_not_reported_in_the_help_text()
    {
        SlashCommandParser.HelpText.Should().NotContain("/mode ")
            .And.NotContain("/team").And.NotContain("/preferences");
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
