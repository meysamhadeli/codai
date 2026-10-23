using System.Runtime.CompilerServices;
using System.Text.Json;
using Codai.Cli;
using Codai.Configuration;
using Codai.Core.Abstractions;
using Codai.Core.Agents;
using Codai.Core.Tools;
using Codai.Providers;
using Codai.Providers.Abstractions;
using Codai.Session.Tests.Fakes;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Codai.Session.Tests;

public class ToolIntegrationTests
{
    [Fact]
    public async Task Guidance_budget_failure_returns_terminal_result_without_approval()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "codai-rule-budget-tools"));
        var services = new ServiceCollection();
        var ui = new FakeUserInterface();
        ui.EnqueueConfirmation(true);
        services.AddSingleton<IUserInterface>(ui);
        services.AddCodaiApplication(OptionsFor(), root, mode: AgentMode.Agent);
        await using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<RequestRuleGuidance>().BeginRequest(
            [new Codai.Core.Customization.ProjectRule("large", ["*.cs"], new string('x', RuleSelector.MaxTotalCharacters), "rules/large.md")], []);

        var result = await provider.GetRequiredService<IToolExecutor>()
            .ExecuteAsync("write_file", ToolTestArgs.Parse("""{"path":"A.cs","content":"not approved"}"""));

        result.IsSuccess.Should().BeFalse();
        result.StopsExecution.Should().BeTrue();
        result.RequiresModelReview.Should().BeFalse();
        result.Content.Should().Contain("tool was not executed");
        ui.Prompts.Should().BeEmpty();
    }

    [Fact]
    public async Task Denied_file_tool_does_not_activate_rules_or_consume_write_approval()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "codai-denied-rule-tools"));
        var services = new ServiceCollection();
        var ui = new FakeUserInterface();
        services.AddSingleton<IUserInterface>(ui);
        services.AddCodaiApplication(OptionsFor(), root, mode: AgentMode.Plan);
        await using var provider = services.BuildServiceProvider();
        var guidance = provider.GetRequiredService<RequestRuleGuidance>();
        guidance.BeginRequest([new Codai.Core.Customization.ProjectRule("scoped", ["*.cs"], "Scoped guidance.", "rules/scoped.md")], []);
        var registry = provider.GetRequiredService<IToolRegistry>();
        provider.GetRequiredService<ToolAccessPolicy>().BeginRequest(AgentModePolicy.AllowedTools(AgentMode.Plan, registry.Tools));
        var executor = provider.GetRequiredService<IToolExecutor>();

        var denied = await executor.ExecuteAsync("write_file", ToolTestArgs.Parse("""{"path":"A.cs","content":"no"}"""));
        var read = await executor.ExecuteAsync("read_file", ToolTestArgs.Parse("""{"path":"A.cs"}"""));

        denied.IsSuccess.Should().BeFalse();
        denied.RequiresModelReview.Should().BeFalse();
        read.RequiresModelReview.Should().BeTrue();
        read.Content.Should().Contain("Scoped guidance.");
        ui.Prompts.Should().BeEmpty();
        guidance.EndRequest();
        (await executor.ExecuteAsync("read_file", ToolTestArgs.Parse("""{"path":"A.cs"}""")))
            .RequiresModelReview.Should().BeFalse();
    }

    [Theory]
    [InlineData(true, AgentMode.Agent, false)]
    [InlineData(false, AgentMode.Agent, false)]
    [InlineData(true, AgentMode.Plan, false)]
    [InlineData(false, AgentMode.Plan, false)]
    [InlineData(true, AgentMode.Agent, true)]
    [InlineData(false, AgentMode.Agent, true)]
    public async Task Lazy_rules_pause_batches_preserve_approval_and_fail_closed_when_oversized(bool stream, AgentMode mode, bool oversized)
    {
        var root = Directory.CreateTempSubdirectory("codai-lazy-rules-");
        try
        {
            Directory.CreateDirectory(Path.Combine(root.FullName, "src"));
            Directory.CreateDirectory(Path.Combine(root.FullName, ".codai", "rules"));
            await File.WriteAllTextAsync(Path.Combine(root.FullName, "src", "A.cs"), "original");
            await File.WriteAllTextAsync(Path.Combine(root.FullName, "other.txt"), "other");
            await File.WriteAllTextAsync(Path.Combine(root.FullName, ".codai", "rules", "api.md"),
                "---\napplyTo: src/**/*.cs\n---\nAPI rule must be reviewed before this file tool executes.");
            if (oversized)
            {
                for (var index = 0; index < 8; index++)
                {
                    await File.WriteAllTextAsync(Path.Combine(root.FullName, ".codai", "rules", $"extra{index}.md"),
                        "---\napplyTo: src/**/*.cs\n---\n" + new string('x', 8192));
                }
            }

            var options = OptionsFor();
            options.AiProviderConfig.Stream = stream;
            var ui = new FakeUserInterface();
            ui.EnqueueConfirmation(true);
            var client = new RuleReviewClient(root.FullName, mode == AgentMode.Agent, ui);
            var services = new ServiceCollection();
            services.AddSingleton<IUserInterface>(ui);
            services.AddCodaiApplication(options, root.FullName, mode: mode);
            services.Replace(ServiceDescriptor.Singleton<IChatClientFactory>(client));
            var scanner = new FakeProjectScanner { Failure = new IOException("Automatic scanning must not run.") };
            services.Replace(ServiceDescriptor.Singleton<IProjectScanner>(scanner));
            await using var provider = services.BuildServiceProvider();

            var exitCode = await provider.GetRequiredService<ICodaiSession>().RunTaskAsync("Inspect relevant source files");
            scanner.ScannedRoots.Should().BeEmpty();
            provider.GetRequiredService<RequestRuleGuidance>().RenderActivated().Should().BeNull();
            if (oversized)
            {
                exitCode.Should().Be(1);
                client.Calls.Should().Be(1);
                File.ReadAllText(Path.Combine(root.FullName, "src", "A.cs")).Should().Be("original");
                File.ReadAllText(Path.Combine(root.FullName, "other.txt")).Should().Be("other");
                ui.Prompts.Should().BeEmpty();
                (await provider.GetRequiredService<IAgentTaskStore>().LoadAsync(root.FullName))!.Status
                    .Should().Be(AgentTaskStatus.Failed);
                return;
            }

            exitCode.Should().Be(0);

            client.Calls.Should().Be(3);
            client.ReviewReceived.Should().BeTrue();
            File.ReadAllText(Path.Combine(root.FullName, "src", "A.cs")).Should()
                .Be(mode == AgentMode.Agent ? "reviewed" : "original");
            File.ReadAllText(Path.Combine(root.FullName, "other.txt")).Should().Be("other");
            ui.Prompts.Should().HaveCount(mode == AgentMode.Agent ? 1 : 0);
            provider.GetRequiredService<AgentModeState>().Mode.Should().Be(mode);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    private sealed class RuleReviewClient(string root, bool write, FakeUserInterface ui) : IChatClient, IChatClientFactory
    {
        public int Calls { get; private set; }
        public bool ReviewReceived { get; private set; }
        public IChatClient CreateClient(ResolvedProviderOptions options) => this;
        public IChatCompletionService CreateService(ResolvedProviderOptions options, IToolExecutor? toolExecutor = null) =>
            new ChatCompletionService(this, options, toolExecutor);
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }

        private IList<AIContent> Next(IEnumerable<ChatMessage> messages)
        {
            Calls++;
            var results = messages.SelectMany(message => message.Contents).OfType<FunctionResultContent>().ToArray();
            if (Calls == 1)
            {
                return [FileCall("initial", "src/A.cs", "unreviewed"), FileCall("later", "other.txt", "unreviewed")];
            }

            if (Calls == 2)
            {
                ui.Prompts.Should().BeEmpty();
                File.ReadAllText(Path.Combine(root, "src", "A.cs")).Should().Be("original");
                File.ReadAllText(Path.Combine(root, "other.txt")).Should().Be("other");
                results.Single(result => result.CallId == "initial").Result!.ToString().Should()
                    .Contain("API rule must be reviewed").And.Contain("Tool was not executed");
                results.Single(result => result.CallId == "later").Result!.ToString().Should().Contain("Tool not executed");
                ReviewReceived = true;
                return [FileCall("reviewed", "src/A.cs", "reviewed")];
            }

            Calls.Should().Be(3);
            results.Single(result => result.CallId == "reviewed").Result!.ToString().Should().NotContain("Tool was not executed");
            return [new TextContent("Finished after reviewing applicable rules.")];
        }

        private FunctionCallContent FileCall(string id, string path, string content) =>
            new(id, write ? "write_file" : "read_file", write
                ? new Dictionary<string, object?> { ["path"] = path, ["content"] = content }
                : new Dictionary<string, object?> { ["path"] = path });

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, Next(messages))));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, Next(messages));
            await Task.CompletedTask;
        }
    }

    private static CodaiOptions OptionsFor(string provider = "ollama") => new()
    {
        Theme = CodaiDefaults.Theme,
        AiProviderConfig = new AiProviderOptions
        {
            Provider = provider,
            Model = string.Empty,
            BaseUrl = "http://localhost:11434/api",
        },
    };

    [Fact]
    public void Reports_tool_start_and_completion_through_the_user_interface()
    {
        var ui = new FakeUserInterface();
        var observer = new InteractiveToolObserver(ui);

        observer.OnToolStarted("grep", "grep(pattern=TODO)");
        observer.OnToolCompleted("grep", succeeded: true, "grep(pattern=TODO)");

        ui.Infos.Should().Equal("  • grep(pattern=TODO)", "  ✓ grep");
    }

    [Fact]
    public void Reports_a_failed_tool_with_its_reason()
    {
        var ui = new FakeUserInterface();
        var observer = new InteractiveToolObserver(ui);

        observer.OnToolCompleted("read_file", succeeded: false, "File 'a.cs' does not exist.");

        ui.Infos.Should().ContainSingle().Which.Should()
            .Be("  ✗ read_file: File 'a.cs' does not exist.");
    }

    [Fact]
    public void Reports_a_denied_tool()
    {
        var ui = new FakeUserInterface();
        var observer = new InteractiveToolObserver(ui);

        observer.OnToolDenied("write_file", "write_file(path=a.txt)");

        ui.Infos.Should().ContainSingle().Which.Should().Be("  ✗ write_file declined");
    }

    [Fact]
    public async Task The_approver_asks_the_user_and_returns_the_answer()
    {
        var ui = new FakeUserInterface();
        ui.EnqueueConfirmation(true);
        var approver = new InteractiveToolApprover(ui);

        var approved = await approver.ApproveAsync(
            new ToolApprovalRequest("write_file", "Writes a file.", "write_file(path=a.txt)"));

        approved.Should().BeTrue();
        ui.Prompts.Should().ContainSingle()
            .Which.Should().Be("Allow write_file? write_file(path=a.txt)");
    }

    [Fact]
    public async Task The_approver_asks_about_the_call_and_not_the_tool_name_alone()
    {
        var ui = new FakeUserInterface();
        ui.EnqueueConfirmation(true);

        await new InteractiveToolApprover(ui).ApproveAsync(new ToolApprovalRequest(
            "run_command", "Run a shell command in the project.", "run_command(command=dotnet test)",
            "Run a shell command in the project.", "dotnet test"));

        ui.Prompts.Should().ContainSingle()
            .Which.Should().Be("Allow run_command? Run a shell command in the project.");
    }

    [Fact]
    public async Task A_user_who_says_not_again_is_not_asked_again_for_that_tool()
    {
        var ui = new RememberingUserInterface(ApprovalDecision.AllowAlways);
        var approver = new InteractiveToolApprover(ui);
        var request = new ToolApprovalRequest("run_command", "Run a shell command.", "run_command(command=ls)");

        (await approver.ApproveAsync(request)).Should().BeTrue();
        (await approver.ApproveAsync(request)).Should().BeTrue();

        ui.Asks.Should().Be(1);
    }

    [Fact]
    public async Task Remembering_one_tool_does_not_approve_another()
    {
        var ui = new RememberingUserInterface(ApprovalDecision.AllowAlways);
        var approver = new InteractiveToolApprover(ui);

        await approver.ApproveAsync(new ToolApprovalRequest("run_command", "Run a shell command.", "run_command(command=ls)"));
        await approver.ApproveAsync(new ToolApprovalRequest("write_file", "Write a file.", "write_file(path=a.txt)"));

        ui.Asks.Should().Be(2);
    }

    [Fact]
    public async Task The_approver_returns_false_when_the_user_declines()
    {
        var ui = new FakeUserInterface();
        ui.EnqueueConfirmation(false);

        (await new InteractiveToolApprover(ui).ApproveAsync(
            new ToolApprovalRequest("write_file", "Writes a file.", "write_file(path=a.txt)")))
            .Should().BeFalse();
    }

    private sealed class RememberingUserInterface(ApprovalDecision decision) : IUserInterface
    {
        public int Asks { get; private set; }

        public Task<ApprovalDecision> ApproveAsync(ToolApprovalRequest request, CancellationToken cancellationToken)
        {
            Asks++;
            return Task.FromResult(decision);
        }

        public Task<string?> ReadInputAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);

        public Task<bool> ConfirmAsync(string question, CancellationToken cancellationToken = default) => Task.FromResult(false);

        public void WriteLine(string message) { }

        public void WriteError(string message) { }

        public void WriteSuccess(string message) { }

        public void WriteInfo(string message) { }

        public void WriteMarkdown(string markdown) { }

        public Task<T> RunWithProgressAsync<T>(string label, Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken = default) =>
            work(cancellationToken);

        public void ShowTokens(string model, Core.Tokens.TokenUsage session, Core.Tokens.TokenUsage last, decimal cost) { }

        public void Clear() { }
    }

    [Fact]
    public void A_host_with_a_user_interface_gets_the_interactive_approver()
    {
        var ui = new FakeUserInterface();
        var services = new ServiceCollection();
        services.AddSingleton<IUserInterface>(ui);
        services.AddCodaiApplication(OptionsFor(), Directory.GetCurrentDirectory());

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IToolApprover>().Should().BeOfType<InteractiveToolApprover>();

        // The observer is decorated: what reports to the user is wrapped by the one that
        // records which files the session touched, so a compaction can restore them.
        provider.GetRequiredService<IToolObserver>()
            .Should().BeOfType<FootprintObserver>()
            .Which.Inner.Should().BeOfType<InteractiveToolObserver>();
    }

    [Fact]
    public void A_host_without_a_user_interface_keeps_the_safe_defaults()
    {
        var services = new ServiceCollection();
        services.AddCodaiApplication(OptionsFor(), Directory.GetCurrentDirectory());

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IToolApprover>().Should().BeSameAs(DenyAllToolApprover.Instance);
        provider.GetRequiredService<IToolObserver>()
            .Should().BeOfType<FootprintObserver>()
            .Which.Inner.Should().BeSameAs(NullToolObserver.Instance);
    }

    [Fact]
    public void The_session_offers_the_read_only_tools_and_the_editing_tools()
    {
        var services = new ServiceCollection();
        services.AddCodaiApplication(OptionsFor(), Directory.GetCurrentDirectory());

        using var provider = services.BuildServiceProvider();
        var executor = provider.GetRequiredService<IToolExecutor>();

        executor.Tools.Select(tool => tool.Name).Should().Equal(
            "read_file", "list_files", "glob", "grep", "read_skill", "skills", "read_skill_resource",
            "submit_task", "ask_question", "fetch_web", "git_status", "git_diff", "git_log", "write_file",
            "edit_file", "apply_patch");

        // The shell tool is the one capability a host has to ask for, so it must not be
        // here by default.
        executor.Tools.Should().NotContain(tool => tool.Name == "run_command");
    }

    [Fact]
    public void The_session_offers_the_shell_tool_only_when_the_host_allows_it()
    {
        var services = new ServiceCollection();
        services.AddCodaiApplication(
            OptionsFor(),
            Directory.GetCurrentDirectory(),
            allowShell: true);

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IToolExecutor>().Tools
            .Should().Contain(tool => tool.Name == "run_command" && !tool.IsReadOnly);
    }

    [Fact]
    public async Task The_session_executor_reaches_the_real_file_system()
    {
        var root = Path.Combine(Path.GetTempPath(), "codai-session-tools-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "src"));
        await File.WriteAllTextAsync(Path.Combine(root, "src", "hello.cs"), "class Hello { }\n");

        try
        {
            var services = new ServiceCollection();
            services.AddSingleton<IUserInterface>(new FakeUserInterface());
            services.AddCodaiApplication(OptionsFor(), root);

            using var provider = services.BuildServiceProvider();
            var executor = provider.GetRequiredService<IToolExecutor>();

            var listed = await executor.ExecuteAsync("list_files", ToolTestArgs.Parse("""{"path":"src"}"""));
            listed.IsSuccess.Should().BeTrue();
            listed.Content.Should().Contain("hello.cs");

            var read = await executor.ExecuteAsync(
                "read_file",
                ToolTestArgs.Parse("""{"path":"src/hello.cs"}"""));

            read.IsSuccess.Should().BeTrue();
            read.Content.Should().Contain("class Hello { }");

            var missing = await executor.ExecuteAsync(
                "read_file",
                ToolTestArgs.Parse("""{"path":"src/definitely-not-here.cs"}"""));

            missing.IsSuccess.Should().BeFalse();
            missing.Content.Should().Contain("does not exist");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task An_unknown_tool_returns_a_failure_rather_than_throwing()
    {
        var services = new ServiceCollection();
        services.AddCodaiApplication(OptionsFor(), Directory.GetCurrentDirectory());

        using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<IToolExecutor>()
            .ExecuteAsync("nope", ToolTestArgs.Parse("{}"));

        result.IsSuccess.Should().BeFalse();
        result.Content.Should().Contain("Unknown tool");
    }

    [Fact]
    public void The_runner_reports_progress_through_the_registered_observer()
    {
        var ui = new FakeUserInterface();
        var services = new ServiceCollection();
        services.AddSingleton<IUserInterface>(ui);
        services.AddCodaiApplication(OptionsFor(), Directory.GetCurrentDirectory());

        using var provider = services.BuildServiceProvider();
        var runner = provider.GetRequiredService<ToolRunner>();

        runner.Tools.Should().HaveCount(16);
    }

    [Fact]
    public async Task A_tool_the_request_does_not_offer_is_refused_without_asking()
    {
        var ui = new FakeUserInterface();
        var services = new ServiceCollection();
        services.AddSingleton<IUserInterface>(ui);
        services.AddCodaiApplication(OptionsFor(), Directory.GetCurrentDirectory());

        using var provider = services.BuildServiceProvider();

        // Resolved separately on purpose: this doubles as the wiring assertion. The
        // executor and whoever begins the request have to be looking at one policy, or
        // the restriction is recorded where nothing reads it.
        provider.GetRequiredService<ToolAccessPolicy>().BeginRequest(["read_file"]);

        var result = await provider.GetRequiredService<IToolExecutor>().ExecuteAsync(
            "write_file",
            ToolTestArgs.Parse("""{"path":"a.txt","content":"hi"}"""));

        result.IsSuccess.Should().BeFalse();
        result.Content.Should().Contain("not available for this request");

        // Refused before approval, so the user is not asked about a call that could
        // never have run.
        ui.Prompts.Should().BeEmpty();
    }

    [Fact]
    public async Task Without_a_restriction_a_write_tool_still_asks_for_approval()
    {
        var ui = new FakeUserInterface();
        var services = new ServiceCollection();
        services.AddSingleton<IUserInterface>(ui);
        services.AddCodaiApplication(OptionsFor(), Directory.GetCurrentDirectory());

        using var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<IToolExecutor>().ExecuteAsync(
            "write_file",
            ToolTestArgs.Parse("""{"path":"a.txt","content":"hi"}"""));

        ui.Prompts.Should().ContainSingle().Which.Should().Contain("write_file");
    }

    [Theory]
    [InlineData(true, AgentMode.Agent)]
    [InlineData(false, AgentMode.Agent)]
    [InlineData(true, AgentMode.Plan)]
    [InlineData(false, AgentMode.Plan)]
    public async Task Disabled_tools_are_hidden_from_provider_and_blocked_before_approval(bool stream, AgentMode mode)
    {
        var root = Directory.CreateTempSubdirectory("codai-disabled-tools-");
        try
        {
            var options = OptionsFor();
            options.AiProviderConfig.Stream = stream;
            options.DisabledTools = ["write_file", "fetch_web"];
            var ui = new FakeUserInterface();
            ui.EnqueueConfirmation(true);
            var client = new SubmissionClient();
            var services = new ServiceCollection();
            services.AddSingleton<IUserInterface>(ui);
            services.AddCodaiApplication(options, root.FullName, mode: mode);
            services.Replace(ServiceDescriptor.Singleton<IChatClientFactory>(client));
            await using var provider = services.BuildServiceProvider();

            (await provider.GetRequiredService<ICodaiSession>().RunTaskAsync("task")).Should().Be(0);

            client.OfferedTools.Should().ContainSingle().Which.Should().NotContain(["write_file", "fetch_web"]);
            var result = await provider.GetRequiredService<IToolExecutor>().ExecuteAsync("write_file",
                ToolTestArgs.Parse("""{"path":"result.txt","content":"forbidden"}"""));

            result.IsSuccess.Should().BeFalse();
            File.Exists(Path.Combine(root.FullName, "result.txt")).Should().BeFalse();
            ui.Prompts.Should().BeEmpty();
            ui.Errors.Should().BeEmpty();
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(true, AgentMode.Plan, false)]
    [InlineData(false, AgentMode.Plan, false)]
    [InlineData(true, AgentMode.Agent, false)]
    [InlineData(false, AgentMode.Agent, false)]
    [InlineData(true, AgentMode.Agent, true)]
    public async Task Explicit_submission_persists_completed_task_and_final_text(bool stream, AgentMode mode, bool interactive)
    {
        var root = Directory.CreateTempSubdirectory("codai-submit-");
        try
        {
            var options = OptionsFor();
            options.AiProviderConfig.Stream = stream;
            var ui = new FakeUserInterface();
            if (interactive)
            {
                ui.EnqueueInput("task");
                ui.EnqueueInput(null);
            }

            var client = new SubmissionClient();
            var services = new ServiceCollection();
            services.AddSingleton<IUserInterface>(ui);
            services.AddCodaiApplication(options, root.FullName, mode: mode);
            services.Replace(ServiceDescriptor.Singleton<IChatClientFactory>(client));
            await using var provider = services.BuildServiceProvider();
            var session = provider.GetRequiredService<ICodaiSession>();

            var exitCode = interactive ? await session.RunAsync() : await session.RunTaskAsync("task");

            ui.Errors.Should().BeEmpty();
            exitCode.Should().Be(0);
            client.Calls.Should().Be(1);
            (await provider.GetRequiredService<IAgentTaskStore>().LoadAsync(root.FullName))!.Status
                .Should().Be(AgentTaskStatus.Completed);
            (await provider.GetRequiredService<ISessionStore>().LoadAsync(root.FullName))!
                .Should().ContainSingle().Which.AiResponse.Should().Be("Final submission.");
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(true, AgentMode.Plan, "2", "PostgreSQL")]
    [InlineData(false, AgentMode.Plan, "custom database", "custom database")]
    [InlineData(true, AgentMode.Agent, null, null)]
    [InlineData(false, AgentMode.Agent, null, null)]
    public async Task Clarification_resumes_only_after_real_answer_and_persists_outcome(bool stream, AgentMode mode, string? input, string? expected)
    {
        var root = Directory.CreateTempSubdirectory("codai-question-");
        try
        {
            var options = OptionsFor();
            options.AiProviderConfig.Stream = stream;
            var ui = new FakeUserInterface();
            ui.EnqueueInput(input);
            var client = new SubmissionClient(askQuestion: true);
            var services = new ServiceCollection();
            services.AddSingleton<IUserInterface>(ui);
            services.AddCodaiApplication(options, root.FullName, mode: mode);
            services.Replace(ServiceDescriptor.Singleton<IChatClientFactory>(client));
            await using var provider = services.BuildServiceProvider();

            var exitCode = await provider.GetRequiredService<ICodaiSession>().RunTaskAsync("task");

            exitCode.Should().Be(expected is null ? 1 : 0);
            client.Calls.Should().Be(expected is null ? 1 : 2);
            client.Answer.Should().Be(expected);
            ui.Lines.Should().Contain("Which database?");
            ui.Prompts.Should().BeEmpty();
            (await provider.GetRequiredService<IAgentTaskStore>().LoadAsync(root.FullName))!.Status
                .Should().Be(expected is null ? AgentTaskStatus.Failed : AgentTaskStatus.Completed);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Clarification_cancellation_does_not_read_or_approve_input()
    {
        var ui = new FakeUserInterface();
        var handler = new InteractiveClarificationHandler(ui);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        Func<Task> ask = () => handler.AskAsync("Which database?", [], cancellation.Token);

        await ask.Should().ThrowAsync<OperationCanceledException>();
        ui.ReadRequests.Should().BeEmpty();
        ui.Prompts.Should().BeEmpty();
    }

    [Fact]
    public void Composition_preserves_host_clarification_handler()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IUserInterface>(new FakeUserInterface());
        services.AddSingleton<IClarificationHandler>(UnavailableClarificationHandler.Instance);
        services.AddCodaiApplication(OptionsFor(), Directory.GetCurrentDirectory());
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IClarificationHandler>().Should().BeSameAs(UnavailableClarificationHandler.Instance);
    }

    [Theory]
    [InlineData(AgentMode.Plan)]
    [InlineData(AgentMode.Agent)]
    public async Task Web_tool_is_available_in_all_modes_but_rejects_unsafe_destinations(AgentMode mode)
    {
        var services = new ServiceCollection();
        services.AddCodaiApplication(OptionsFor(), Directory.GetCurrentDirectory(), mode: mode);
        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IToolRegistry>();
        var allowed = AgentModePolicy.AllowedTools(mode, registry.Tools);
        provider.GetRequiredService<ToolAccessPolicy>().BeginRequest(allowed);
        allowed.Should().Contain("fetch_web");

        var result = await provider.GetRequiredService<IToolExecutor>().ExecuteAsync("fetch_web",
            ToolTestArgs.Parse("{\"url\":\"http://127.0.0.1/private\"}"));

        result.IsSuccess.Should().BeFalse();
        result.Content.Should().Contain("public HTTP(S)");
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    public async Task Reviewed_plan_execution_shares_mode_but_keeps_write_consent_independent(bool stream, bool transition, bool write)
    {
        var root = Directory.CreateTempSubdirectory("codai-plan-act-");
        try
        {
            var options = OptionsFor();
            options.AiProviderConfig.Stream = stream;
            var ui = new FakeUserInterface();
            var client = new SubmissionClient(planAct: true);
            var services = new ServiceCollection();
            services.AddSingleton<IUserInterface>(ui);
            services.AddCodaiApplication(options, root.FullName, mode: AgentMode.Plan);
            services.Replace(ServiceDescriptor.Singleton<IChatClientFactory>(client));
            await using var provider = services.BuildServiceProvider();
            var session = provider.GetRequiredService<ICodaiSession>();

            (await session.RunTaskAsync("Plan creation of result.txt")).Should().Be(0);
            var proposal = await provider.GetRequiredService<IAgentTaskStore>().LoadAsync(root.FullName);
            proposal!.ProposedPlan.Should().Be("1. Create result.txt with approved content.");
            File.Exists(Path.Combine(root.FullName, "result.txt")).Should().BeFalse();
            client.OfferedTools[0].Should().NotContain("write_file");
            ui.EnqueueConfirmation(transition);
            ui.EnqueueConfirmation(write);

            (await session.ExecutePlanAsync("Run required checks.")).Should().Be(transition ? 0 : 1);

            File.Exists(Path.Combine(root.FullName, "result.txt")).Should().Be(transition && write);
            provider.GetRequiredService<AgentModeState>().Mode.Should().Be(transition ? AgentMode.Agent : AgentMode.Plan);
            var latest = await provider.GetRequiredService<IAgentTaskStore>().LoadAsync(root.FullName);
            latest!.SourcePlanId.Should().Be(transition ? proposal.Id : null);
            client.Calls.Should().Be(transition ? 3 : 1);
            if (transition)
            {
                client.OfferedTools[1].Should().Contain("write_file");
                client.PlanningContextReceived.Should().BeTrue();
                (await session.ExecutePlanAsync()).Should().Be(1);
                client.Calls.Should().Be(3);
            }
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Live_command_cli_cancellation_after_output_stops_process_and_persists_abort(bool timeout)
    {
        var root = Directory.CreateTempSubdirectory("codai-cli-command-cancel-");
        ServiceProvider? executionProvider = null;
        using var cancellation = new CancellationTokenSource();
        try
        {
            var separator = OperatingSystem.IsWindows() ? " & " : "; ";
            var slow = OperatingSystem.IsWindows() ? "ping -n 8 127.0.0.1 > nul" : "sleep 7";
            var client = new SubmissionClient(command: "echo started" + separator + slow + separator + "echo orphan > cancelled-marker.txt");
            var output = new PlanConsole
            {
                OnLine = line =>
                {
                    var entry = JsonSerializer.Deserialize<JsonElement>(line);
                    if (!timeout && entry.TryGetProperty("event", out var progress) &&
                        progress.TryGetProperty("kind", out var kind) &&
                        kind.GetString() == nameof(AgentProgressEventKind.CommandStandardOutput))
                    {
                        cancellation.Cancel();
                    }
                },
            };
            var app = new CliApplication(output, () => root.FullName, input: new PlanInput(), sessionFactory: host =>
            {
                var services = new ServiceCollection();
                services.AddSingleton(host.GetRequiredService<IUserInterface>());
                services.AddCodaiApplication(OptionsFor(), root.FullName, allowShell: true, allowSubAgents: false);
                services.Replace(ServiceDescriptor.Singleton<IChatClientFactory>(client));
                services.Replace(ServiceDescriptor.Singleton<IAgentProgressSink>(host.GetRequiredService<IAgentProgressSink>()));
                executionProvider = services.BuildServiceProvider();
                return executionProvider.GetRequiredService<ICodaiSession>();
            });
            List<string> args = ["--json", "--provider", "ollama", "--no-hooks", "--no-sub-agents", "--auto-approve", "true", "Run command"];
            if (timeout)
            {
                args.AddRange(["--timeout", "2"]);
            }

            (await app.RunAsync([.. args], cancellation.Token)).Should().Be(timeout ? 124 : 130);

            File.Exists(Path.Combine(root.FullName, "cancelled-marker.txt")).Should().BeFalse();
            client.Calls.Should().Be(1);
            var runtime = executionProvider ?? throw new InvalidOperationException("Command runtime was not constructed.");
            (await runtime.GetRequiredService<IAgentTaskStore>().LoadAsync(root.FullName))!.Status.Should().Be(AgentTaskStatus.Cancelled);
            var events = output.Lines.Select(line => JsonSerializer.Deserialize<JsonElement>(line)).ToArray();
            events.Should().ContainSingle(entry => entry.GetProperty("type").GetString() == "run_result")
                .Which.GetProperty("finishReason").GetString().Should().Be("aborted");
            events[^1].GetProperty("type").GetString().Should().Be("run_aborted");
            events[^1].GetProperty("reason").GetString().Should().Be(timeout ? "timeout" : "cancelled");
        }
        finally
        {
            if (executionProvider is not null)
            {
                await executionProvider.DisposeAsync();
            }

            root.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task Live_command_cli_reports_streams_before_completion_and_preserves_consent(bool stream, bool approval)
    {
        var root = Directory.CreateTempSubdirectory("codai-cli-command-");
        ServiceProvider? executionProvider = null;
        try
        {
            var separator = OperatingSystem.IsWindows() ? " & " : "; ";
            var command = "echo live-stdout" + separator + "echo live-stderr 1>&2" + separator + "echo executed > executed.txt";
            var client = new SubmissionClient(command: command);
            var output = new PlanConsole();
            var app = new CliApplication(output, () => root.FullName, input: new PlanInput(), sessionFactory: host =>
            {
                var options = OptionsFor();
                options.AiProviderConfig.Stream = stream;
                var services = new ServiceCollection();
                services.AddSingleton(host.GetRequiredService<IUserInterface>());
                services.AddCodaiApplication(options, root.FullName, allowShell: true, allowSubAgents: false);
                services.Replace(ServiceDescriptor.Singleton<IChatClientFactory>(client));
                services.Replace(ServiceDescriptor.Singleton<IAgentProgressSink>(host.GetRequiredService<IAgentProgressSink>()));
                executionProvider = services.BuildServiceProvider();
                return executionProvider.GetRequiredService<ICodaiSession>();
            });

            (await app.RunAsync(["--json", "--provider", "ollama", "--no-hooks", "--no-sub-agents",
                "--auto-approve", approval ? "true" : "false", "Run command"]))
                .Should().Be(0);

            File.Exists(Path.Combine(root.FullName, "executed.txt")).Should().Be(approval);
            client.Calls.Should().Be(2);
            var events = output.Lines.Select(line => JsonSerializer.Deserialize<JsonElement>(line)).ToArray();
            var progress = events.Where(entry => entry.GetProperty("type").GetString() == "agent_event" &&
                entry.GetProperty("event").GetProperty("type").GetString() == "progress")
                .Select(entry => entry.GetProperty("event")).ToArray();
            var chunks = progress.Where(entry => entry.GetProperty("kind").GetString() is "CommandStandardOutput" or "CommandStandardError")
                .ToArray();
            if (approval)
            {
                chunks.Should().Contain(entry => entry.GetProperty("kind").GetString() == "CommandStandardOutput" &&
                    entry.GetProperty("text").GetString()!.Contains("live-stdout"));
                chunks.Should().Contain(entry => entry.GetProperty("kind").GetString() == "CommandStandardError" &&
                    entry.GetProperty("text").GetString()!.Contains("live-stderr"));
                var started = Array.FindIndex(progress, entry => entry.GetProperty("kind").GetString() == "ToolStarted" &&
                    entry.GetProperty("description").GetString() == "run_command");
                var completed = Array.FindIndex(progress, entry => entry.GetProperty("kind").GetString() == "ToolCompleted" &&
                    entry.GetProperty("description").GetString() == "run_command");
                foreach (var chunk in chunks)
                {
                    var index = Array.IndexOf(progress, chunk);
                    index.Should().BeGreaterThan(started).And.BeLessThan(completed);
                }

                client.Answer.Should().Contain("live-stdout").And.Contain("live-stderr").And.Contain("Exit code 0");
            }
            else
            {
                chunks.Should().BeEmpty();
                progress.Should().Contain(entry => entry.GetProperty("kind").GetString() == "ToolDenied");
            }

            events.Should().ContainSingle(entry => entry.GetProperty("type").GetString() == "run_result")
                .Which.GetProperty("finishReason").GetString().Should().Be("completed");
        }
        finally
        {
            if (executionProvider is not null)
            {
                await executionProvider.DisposeAsync();
            }

            root.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Saved_plan_cli_execution_uses_headless_consent_and_reports_json_outcome(bool approval)
    {
        var root = Directory.CreateTempSubdirectory("codai-cli-plan-");
        ServiceProvider? executionProvider = null;
        try
        {
            var client = new SubmissionClient(planAct: true);
            var planningServices = new ServiceCollection();
            planningServices.AddSingleton<IUserInterface>(new FakeUserInterface());
            planningServices.AddCodaiApplication(OptionsFor(), root.FullName, mode: AgentMode.Plan);
            planningServices.Replace(ServiceDescriptor.Singleton<IChatClientFactory>(client));
            await using var planningProvider = planningServices.BuildServiceProvider();
            (await planningProvider.GetRequiredService<ICodaiSession>().RunTaskAsync("Plan creation of result.txt"))
                .Should().Be(0);
            var proposal = await planningProvider.GetRequiredService<IAgentTaskStore>().LoadAsync(root.FullName);
            var output = new PlanConsole();
            var app = new CliApplication(output, () => root.FullName, input: new PlanInput(), sessionFactory: host =>
            {
                var services = new ServiceCollection();
                services.AddSingleton(host.GetRequiredService<IUserInterface>());
                services.AddCodaiApplication(OptionsFor(), root.FullName, mode: host.GetRequiredService<SessionOptions>().Mode);
                services.Replace(ServiceDescriptor.Singleton<IChatClientFactory>(client));
                services.Replace(ServiceDescriptor.Singleton(host.GetRequiredService<IAgentProgressSink>()));
                executionProvider = services.BuildServiceProvider();
                return executionProvider.GetRequiredService<ICodaiSession>();
            });

            var exitCode = await app.RunAsync(["--execute-plan", "--mode", "plan", "--json", "--provider", "ollama",
                "--auto-approve", approval ? "true" : "false", "--no-hooks", "--no-sub-agents", "Run required checks."]);

            exitCode.Should().Be(approval ? 0 : 1);
            File.Exists(Path.Combine(root.FullName, "result.txt")).Should().Be(approval);
            client.Calls.Should().Be(approval ? 3 : 1);
            var runtime = executionProvider ?? throw new InvalidOperationException("Execution runtime was not constructed.");
            var latest = await runtime.GetRequiredService<IAgentTaskStore>().LoadAsync(root.FullName);
            latest!.SourcePlanId.Should().Be(approval ? proposal!.Id : null);
            runtime.GetRequiredService<AgentModeState>().Mode.Should().Be(approval ? AgentMode.Agent : AgentMode.Plan);
            var final = output.Lines.Select(line => JsonSerializer.Deserialize<JsonElement>(line))
                .Single(entry => entry.GetProperty("type").GetString() == "run_result");
            final.GetProperty("finishReason").GetString().Should().Be(approval ? "completed" : "failed");
            final.GetProperty("exitCode").GetInt32().Should().Be(exitCode);
        }
        finally
        {
            if (executionProvider is not null)
            {
                await executionProvider.DisposeAsync();
            }

            root.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(true, AgentMode.Plan)]
    [InlineData(false, AgentMode.Plan)]
    [InlineData(true, AgentMode.Agent)]
    [InlineData(false, AgentMode.Agent)]
    public async Task Batch_read_cli_model_calls_preserve_modes_and_line_windows(bool stream, AgentMode mode)
    {
        var root = Directory.CreateTempSubdirectory("codai-cli-batch-read-");
        ServiceProvider? executionProvider = null;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root.FullName, "a.txt"), "ignored\nfirst sentinel\nmore");
            await File.WriteAllTextAsync(Path.Combine(root.FullName, "b.txt"), "ignored\nsecond sentinel\nmore");
            var client = new SubmissionClient(readBatch: true);
            var output = new PlanConsole();
            var app = new CliApplication(output, () => root.FullName, input: new PlanInput(), sessionFactory: host =>
            {
                var options = OptionsFor();
                options.AiProviderConfig.Stream = stream;
                var services = new ServiceCollection();
                services.AddSingleton(host.GetRequiredService<IUserInterface>());
                services.AddCodaiApplication(options, root.FullName, mode: host.GetRequiredService<SessionOptions>().Mode);
                services.Replace(ServiceDescriptor.Singleton<IChatClientFactory>(client));
                services.Replace(ServiceDescriptor.Singleton(host.GetRequiredService<IAgentProgressSink>()));
                executionProvider = services.BuildServiceProvider();
                return executionProvider.GetRequiredService<ICodaiSession>();
            });

            var exitCode = await app.RunAsync(["--json", "--provider", "ollama", "--mode", AgentModeParser.ToName(mode),
                "--no-hooks", "--no-sub-agents", "Read a.txt and b.txt in one call."]);

            exitCode.Should().Be(0);
            client.Calls.Should().Be(2);
            client.Answer.Should().Contain("a.txt (lines 2-2 of 3):").And.Contain("first sentinel")
                .And.Contain("b.txt (lines 2-2 of 3):").And.Contain("second sentinel").And.NotContain("ignored");
            var runtime = executionProvider ?? throw new InvalidOperationException("Execution runtime was not constructed.");
            runtime.GetRequiredService<AgentModeState>().Mode.Should().Be(mode);
            var task = await runtime.GetRequiredService<IAgentTaskStore>().LoadAsync(root.FullName);
            task!.Status.Should().Be(AgentTaskStatus.Completed);
            task.Mode.Should().Be(mode);
            var final = output.Lines.Select(line => JsonSerializer.Deserialize<JsonElement>(line))
                .Single(entry => entry.GetProperty("type").GetString() == "run_result");
            final.GetProperty("finishReason").GetString().Should().Be("completed");
        }
        finally
        {
            if (executionProvider is not null)
            {
                await executionProvider.DisposeAsync();
            }

            root.Delete(recursive: true);
        }
    }

    private sealed class PlanInput : IConsoleInput
    {
        public string? ReadLine() => throw new InvalidOperationException("Saved-plan CLI runs must not read terminal input.");
    }

    private sealed class PlanConsole : IConsoleOutput
    {
        public List<string> Lines { get; } = [];
        public Action<string>? OnLine { get; init; }
        public void WriteLine(string message)
        {
            Lines.Add(message);
            OnLine?.Invoke(message);
        }
        public void WriteError(string message) { }
    }

    private sealed class SubmissionClient(bool askQuestion = false, bool planAct = false, string? command = null,
        bool readBatch = false) : IChatClient, IChatClientFactory
    {
        public int Calls { get; private set; }
        public string? Answer { get; private set; }
        public List<string[]> OfferedTools { get; } = [];
        public bool PlanningContextReceived { get; private set; }

        public IChatClient CreateClient(ResolvedProviderOptions options) => this;

        public IChatCompletionService CreateService(ResolvedProviderOptions options, IToolExecutor? toolExecutor = null) =>
            new ChatCompletionService(this, options, toolExecutor);

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            CaptureToolsAndContext(messages, options);
            CaptureAnswer(messages);
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, [NextCall()])));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            CaptureToolsAndContext(messages, options);
            CaptureAnswer(messages);
            yield return new ChatResponseUpdate(ChatRole.Assistant, [NextCall()]);
            await Task.CompletedTask.ConfigureAwait(false);
        }

        private static FunctionCallContent Submission() =>
            new("final", "submit_task", new Dictionary<string, object?> { ["result"] = "Final submission." });

        private FunctionCallContent NextCall()
        {
            if (readBatch && Calls == 1)
            {
                return new("read", "read_file", new Dictionary<string, object?>
                {
                    ["paths"] = new[] { "a.txt", "b.txt" },
                    ["offset"] = 2,
                    ["limit"] = 1,
                });
            }

            if (command is not null && Calls == 1)
            {
                return new("command", "run_command", new Dictionary<string, object?> { ["command"] = command });
            }

            if (planAct && Calls == 1)
            {
                return new("plan", "submit_task", new Dictionary<string, object?> { ["result"] = "1. Create result.txt with approved content." });
            }

            if (planAct && Calls == 2)
            {
                return new("write", "write_file", new Dictionary<string, object?> { ["path"] = "result.txt", ["content"] = "approved" });
            }

            return askQuestion && Calls == 1
                ? new FunctionCallContent("question", "ask_question", new Dictionary<string, object?>
                {
                    ["question"] = "Which database?",
                    ["options"] = new[] { "SQLite", "PostgreSQL" },
                }) : Submission();
        }

        private void CaptureToolsAndContext(IEnumerable<ChatMessage> messages, ChatOptions? options)
        {
            OfferedTools.Add(options?.Tools?.OfType<AIFunction>().Select(tool => tool.Name).ToArray() ?? []);
            if (planAct && Calls > 1)
            {
                PlanningContextReceived |= messages.Any(message => message.Text.Contains("1. Create result.txt", StringComparison.Ordinal));
            }
        }

        private void CaptureAnswer(IEnumerable<ChatMessage> messages)
        {
            if (readBatch && Calls > 1)
            {
                Answer = messages.SelectMany(message => message.Contents).OfType<FunctionResultContent>()
                    .FirstOrDefault(result => result.CallId == "read")?.Result?.ToString();
            }

            if (command is not null && Calls > 1)
            {
                Answer = messages.SelectMany(message => message.Contents).OfType<FunctionResultContent>()
                    .FirstOrDefault(result => result.CallId == "command")?.Result?.ToString();
            }

            if (askQuestion && Calls > 1)
            {
                Answer = messages.SelectMany(message => message.Contents).OfType<FunctionResultContent>()
                    .FirstOrDefault(result => result.CallId == "question")?.Result?.ToString();
            }
        }
    }

    /// <summary>Argument parsing helper shared by the tool tests here.</summary>
    private static class ToolTestArgs
    {
        public static System.Text.Json.JsonElement Parse(string json) =>
            System.Text.Json.JsonDocument.Parse(json).RootElement.Clone();
    }
}
