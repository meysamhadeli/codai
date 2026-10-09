using Codai.Core.Agents;
using Codai.Core.Tokens;
using FluentAssertions;

namespace Codai.Session.Tests;

public class SlashCommandTests
{
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
    [InlineData(":mode", SlashCommand.Mode)]
    [InlineData(":mode agent", SlashCommand.Mode)]
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
        harness.QueueInputs(":mode ask", ":mode yolo", "question", ":exit");

        await harness.Build().RunAsync();

        harness.Ui.Errors.Should().ContainSingle().Which.Should().Contain("Unknown mode 'yolo'");

        // The rejected name changed nothing, so the request still carries the guidance
        // of the last mode that was accepted.
        harness.Chat.Requests[0].SystemPrompt.Should().Contain("must not change any file");
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
        harness.Chat.Enqueue("the summary");
        harness.QueueInputs("first question", "second question", ":compact", ":exit");

        await harness.Build().RunAsync();

        harness.History.Turns.Should().ContainSingle();
        harness.History.Turns[0].UserRequest.Should().Be(HistoryCompactor.SummaryTurnLabel);
        harness.History.Turns[0].AiResponse.Should().Be("the summary");
    }

    [Fact]
    public async Task Compact_carries_the_focus_the_user_typed()
    {
        var harness = new SessionHarness();
        harness.Chat.Enqueue("first answer");
        harness.Chat.Enqueue("second answer");
        harness.Chat.Enqueue("the summary");
        harness.QueueInputs("first question", "second question", ":compact keep the file paths", ":exit");

        await harness.Build().RunAsync();

        harness.Chat.Requests[^1].UserMessage
            .Should()
            .Contain("keep the file paths");
    }

    [Fact]
    public async Task The_mode_of_the_request_reaches_the_executor()
    {
        // The whole chain in one test: the mode narrows the tools the prompt builder
        // produces, the request carries that list, the exchange service publishes it, and
        // the executor refuses anything outside it. Without the last link a read-only mode
        // would be a sentence in the prompt and nothing more.
        var harness = new SessionHarness();
        harness.Mode.Mode = AgentMode.Ask;
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
        harness.Mode.Mode = AgentMode.Ask;
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
        SlashCommandParser.HelpText.Should().Contain(":compact");
        SlashCommandParser.HelpText.Should().Contain(":mode");
    }
}
