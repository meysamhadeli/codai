using Codai.Core.Models;
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
    [InlineData("hello", SlashCommand.None)]
    [InlineData("", SlashCommand.None)]
    [InlineData(":unknown", SlashCommand.None)]
    public void Parses_known_commands(string input, SlashCommand expected) =>
        SlashCommandParser.Parse(input).Should().Be(expected);
}
