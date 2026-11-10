using Codai.Cli;
using Codai.Cli.Ui;
using Codai.Core.Abstractions;
using Codai.Core.Agents;
using Codai.Core.Chat;
using Codai.Core.Tokens;
using Codai.Session.Tests.Fakes;
using FluentAssertions;
using Spectre.Console;

namespace Codai.Session.Tests;

public class CodeSessionTests
{
    [Theory]
    [InlineData("")]
    [InlineData("No WSO2 code in this repository. I will check the documentation.")]
    public async Task Failed_research_remains_in_history_for_tool_follow_up(string partialResponse)
    {
        const string question = "Does WSO2 support correlation IDs?";
        const string followUp = "Use MCP Context7 for finding it";
        const string failure = "Tool failure limit reached (3 consecutive failures).";
        var harness = new SessionHarness();
        harness.TaskStore = new FileAgentTaskStore(harness.FileSystem);
        harness.Chat.EnqueueThenFail(partialResponse, TokenUsage.Empty, new ChatProviderException(failure));
        harness.Chat.Enqueue("WSO2 supports correlation IDs.");
        var session = harness.Build();

        (await session.RunTaskAsync(question)).Should().Be(1);

        harness.History.Turns.Should().ContainSingle();
        harness.History.Turns[0].UserRequest.Should().Be(question);
        harness.History.Turns[0].AiResponse.Should().Contain("interrupted");
        if (partialResponse.Length > 0)
        {
            harness.History.Turns[0].AiResponse.Should().StartWith(partialResponse);
        }
        harness.Ui.Errors.Should().Contain(failure);
        harness.Ui.CompletedModelRequests.Should().Equal(harness.Ui.ModelRequests);
        var failedTask = await harness.TaskStore.LoadAsync(harness.Root);
        failedTask!.Status.Should().Be(AgentTaskStatus.Failed);
        var saved = await harness.SessionStore.LoadAsync(harness.Root, failedTask.Id);
        saved.Should().ContainSingle().Which.Should().Be(harness.History.Turns[0]);

        (await session.RunTaskAsync(followUp)).Should().Be(0);

        harness.Chat.Requests.Should().HaveCount(2);
        harness.Chat.Requests[1].UserMessage.Should().Contain(followUp).And.NotContain(question);
        harness.Chat.Requests[1].SystemPrompt.Should().Contain(question).And.Contain("interrupted");
        harness.History.Turns.Should().HaveCount(2);
        harness.History.Turns[1].UserRequest.Should().Be(followUp);
    }

    [Fact]
    public async Task Cancelled_response_retains_partial_history_without_reporting_success()
    {
        var harness = new SessionHarness();
        harness.TaskStore = new FileAgentTaskStore(harness.FileSystem);
        harness.Chat.EnqueueThenFail("Checking WSO2 documentation.", TokenUsage.Empty, new OperationCanceledException());
        harness.Chat.Enqueue("Continuing the lookup.");
        var session = harness.Build();

        (await session.RunTaskAsync("Does WSO2 support correlation IDs?")).Should().Be(130);
        harness.Ui.ModelRequests.Should().ContainSingle();
        harness.Ui.CompletedModelRequests.Should().Equal(harness.Ui.ModelRequests);

        harness.History.Turns.Should().ContainSingle().Which.AiResponse.Should()
            .StartWith("Checking WSO2 documentation.").And.Contain("interrupted");
        var task = await harness.TaskStore.LoadAsync(harness.Root);
        task!.Status.Should().Be(AgentTaskStatus.Cancelled);
        (await harness.SessionStore.LoadAsync(harness.Root, task.Id)).Should().ContainSingle();

        (await session.RunTaskAsync("Continue using Context7")).Should().Be(0);

        harness.Chat.Requests[1].SystemPrompt.Should().Contain("Does WSO2 support correlation IDs?");
        harness.History.Turns.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("src/File.cs", true)]
    [InlineData("notes.md", false)]
    public async Task Conditional_skill_instructions_only_reach_matching_file_requests(string file, bool expected)
    {
        var harness = new SessionHarness();
        harness.FileSystem.AddFile(Path.Combine(harness.Root, ".codai", "skills", "review", "SKILL.md"),
            "---\npaths:\n  - 'src/**/*.cs'\n---\nUNIQUE_PATH_SCOPED_SKILL_INSTRUCTIONS");
        harness.FileSystem.AddFile(Path.Combine(harness.Root, file), "File content.");
        harness.Chat.Enqueue("Reviewed.");
        harness.QueueInputs($"Review @{file}", ":exit");

        (await harness.Build().RunAsync()).Should().Be(0);

        harness.Chat.Requests.Should().ContainSingle();
        harness.Chat.Requests.Single().SystemPrompt.Contains("UNIQUE_PATH_SCOPED_SKILL_INSTRUCTIONS", StringComparison.Ordinal).Should().Be(expected);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Skills_selection_refreshes_advertised_skills_before_next_request(bool keepAudit)
    {
        var harness = new SessionHarness();
        harness.FileSystem.AddFile(Path.Combine(harness.Root, ".codai", "skills", "audit", "SKILL.md"),
            "---\ndescription: UNIQUE_AUDIT_GUIDANCE\n---\nUNIQUE_AUDIT_INSTRUCTIONS Audit changes.");
        harness.FileSystem.AddFile(Path.Combine(harness.Root, ".codai", "skills", "testing", "SKILL.md"),
            "---\ndescription: UNIQUE_TESTING_GUIDANCE\n---\nUNIQUE_TESTING_INSTRUCTIONS Run tests.");
        harness.Settings = new Codai.Application.SessionSettingsWorkflow(harness.Ui, harness.Inspector,
            new Codai.Application.Inspection.ConfigurationInspectionContext
            {
                ProjectRoot = harness.Root,
                ConfigurationDirectory = harness.Root,
                Provider = "ollama",
                Model = "local-model",
            }, harness.FileSystem, new Codai.Core.Customization.SkillCatalog(harness.FileSystem));
        harness.Ui.EnqueueChoice(keepAudit ? "[\"audit\"]" : "[]");
        harness.Chat.Enqueue("First answer.");
        harness.Chat.Enqueue("Second answer.");
        harness.Chat.Enqueue("Audit answer.");
        harness.QueueInputs("first request", "/skills", "second request", "/audit Inspect changes", ":exit");

        (await harness.Build().RunAsync()).Should().Be(0);

        harness.Chat.Requests.Should().HaveCount(3);
        harness.Ui.SkillSelections.First().Should().Equal("audit", "testing");
        harness.Ui.SkillSelections.Last().Should().Equal("audit", "testing");
        harness.Chat.Requests[2].UserMessage.Should().Contain("UNIQUE_AUDIT_INSTRUCTIONS").And.Contain("Inspect changes");
        (await new Codai.Core.Customization.SkillCatalog(harness.FileSystem).LoadAsync(harness.Root))
            .Select(skill => skill.Name).Should().Equal(keepAudit ? ["audit"] : []);
        harness.Chat.Requests[0].SystemPrompt.Should().Contain("UNIQUE_TESTING_GUIDANCE")
            .And.Contain("UNIQUE_AUDIT_INSTRUCTIONS").And.Contain("UNIQUE_TESTING_INSTRUCTIONS");
        harness.Chat.Requests[1].SystemPrompt.Should().NotContain("UNIQUE_TESTING_GUIDANCE").And.NotContain("UNIQUE_TESTING_INSTRUCTIONS");
        if (keepAudit)
        {
            harness.Chat.Requests[1].SystemPrompt.Should().Contain("UNIQUE_AUDIT_GUIDANCE").And.Contain("UNIQUE_AUDIT_INSTRUCTIONS");
            harness.Chat.Requests[2].SystemPrompt.Should().Contain("UNIQUE_AUDIT_INSTRUCTIONS").And.NotContain("UNIQUE_TESTING_INSTRUCTIONS");
        }
        else
        {
            harness.Chat.Requests[1].SystemPrompt.Should().NotContain("UNIQUE_AUDIT_GUIDANCE").And.NotContain("UNIQUE_AUDIT_INSTRUCTIONS");
            harness.Chat.Requests[2].SystemPrompt.Should().NotContain("UNIQUE_AUDIT_INSTRUCTIONS");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Checkpoint_storage_failure_fails_request_but_allows_next_request(bool denied)
    {
        var harness = new SessionHarness();
        harness.TaskStore = new FileAgentTaskStore(harness.FileSystem);
        Exception failure = denied ? new UnauthorizedAccessException("Checkpoint storage unavailable") : new IOException("Checkpoint storage unavailable");
        harness.Checkpoints = new CaptureFailureStore(new FileWorkspaceCheckpointStore(harness.FileSystem), failure);
        harness.Chat.Enqueue("recovered answer");
        harness.QueueInputs("first request", "second request");

        (await harness.Build().RunAsync()).Should().Be(0);

        harness.Ui.Errors.Should().Contain("Checkpoint storage unavailable");
        harness.Chat.Requests.Should().ContainSingle();
        harness.Ui.AssistantOutput.ToString().Should().Be("recovered answer");
        var tasks = await harness.TaskStore.ListAsync(harness.Root);
        tasks.Single(task => task.Request == "first request").Status.Should().Be(AgentTaskStatus.Failed);
        tasks.Single(task => task.Request == "second request").Status.Should().Be(AgentTaskStatus.Completed);
    }

    private sealed class CaptureFailureStore(IWorkspaceCheckpointStore inner, Exception failure) : IWorkspaceCheckpointStore
    {
        private bool _failed;

        public Task<WorkspaceCheckpoint> CaptureAsync(string projectRoot, string taskId, string label, CancellationToken cancellationToken = default)
        {
            if (!_failed)
            {
                _failed = true;
                throw failure;
            }
            return inner.CaptureAsync(projectRoot, taskId, label, cancellationToken);
        }

        public Task<IReadOnlyList<WorkspaceCheckpoint>> ListAsync(string projectRoot, string taskId, CancellationToken cancellationToken = default) =>
            inner.ListAsync(projectRoot, taskId, cancellationToken);

        public Task RestoreAsync(string projectRoot, string taskId, string checkpointId, CancellationToken cancellationToken = default) =>
            inner.RestoreAsync(projectRoot, taskId, checkpointId, cancellationToken);

        public Task<string> CompareAsync(string projectRoot, string taskId, string checkpointId, CancellationToken cancellationToken = default) =>
            inner.CompareAsync(projectRoot, taskId, checkpointId, cancellationToken);
    }

    [Fact]
    public async Task Automatic_checkpoint_success_is_silent_but_capture_failures_remain_visible()
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.No });
        using var ui = new SpectreUserInterface(console, new SystemConsoleInput());
        var sink = new InteractiveAgentProgressSink(ui);
        await sink.PublishAsync(new AgentProgressEvent("task-id", "checkpoint",
            AgentProgressEventKind.CheckpointCaptureCompleted, "checkpoint-id", DateTimeOffset.UtcNow));
        writer.ToString().Should().BeEmpty();
        await sink.PublishAsync(new AgentProgressEvent("task-id", "checkpoint",
            AgentProgressEventKind.CheckpointCaptureFailed, "Storage unavailable", DateTimeOffset.UtcNow));
        writer.ToString().Should().Contain("Checkpoint capture failed").And.Contain("Storage unavailable");
    }

    [Theory]
    [InlineData("new", 1)]
    [InlineData("fork", 2)]
    public async Task Conversation_switches_preserve_source_snapshot_and_create_independent_state(string operation, int expectedTurns)
    {
        var harness = new SessionHarness();
        harness.TaskStore = new FileAgentTaskStore(harness.FileSystem);
        harness.Chat.Enqueue("first answer");
        harness.Chat.Enqueue("second answer");
        harness.Ui.EnqueueSelection(items =>
        {
            items.Select(item => item.Label).Should().Equal("Continue", "Keep current conversation");
            items.Select(item => item.Value).Should().Equal("confirm", "cancel");
            return items[0].Value;
        });
        harness.QueueInputs("first question", ":session " + operation, "second question", ":exit");
        (await harness.Build().RunAsync()).Should().Be(0);
        harness.History.Turns.Should().HaveCount(expectedTurns);
        var tasks = await harness.TaskStore.ListAsync(harness.Root);
        var source = tasks.Single(task => task.Request == "first question");
        var snapshot = await harness.SessionStore.LoadAsync(harness.Root, source.Id);
        snapshot.Should().ContainSingle().Which.UserRequest.Should().Be("first question");
        var branch = tasks.Single(task => task.Request == (operation == "new" ? "Fresh conversation" : "Conversation branch"));
        var branchTurns = await harness.SessionStore.LoadAsync(harness.Root, branch.Id);
        branchTurns.Should().HaveCount(operation == "new" ? 0 : 1);
        harness.Chat.Requests.Should().HaveCount(2);
        if (operation == "new")
        {
            harness.Chat.Requests[1].SystemPrompt.Should().NotContain("first question");
        }
        else
        {
            harness.Chat.Requests[1].SystemPrompt.Should().Contain("first question");
        }
    }

    [Fact]
    public async Task Cancelling_fresh_conversation_keeps_history_and_creates_no_branch()
    {
        var harness = new SessionHarness();
        harness.TaskStore = new FileAgentTaskStore(harness.FileSystem);
        harness.Chat.Enqueue("answer");
        harness.Ui.EnqueueChoice("cancel");
        harness.QueueInputs("question", ":session new", ":exit");
        await harness.Build().RunAsync();
        harness.History.Turns.Should().ContainSingle();
        (await harness.TaskStore.ListAsync(harness.Root)).Should().ContainSingle();
    }

    [Fact]
    public async Task History_picker_continues_saved_conversation_in_new_branch_without_overwriting_source()
    {
        var harness = new SessionHarness();
        harness.TaskStore = new FileAgentTaskStore(harness.FileSystem);
        var source = (AgentTask.Create("saved conversation") with { Mode = AgentMode.Plan }).Complete();
        await harness.TaskStore.SaveAsync(harness.Root, source);
        await harness.SessionStore.SaveAsync(harness.Root, source.Id, [new ChatTurn(DateTimeOffset.UtcNow, "saved question", "saved answer")]);
        harness.Ui.EnqueueChoice(source.Id);
        harness.Chat.Enqueue("current answer");
        harness.Chat.Enqueue("continued answer");
        harness.QueueInputs("current question", ":session history", "follow-up", ":exit");
        await harness.Build().RunAsync();
        harness.Mode.Mode.Should().Be(AgentMode.Plan);
        harness.History.Turns.Should().HaveCount(2);
        harness.History.Turns[0].UserRequest.Should().Be("saved question");
        (await harness.SessionStore.LoadAsync(harness.Root, source.Id)).Should().ContainSingle();
        var current = (await harness.TaskStore.ListAsync(harness.Root)).Single(task => task.Request == "current question");
        (await harness.SessionStore.LoadAsync(harness.Root, current.Id)).Should().ContainSingle().Which.UserRequest.Should().Be("current question");
        harness.Chat.Requests[1].SystemPrompt.Should().Contain("saved question").And.NotContain("current question");
    }

    [Fact]
    public async Task Checkpoint_picker_restores_files_only_after_explicit_consent()
    {
        var harness = new SessionHarness();
        harness.TaskStore = new FileAgentTaskStore(harness.FileSystem);
        harness.Checkpoints = new FileWorkspaceCheckpointStore(harness.FileSystem);
        var task = AgentTask.Create("saved task").Complete();
        await harness.TaskStore.SaveAsync(harness.Root, task);
        var path = Path.Combine(harness.Root, "note.txt");
        harness.FileSystem.AddFile(path, "before");
        harness.FileSystem.CreateDirectory(Path.Combine(harness.Root, FileWorkspaceCheckpointStore.RelativeFolder, task.Id));
        await harness.Checkpoints.CaptureAsync(harness.Root, task.Id, "Earlier state");
        harness.FileSystem.AddFile(path, "after");
        harness.Ui.EnqueueChoice(task.Id);
        harness.Ui.EnqueueSelection(items => items[0].Value);
        harness.Ui.EnqueueChoice("files-only");
        harness.Ui.EnqueueChoice("confirm");
        harness.QueueInputs(":session restore", ":exit");
        await harness.Build().RunAsync();
        (await harness.FileSystem.ReadAllTextAsync(path)).Should().Be("before");
        harness.Ui.Errors.Should().BeEmpty();
        harness.History.Turns.Should().BeEmpty();
        harness.Ui.Prompts.Should().BeEmpty();
    }

    [Fact]
    public async Task Plan_response_is_durable_without_granting_writes()
    {
        var harness = new SessionHarness();
        harness.Mode.Mode = AgentMode.Plan;
        harness.TaskStore = new FileAgentTaskStore(harness.FileSystem);
        harness.Chat.Enqueue("1. Inspect Foo.\n2. Add a variable.");

        (await harness.Build().RunTaskAsync("Plan the change")).Should().Be(0);

        var task = await harness.TaskStore.LoadAsync(harness.Root);
        task!.Mode.Should().Be(AgentMode.Plan);
        task.ProposedPlan.Should().Be("1. Inspect Foo.\n2. Add a variable.");
        task.Status.Should().Be(AgentTaskStatus.Completed);
        harness.Chat.Requests.Single().AllowedTools.Should().NotContain("write_file");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Act_transition_requires_consent_and_keeps_independent_change_approval(bool accepted)
    {
        var harness = new SessionHarness();
        harness.Mode.Mode = AgentMode.Plan;
        harness.TaskStore = new FileAgentTaskStore(harness.FileSystem);
        harness.Chat.Enqueue("1. Add Foo.");
        var session = harness.Build();
        await session.RunTaskAsync("Plan Foo");
        var proposal = await harness.TaskStore.LoadAsync(harness.Root);
        harness.Ui.EnqueueConfirmation(accepted);
        harness.Ui.EnqueueConfirmation(false);
        harness.Chat.Enqueue("File: src/Foo.cs\n```csharp\n+var value = 1;\n```");

        var exitCode = await session.ExecutePlanAsync("Run checks afterwards.");

        exitCode.Should().Be(accepted ? 0 : 1);
        harness.Mode.Mode.Should().Be(accepted ? AgentMode.Agent : AgentMode.Plan);
        harness.Chat.Requests.Should().HaveCount(accepted ? 2 : 1);
        harness.FileSystem.FileExists(@"C:\project\src\Foo.cs").Should().BeFalse();
        if (accepted)
        {
            harness.Chat.Requests[1].SystemPrompt.Should().Contain("1. Add Foo.");
            harness.Chat.Requests[1].UserMessage.Should().Contain("Run checks afterwards.");
            (await harness.TaskStore.LoadAsync(harness.Root))!.SourcePlanId.Should().Be(proposal!.Id);
            harness.Ui.Prompts.Should().HaveCount(2);
            (await session.ExecutePlanAsync()).Should().Be(1);
            harness.Chat.Requests.Should().HaveCount(2);
        }
        else
        {
            (await harness.TaskStore.LoadAsync(harness.Root))!.Id.Should().Be(proposal!.Id);
        }
    }

    [Fact]
    public async Task Interactive_act_reuses_planning_conversation_and_executes_approved_changes()
    {
        var harness = new SessionHarness();
        harness.Mode.Mode = AgentMode.Plan;
        harness.TaskStore = new FileAgentTaskStore(harness.FileSystem);
        harness.Chat.Enqueue("1. Add Foo.");
        harness.Chat.Enqueue("File: src/Foo.cs\n```csharp\n+var value = 1;\n```");
        harness.Ui.EnqueueConfirmation(true);
        harness.Ui.EnqueueConfirmation(true);
        harness.QueueInputs("Plan Foo", ":act", ":exit");

        (await harness.Build().RunAsync()).Should().Be(0);

        harness.FileSystem.FileExists(@"C:\project\src\Foo.cs").Should().BeTrue();
        harness.History.Turns.Should().HaveCount(2);
        harness.Chat.Requests[1].AllowedTools.Should().Contain("write_file");
        harness.Scanner.ScannedRoots.Should().BeEmpty();
        (await harness.TaskStore.LoadAsync(harness.Root))!.Status.Should().Be(AgentTaskStatus.Completed);
    }
    [Fact]
    public async Task Single_task_runs_once_without_reading_interactive_input()
    {
        var harness = new SessionHarness();
        harness.Chat.Enqueue("done");
        harness.QueueInputs("must not be read");

        var exitCode = await harness.Build().RunTaskAsync("single request");

        exitCode.Should().Be(0);
        harness.Chat.Requests.Should().ContainSingle();
        harness.History.Turns.Should().ContainSingle().Which.UserRequest.Should().Be("single request");
        harness.Ui.ReadRequests.Should().BeEmpty();
        harness.Scanner.ScannedRoots.Should().BeEmpty();
        harness.Ui.ProgressLabels.Should().NotContain("Loading Context...");
        (await harness.SessionStore.LoadAsync(harness.Root)).Should().ContainSingle();
    }

    [Fact]
    public async Task Act_restores_saved_planning_conversation_after_restart()
    {
        var harness = new SessionHarness();
        harness.Mode.Mode = AgentMode.Plan;
        harness.TaskStore = new FileAgentTaskStore(harness.FileSystem);
        harness.Chat.Enqueue("1. Inspect Foo.");
        await harness.Build().RunTaskAsync("Plan Foo");
        harness.History.Clear();
        harness.Chat.Enqueue("Inspection complete.");
        harness.Ui.EnqueueConfirmation(true);

        (await harness.Build().ExecutePlanAsync()).Should().Be(0);

        harness.History.Turns.Should().HaveCount(2);
        harness.Chat.Requests[1].SystemPrompt.Should().Contain("1. Inspect Foo.");
        harness.Scanner.ScannedRoots.Should().BeEmpty();
    }

    [Theory]
    [InlineData(AgentMode.Agent, AgentTaskStatus.Completed, "proposal")]
    [InlineData(AgentMode.Plan, AgentTaskStatus.Failed, "proposal")]
    [InlineData(AgentMode.Plan, AgentTaskStatus.Cancelled, "proposal")]
    [InlineData(AgentMode.Plan, AgentTaskStatus.InProgress, "proposal")]
    [InlineData(AgentMode.Plan, AgentTaskStatus.Completed, " ")]
    public async Task Act_rejects_unusable_plans_before_consent_or_model_calls(AgentMode mode,
        AgentTaskStatus status, string proposal)
    {
        var harness = new SessionHarness();
        harness.Mode.Mode = AgentMode.Plan;
        harness.TaskStore = new FileAgentTaskStore(harness.FileSystem);
        await harness.TaskStore.SaveAsync(harness.Root, AgentTask.Create("Plan Foo") with
        {
            Mode = mode,
            Status = status,
            ProposedPlan = proposal,
        });

        (await harness.Build().ExecutePlanAsync()).Should().Be(1);

        harness.Mode.Mode.Should().Be(AgentMode.Plan);
        harness.Ui.Prompts.Should().BeEmpty();
        harness.Chat.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Act_rejects_missing_or_stale_conversation_without_switching_mode()
    {
        var harness = new SessionHarness();
        harness.Mode.Mode = AgentMode.Plan;
        harness.TaskStore = new FileAgentTaskStore(harness.FileSystem);
        await harness.TaskStore.SaveAsync(harness.Root, AgentTask.Create("Plan Foo").Complete() with
        {
            Mode = AgentMode.Plan,
            ProposedPlan = "1. Inspect Foo.",
        });
        var session = harness.Build();

        (await session.ExecutePlanAsync()).Should().Be(1);
        harness.History.Add(new ChatTurn(DateTimeOffset.UtcNow, "unrelated request", "unrelated response"));
        (await session.ExecutePlanAsync()).Should().Be(1);

        harness.Mode.Mode.Should().Be(AgentMode.Plan);
        harness.Ui.Prompts.Should().BeEmpty();
        harness.Chat.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Act_without_confirmation_fails_closed_and_keeps_proposal()
    {
        var harness = new SessionHarness();
        harness.Mode.Mode = AgentMode.Plan;
        harness.TaskStore = new FileAgentTaskStore(harness.FileSystem);
        harness.Chat.Enqueue("1. Inspect Foo.");
        var session = harness.Build();
        await session.RunTaskAsync("Plan Foo");
        var plan = await harness.TaskStore.LoadAsync(harness.Root);

        (await session.ExecutePlanAsync()).Should().Be(1);

        harness.Mode.Mode.Should().Be(AgentMode.Plan);
        (await harness.TaskStore.LoadAsync(harness.Root))!.Id.Should().Be(plan!.Id);
        harness.Chat.Requests.Should().ContainSingle();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Act_handles_cancellation_without_replaying_proposal(bool beforeConsent)
    {
        var harness = new SessionHarness();
        harness.Mode.Mode = AgentMode.Plan;
        harness.TaskStore = new FileAgentTaskStore(harness.FileSystem);
        harness.Chat.Enqueue("1. Inspect Foo.");
        var session = harness.Build();
        await session.RunTaskAsync("Plan Foo");
        var plan = await harness.TaskStore.LoadAsync(harness.Root);
        using var cancellation = new CancellationTokenSource();
        if (beforeConsent)
        {
            cancellation.Cancel();
        }
        else
        {
            harness.Ui.EnqueueConfirmation(true);
            harness.Chat.Failures.Enqueue(new OperationCanceledException());
        }

        (await session.ExecutePlanAsync(cancellationToken: cancellation.Token)).Should().Be(130);

        var latest = await harness.TaskStore.LoadAsync(harness.Root);
        if (beforeConsent)
        {
            harness.Mode.Mode.Should().Be(AgentMode.Plan);
            latest!.Id.Should().Be(plan!.Id);
            harness.Ui.Prompts.Should().BeEmpty();
        }
        else
        {
            latest!.Status.Should().Be(AgentTaskStatus.Cancelled);
            latest.SourcePlanId.Should().Be(plan!.Id);
            (await session.ExecutePlanAsync()).Should().Be(1);
        }
    }

    [Fact]
    public async Task Single_task_returns_failure_and_persists_failed_state()
    {
        var harness = new SessionHarness();
        var store = new FileAgentTaskStore(harness.FileSystem);
        harness.TaskStore = store;
        harness.Chat.Failures.Enqueue(new ChatProviderException("request failed"));

        var exitCode = await harness.Build().RunTaskAsync("single request");

        exitCode.Should().Be(1);
        (await store.LoadAsync(harness.Root))!.Status.Should().Be(AgentTaskStatus.Failed);
        harness.Ui.ReadRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task Single_task_returns_cancellation_and_persists_cancelled_state()
    {
        var harness = new SessionHarness();
        var store = new FileAgentTaskStore(harness.FileSystem);
        harness.TaskStore = store;
        harness.Chat.Failures.Enqueue(new OperationCanceledException());

        var exitCode = await harness.Build().RunTaskAsync("single request");

        exitCode.Should().Be(130);
        (await store.LoadAsync(harness.Root))!.Status.Should().Be(AgentTaskStatus.Cancelled);
    }

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
    public async Task Answers_multiple_requests_without_scanning_the_project()
    {
        var harness = new SessionHarness();
        harness.Chat.Enqueue("ok");
        harness.Chat.Enqueue("done");
        harness.QueueInputs("hi", "follow up");

        await harness.Build().RunAsync();

        harness.Scanner.ScannedRoots.Should().BeEmpty();
        harness.Ui.ProgressLabels.Should().NotContain("Loading Context...");
        harness.Chat.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task Offers_file_tools_without_an_automatic_project_summary()
    {
        var harness = new SessionHarness();
        harness.Chat.Enqueue("ok");
        harness.QueueInputs("hi");

        await harness.Build().RunAsync();

        var request = harness.Chat.Requests.Should().ContainSingle().Subject;
        request.SystemPrompt.Should().NotContain("**File: Program.cs**");
        request.AllowedTools.Should().Contain("read_file");
        request.UserMessage.Should().Be("## Here is user request\nhi");
    }

    [Theory]
    [InlineData(AgentMode.Agent, "Hi")]
    [InlineData(AgentMode.Plan, "Hi")]
    [InlineData(AgentMode.Agent, "Thanks!")]
    [InlineData(AgentMode.Plan, "Thanks!")]
    public async Task Casual_requests_reach_the_model_once_with_scoped_guidance_and_unchanged_permissions(AgentMode mode, string message)
    {
        var harness = new SessionHarness
        {
            InstructionsProvider = new FakeProjectInstructionsProvider("After code changes, update docs and run tests."),
        };
        harness.Mode.Mode = mode;
        harness.Chat.Enqueue("Hello!");
        harness.QueueInputs(message, ":exit");

        (await harness.Build().RunAsync()).Should().Be(0);

        var request = harness.Chat.Requests.Should().ContainSingle().Subject;
        request.UserMessage.Should().Be("## Here is user request\n" + message);
        request.SystemPrompt.Should().Contain("one short, natural sentence")
            .And.Contain("Do not call tools for greetings")
            .And.Contain("After code changes, update docs and run tests.");
        request.AllowedTools.Should().Contain("read_file");
        request.AllowedTools.Contains("write_file").Should().Be(mode == AgentMode.Agent);
        harness.Scanner.ScannedRoots.Should().BeEmpty();
        harness.Ui.AssistantOutput.ToString().Should().Be("Hello!");
        harness.History.Turns.Should().ContainSingle().Which.UserRequest.Should().Be(message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Opening_and_exiting_or_using_local_commands_does_not_load_project_guidance(bool localCommands)
    {
        var instructions = new FakeProjectInstructionsProvider("Project instructions.");
        var harness = new SessionHarness { InstructionsProvider = instructions };
        harness.QueueInputs(localCommands ? [":help", ":mode plan", ":exit"] : []);

        (await harness.Build().RunAsync()).Should().Be(0);

        harness.Ui.ReadRequests.Should().NotBeEmpty();
        instructions.LoadCount.Should().Be(0);
        harness.Scanner.ScannedRoots.Should().BeEmpty();
        harness.Ui.ProgressLabels.Should().NotContain("Loading Context...");
        harness.Chat.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Refreshes_project_instructions_for_each_model_request_without_scanning()
    {
        var instructions = new FakeProjectInstructionsProvider("Always write a test.");
        var harness = new SessionHarness { InstructionsProvider = instructions };
        harness.Chat.Enqueue("first");
        harness.Chat.Enqueue("second");
        harness.QueueInputs(":help", "first request", "second request");

        await harness.Build().RunAsync();

        instructions.LoadCount.Should().Be(3);
        harness.Chat.Requests.Should().HaveCount(2).And.OnlyContain(
            request => request.SystemPrompt.Contains("Always write a test.", StringComparison.Ordinal));
        harness.Scanner.ScannedRoots.Should().BeEmpty();
    }

    [Fact]
    public async Task Deep_planning_loads_guidance_without_scanning()
    {
        var harness = new SessionHarness { Instructions = "Always write a test." };
        harness.Chat.Enqueue("1. Inspect the relevant files.");
        harness.QueueInputs(":deep-plan investigate startup", ":exit");

        (await harness.Build().RunAsync()).Should().Be(0);

        harness.Chat.Requests.Should().ContainSingle().Which.SystemPrompt.Should().Contain("Always write a test.");
        harness.Scanner.ScannedRoots.Should().BeEmpty();
        harness.Mode.Mode.Should().Be(AgentMode.Plan);
    }

    [Fact]
    public async Task Each_request_refreshes_root_instructions_and_selects_only_referenced_nested_guidance()
    {
        var harness = new SessionHarness();
        harness.InstructionsProvider = new Codai.Core.Prompting.ProjectInstructionsProvider(harness.FileSystem);
        var instructions = Path.Combine(harness.Root, "AGENTS.md");
        harness.FileSystem.AddFile(instructions, "FIRST_ROOT_GUIDANCE");
        harness.FileSystem.AddFile(Path.Combine(harness.Root, "src", "instructions.md"), "SOURCE_ONLY_GUIDANCE");
        harness.FileSystem.AddFile(Path.Combine(harness.Root, "src", "Main.cs"), "source");
        harness.FileSystem.AddFile(Path.Combine(harness.Root, "other", "CLAUDE.md"), "UNRELATED_GUIDANCE");
        harness.Chat.BeforeResponse = (_, _) =>
        {
            if (harness.Chat.Requests.Count == 1)
            {
                harness.FileSystem.AddFile(instructions, "SECOND_ROOT_GUIDANCE");
            }
            else if (harness.Chat.Requests.Count == 2)
            {
                harness.FileSystem.DeleteFile(instructions);
            }
            return Task.CompletedTask;
        };
        harness.Chat.Enqueue("first answer");
        harness.Chat.Enqueue("second answer");
        harness.Chat.Enqueue("third answer");
        harness.QueueInputs("Inspect @src/Main.cs", "Another task", "Final task");

        (await harness.Build().RunAsync()).Should().Be(0);

        harness.Chat.Requests.Should().HaveCount(3);
        harness.Chat.Requests[0].SystemPrompt.Should().Contain("FIRST_ROOT_GUIDANCE").And.Contain("SOURCE_ONLY_GUIDANCE").And.NotContain("UNRELATED_GUIDANCE");
        harness.Chat.Requests[1].SystemPrompt.Should().Contain("SECOND_ROOT_GUIDANCE").And.NotContain("FIRST_ROOT_GUIDANCE").And.NotContain("SOURCE_ONLY_GUIDANCE");
        harness.Chat.Requests[2].SystemPrompt.Should().NotContain("FIRST_ROOT_GUIDANCE").And.NotContain("SECOND_ROOT_GUIDANCE").And.NotContain("SOURCE_ONLY_GUIDANCE");
        harness.Scanner.ScannedRoots.Should().BeEmpty();
    }

    [Fact]
    public async Task A_conditional_project_rule_applies_only_when_a_matching_file_is_referenced()
    {
        var harness = new SessionHarness();
        harness.FileSystem.AddFile(
            Path.Combine(harness.Root, ".codai", "rules", "always.md"),
            "Always-on rule body.");
        harness.FileSystem.AddFile(
            Path.Combine(harness.Root, ".codai", "rules", "api.md"),
            "---\napplyTo: src/**/*.cs\n---\nConditional file rule body.");
        harness.FileSystem.AddFile(
            Path.Combine(harness.Root, "src", "Foo.cs"),
            "class Foo { }");

        harness.Chat.Enqueue("ok");
        harness.Chat.Enqueue("ok");
        harness.QueueInputs("look at @src/Foo.cs", "hello");

        await harness.Build().RunAsync();

        harness.Chat.Requests.Should().HaveCount(2);
        var withMention = harness.Chat.Requests[0].SystemPrompt;
        var withoutMention = harness.Chat.Requests[1].SystemPrompt;

        withMention.Should().Contain("## Here are the project rules that apply to this request")
            .And.Contain("Always-on rule body.")
            .And.Contain("Conditional file rule body.");
        withoutMention.Should().Contain("Always-on rule body.")
            .And.NotContain("Conditional file rule body.");
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
    public async Task A_stopped_tool_loop_persists_failure_and_allows_the_next_request()
    {
        var harness = new SessionHarness();
        var store = new FileAgentTaskStore(harness.FileSystem);
        harness.TaskStore = store;
        harness.Chat.Failures.Enqueue(new ChatProviderException("Tool failure limit reached."));
        harness.Chat.Enqueue("recovered");
        harness.QueueInputs("failing request", "next request");

        await harness.Build().RunAsync();

        var tasks = await store.ListAsync(harness.Root);
        var failed = tasks.Single(task => task.Request == "failing request");
        failed.Status.Should().Be(AgentTaskStatus.Failed);
        failed.Checkpoints[^1].Status.Should().Be(AgentTaskStatus.Failed);
        failed.Checkpoints[^1].Error.Should().Be("Tool failure limit reached.");
        failed.Plan[0].Status.Should().Be(AgentTaskStatus.Failed);
        failed.Plan[1].Status.Should().Be(AgentTaskStatus.Pending);

        var completed = tasks.Single(task => task.Request == "next request");
        completed.Status.Should().Be(AgentTaskStatus.Completed);
        completed.Plan.Should().OnlyContain(step => step.Status == AgentTaskStatus.Completed);
        completed.Checkpoints.Should().OnlyContain(checkpoint => checkpoint.Status == AgentTaskStatus.Completed);
    }

    [Fact]
    public async Task Session_does_not_depend_on_project_scanner_availability()
    {
        var harness = new SessionHarness();
        harness.Scanner.Failure = new DirectoryNotFoundException("Project directory was not found.");
        harness.Chat.Enqueue("answering without context");
        harness.QueueInputs("hi");

        await harness.Build().RunAsync();

        harness.Ui.Errors.Should().BeEmpty();
        harness.Scanner.ScannedRoots.Should().BeEmpty();
        harness.Chat.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task Explicit_project_scans_remain_available_and_report_failures()
    {
        var harness = new SessionHarness();
        var loader = new ProjectContextLoader(harness.Options, harness.Scanner, harness.Ui);

        (await loader.LoadAsync()).Should().BeSameAs(harness.Scanner.Result);
        harness.Scanner.Failure = new DirectoryNotFoundException("Project directory was not found.");
        (await loader.LoadAsync()).Should().BeSameAs(Codai.Core.Projects.ProjectContext.Empty);

        harness.Scanner.ScannedRoots.Should().HaveCount(2);
        harness.Ui.Errors.Should().Contain("Project directory was not found.");
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

        harness.Ui.Infos.Should().NotContain(message => message.Contains("AGENTS.md", StringComparison.Ordinal));
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
    public async Task Task_show_reports_saved_task_details()
    {
        var harness = new SessionHarness();
        harness.TaskStore = new FileAgentTaskStore(harness.FileSystem);
        var task = AgentTask.Create("inspect the project").Start().Complete();
        await harness.TaskStore.SaveAsync(harness.Root, task);
        harness.QueueInputs($":task show {task.Id}");

        await harness.Build().RunAsync();

        harness.Ui.Infos.Should().Contain(message => message.Contains(task.Id, StringComparison.Ordinal));
        harness.Ui.Lines.Should().Contain(message => message.Contains("Mode:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Task_resume_replaces_history_and_restores_task_mode()
    {
        var harness = new SessionHarness();
        harness.TaskStore = new FileAgentTaskStore(harness.FileSystem);
        var task = AgentTask.Create("resume this").Start().Complete() with { Mode = AgentMode.Plan };
        await harness.TaskStore.SaveAsync(harness.Root, task);
        await harness.SessionStore.SaveAsync(harness.Root, task.Id,
            [new ChatTurn(DateTimeOffset.UtcNow, "old question", "old answer")]);
        harness.QueueInputs($":task resume {task.Id}", ":exit");

        await harness.Build().RunAsync();

        harness.History.Turns.Should().ContainSingle(turn => turn.UserRequest == "old question");
        harness.Mode.Mode.Should().Be(AgentMode.Plan);
        harness.Ui.Successes.Should().Contain(message => message.Contains("Resumed task", StringComparison.Ordinal));
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
