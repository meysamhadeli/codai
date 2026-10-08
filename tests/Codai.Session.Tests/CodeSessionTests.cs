using Codai.Core.Abstractions;
using Codai.Core.Chat;
using Codai.Core.Tokens;
using Codai.Session.Tests.Fakes;
using FluentAssertions;

namespace Codai.Session.Tests;

public class CodeSessionTests
{
    [Fact]
    public async Task Streams_the_response_and_reports_token_usage()
    {
        var harness = new SessionHarness();
        harness.Chat.Enqueue("Hello there!", new TokenUsage(120, 30));
        harness.QueueInputs("say hi");

        var exitCode = await harness.Build().RunAsync();

        exitCode.Should().Be(0);
        harness.Ui.AssistantOutput.ToString().Should().Be("Hello there!");
        harness.Ui.TokenDisplays.Should().ContainSingle();

        var display = harness.Ui.TokenDisplays[0];
        display.Model.Should().Be("deepseek-flash");
        display.Session.Should().Be(new TokenUsage(120, 30));
        display.Last.Should().Be(new TokenUsage(120, 30));
    }

    [Fact]
    public async Task Scans_the_project_once_before_the_first_request()
    {
        var harness = new SessionHarness();
        harness.Chat.Enqueue("ok");
        harness.QueueInputs("hi");

        await harness.Build().RunAsync();

        harness.Scanner.ScannedRoots.Should().ContainSingle().Which.Should().Be(harness.Root);
        harness.Ui.ProgressLabels.Should().Contain("Loading Context...");
    }

    [Fact]
    public async Task Sends_the_project_context_in_the_system_prompt()
    {
        var harness = new SessionHarness();
        harness.Chat.Enqueue("ok");
        harness.QueueInputs("hi");

        await harness.Build().RunAsync();

        var request = harness.Chat.Requests.Should().ContainSingle().Subject;
        request.SystemPrompt.Should().Contain("**File: Program.cs**");
        request.SystemPrompt.Should().Contain("## Here is the summary of context of project");
        request.UserMessage.Should().Be("## Here is user request\nhi");
    }

    [Fact]
    public async Task Applies_accepted_code_changes_to_the_file_system()
    {
        var harness = new SessionHarness();
        harness.Chat.Enqueue("File: src/Foo.cs\n```csharp\n+var x = 1;\n```");
        harness.QueueInputs("add a variable");
        harness.Ui.EnqueueConfirmation(true);

        await harness.Build().RunAsync();

        harness.FileSystem.FileExists(@"C:\project\src\Foo.cs").Should().BeTrue();
        harness.FileSystem.Files[InMemoryFileSystem.Normalize(@"C:\project\src\Foo.cs")]
            .Should().Be("var x = 1;");
        harness.Ui.Successes.Should().Contain(message => message.Contains("src/Foo.cs", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Does_not_write_anything_when_the_user_rejects_a_change()
    {
        var harness = new SessionHarness();
        harness.Chat.Enqueue("File: src/Foo.cs\n```csharp\n+var x = 1;\n```");
        harness.QueueInputs("add a variable");
        harness.Ui.EnqueueConfirmation(false);

        await harness.Build().RunAsync();

        // The session transcript is written on purpose; no project file may be.
        harness.FileSystem.Files.Keys
            .Where(path => !path.Contains("/.codai/", StringComparison.OrdinalIgnoreCase))
            .Should().BeEmpty();
        harness.Ui.Errors.Should().Contain(message => message.Contains("rejected", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Asks_for_the_full_contents_of_files_the_model_requests()
    {
        var harness = new SessionHarness();
        harness.FileSystem.AddFile(@"C:\project\src\Foo.cs", "class Foo { }");

        harness.Chat.Enqueue("I need more context: [\"src/Foo.cs\"]");
        harness.Chat.Enqueue("Now I can answer.");
        harness.QueueInputs("refactor Foo");
        harness.Ui.EnqueueConfirmation(true);

        await harness.Build().RunAsync();

        harness.Chat.Requests.Should().HaveCount(2);
        harness.Chat.Requests[1].SystemPrompt
            .Should().Contain("## Here are the requsted full context files for using in your task");
        harness.Chat.Requests[1].SystemPrompt.Should().Contain("class Foo { }");
        harness.Ui.Successes.Should().Contain("✔️ Context accepted!");
    }

    [Fact]
    public async Task Skips_the_retry_when_the_user_rejects_the_extra_context()
    {
        var harness = new SessionHarness();
        harness.FileSystem.AddFile(@"C:\project\src\Foo.cs", "class Foo { }");

        harness.Chat.Enqueue("I need more context: [\"src/Foo.cs\"]");
        harness.QueueInputs("refactor Foo");
        harness.Ui.EnqueueConfirmation(false);

        await harness.Build().RunAsync();

        harness.Chat.Requests.Should().ContainSingle();
        harness.Ui.Errors.Should().Contain("❌ Context rejected.");
    }

    [Fact]
    public async Task Does_not_retry_when_a_listed_file_cannot_be_read()
    {
        var harness = new SessionHarness();
        harness.Chat.Enqueue("Please read [\"src/Missing.cs\"]");
        harness.QueueInputs("go");
        harness.Ui.EnqueueConfirmation(true);

        await harness.Build().RunAsync();

        harness.Chat.Requests.Should().ContainSingle();
        harness.Ui.Errors.Should().Contain(message => message.Contains("could be read", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Records_exactly_one_history_turn_per_user_request()
    {
        var harness = new SessionHarness();
        harness.FileSystem.AddFile(@"C:\project\src\Foo.cs", "class Foo { }");

        harness.Chat.Enqueue("More context needed [\"src/Foo.cs\"]");
        harness.Chat.Enqueue("final answer");
        harness.QueueInputs("do it");
        harness.Ui.EnqueueConfirmation(true);

        await harness.Build().RunAsync();

        harness.History.Turns.Should().ContainSingle();
        harness.History.Turns[0].UserRequest.Should().Be("do it");
        harness.History.Turns[0].AiResponse.Should().Be("final answer");
    }

    [Fact]
    public async Task Includes_previous_turns_in_the_next_request()
    {
        var harness = new SessionHarness();
        harness.Chat.Enqueue("first answer");
        harness.Chat.Enqueue("second answer");
        harness.QueueInputs("first question", "second question");

        await harness.Build().RunAsync();

        harness.Chat.Requests.Should().HaveCount(2);
        harness.Chat.Requests[1].SystemPrompt.Should().Contain("## Here is the history of chats");
        harness.Chat.Requests[1].SystemPrompt.Should().Contain("first question");
    }

    [Fact]
    public async Task Ignores_blank_input()
    {
        var harness = new SessionHarness();
        harness.QueueInputs(string.Empty, "   ", "real request");
        harness.Chat.Enqueue("ok");

        await harness.Build().RunAsync();

        harness.Chat.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task Reports_provider_failures_and_keeps_the_session_alive()
    {
        var harness = new SessionHarness();
        harness.Chat.Failures.Enqueue(new ChatProviderException("boom"));
        harness.Chat.Enqueue("recovered");
        harness.QueueInputs("first", "second");

        var exitCode = await harness.Build().RunAsync();

        exitCode.Should().Be(0);
        harness.Ui.Errors.Should().Contain("boom");
        harness.Ui.AssistantOutput.ToString().Should().Be("recovered");
    }

    [Fact]
    public async Task Reports_a_scan_failure_but_still_answers()
    {
        var harness = new SessionHarness();
        harness.Scanner.Failure = new DirectoryNotFoundException("Project directory was not found.");
        harness.Chat.Enqueue("answering without context");
        harness.QueueInputs("hi");

        await harness.Build().RunAsync();

        harness.Ui.Errors.Should().Contain("Project directory was not found.");
        harness.Chat.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task Uses_the_cost_calculator_when_one_is_available()
    {
        var harness = new SessionHarness { CostCalculator = new FixedCostCalculator(0.42m) };
        harness.Chat.Enqueue("ok", new TokenUsage(10, 10));
        harness.QueueInputs("hi");

        await harness.Build().RunAsync();

        harness.Ui.TokenDisplays.Should().ContainSingle()
            .Which.Cost.Should().Be(0.42m);
    }

    [Fact]
    public async Task Follows_the_project_instructions_file()
    {
        var harness = new SessionHarness { Instructions = "Always write a test." };
        harness.Chat.Enqueue("ok");
        harness.QueueInputs("do it");

        await harness.Build().RunAsync();

        harness.Ui.Infos.Should().Contain(message => message.Contains("AGENTS.md", StringComparison.Ordinal));
        harness.Chat.Requests[0].SystemPrompt.Should().Contain("Always write a test.");
        harness.Chat.Requests[0].SystemPrompt
            .Should().Contain("## Here are the project instructions you must follow");
    }

    [Fact]
    public async Task Attaches_the_files_the_user_mentions()
    {
        var harness = new SessionHarness();
        harness.FileSystem.AddFile(@"C:\project\src\a.cs", "class A { }");
        harness.Chat.Enqueue("ok");
        harness.QueueInputs("explain @src/a.cs");

        await harness.Build().RunAsync();

        harness.Chat.Requests[0].SystemPrompt.Should().Contain("class A { }");
        harness.Ui.Infos.Should().Contain(message => message.Contains("Attached 1 file", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Warns_about_a_mention_that_matches_nothing()
    {
        var harness = new SessionHarness();
        harness.Chat.Enqueue("ok");
        harness.QueueInputs("explain @src/missing.cs");

        await harness.Build().RunAsync();

        harness.Ui.Errors.Should().Contain(message => message.Contains("'@src/missing.cs'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Persists_the_conversation_after_every_turn()
    {
        var harness = new SessionHarness();
        harness.Chat.Enqueue("remembered answer");
        harness.QueueInputs("remember this");

        await harness.Build().RunAsync();

        var stored = await harness.SessionStore.LoadAsync(harness.Root);

        stored.Should().ContainSingle();
        stored[0].UserRequest.Should().Be("remember this");
        stored[0].AiResponse.Should().Be("remembered answer");
    }

    [Fact]
    public async Task Resumes_the_stored_conversation_when_asked()
    {
        var harness = new SessionHarness();
        await harness.SessionStore.SaveAsync(
            harness.Root,
            [new ChatTurn(DateTimeOffset.UtcNow, "previous question", "previous answer")]);

        harness.Options = new SessionOptions
        {
            ProjectRoot = harness.Root,
            Provider = harness.Options.Provider,
            Model = harness.Options.Model,
            ResumePreviousSession = true,
        };

        harness.Chat.Enqueue("sure");
        harness.QueueInputs("and now this");

        await harness.Build().RunAsync();

        harness.Chat.Requests[0].SystemPrompt.Should().Contain("previous question");
        harness.History.Turns.Should().HaveCount(2);
        harness.Ui.Successes.Should().Contain(message => message.Contains("Resumed 1 turn", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Says_so_when_there_is_nothing_to_resume()
    {
        var harness = new SessionHarness();
        harness.Options = new SessionOptions
        {
            ProjectRoot = harness.Root,
            Provider = harness.Options.Provider,
            Model = harness.Options.Model,
            ResumePreviousSession = true,
        };

        harness.QueueInputs(":exit");

        await harness.Build().RunAsync();

        harness.Ui.Infos.Should().Contain("No previous session to continue.");
    }

    [Fact]
    public async Task Does_not_resume_unless_asked()
    {
        var harness = new SessionHarness();
        await harness.SessionStore.SaveAsync(
            harness.Root,
            [new ChatTurn(DateTimeOffset.UtcNow, "previous question", "previous answer")]);

        harness.Chat.Enqueue("fresh answer");
        harness.QueueInputs("a new question");

        await harness.Build().RunAsync();

        harness.Chat.Requests[0].SystemPrompt.Should().NotContain("previous question");
        harness.History.Turns.Should().ContainSingle();
    }

    [Fact]
    public async Task Clearing_the_history_forgets_the_stored_session()
    {
        var harness = new SessionHarness();
        harness.Chat.Enqueue("answer");
        harness.QueueInputs("question", ":clear-history");

        await harness.Build().RunAsync();

        harness.FileSystem.FileExists(FileSessionStore.GetPath(harness.Root)).Should().BeFalse();
        (await harness.SessionStore.LoadAsync(harness.Root)).Should().BeEmpty();
    }

    private sealed class FixedCostCalculator(decimal cost) : ICostCalculator
    {
        public decimal Calculate(string provider, string model, TokenUsage usage) => cost;
    }
}
