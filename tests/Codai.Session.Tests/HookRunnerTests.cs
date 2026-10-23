using System.Text.Json;
using Codai.Cli;
using Codai.Configuration;
using Codai.Core.Abstractions;
using Codai.Core.Agents;
using Codai.Core.Chat;
using Codai.Core.IO;
using Codai.Core.Tools;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Codai.Session.Tests;

public sealed class HookRunnerTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("codai-hooks-");
    private readonly RecordingProcesses _processes = new();

    [Fact]
    public async Task Approved_hook_receives_json_stdin_without_command_interpolation()
    {
        await ManifestAsync();
        var context = new AgentHookContext(AgentHookEvent.PreToolUse, _directory.FullName)
        {
            ToolName = "write_file",
            Prompt = "quoted \"prompt\" & not-a-command",
        };

        var decision = await Runner().InvokeAsync(context);

        decision.Allowed.Should().BeTrue();
        _processes.Calls.Should().ContainSingle();
        var call = _processes.Calls.Single();
        call.Command.Should().Be("policy-command");
        using var payload = JsonDocument.Parse(call.Input);
        payload.RootElement.GetProperty("event").GetString().Should().Be("PreToolUse");
        payload.RootElement.GetProperty("prompt").GetString().Should().Be(context.Prompt);
    }

    [Fact]
    public async Task Plugin_hook_can_block_event_before_external_hooks_run()
    {
        await ManifestAsync();

        var result = await Runner(pluginHooks: [new BlockingPluginHook()]).InvokeAsync(Context());

        result.Allowed.Should().BeFalse();
        result.Reason.Should().Be("blocked by plugin policy");
        _processes.Calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData(AgentMode.Plan, true)]
    [InlineData(AgentMode.Agent, false)]
    public async Task Read_only_modes_or_disabled_shell_cannot_execute_hooks(AgentMode mode, bool shell)
    {
        await ManifestAsync();

        var result = await Runner(mode: mode, allowShell: shell).InvokeAsync(Context());

        result.Allowed.Should().BeFalse();
        _processes.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Unapproved_hooks_fail_closed_without_starting_process()
    {
        await ManifestAsync();

        var result = await Runner(approver: DenyAllToolApprover.Instance).InvokeAsync(Context());

        result.Allowed.Should().BeFalse();
        result.Reason.Should().Contain("not approved");
        _processes.Calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"allow\":false,\"reason\":\"blocked by policy\"}")]
    public async Task Malformed_or_blocking_responses_fail_closed(string response)
    {
        await ManifestAsync();
        _processes.Result = new(0, response, string.Empty, false);

        (await Runner().InvokeAsync(Context())).Allowed.Should().BeFalse();
        _processes.Calls.Should().ContainSingle();
    }

    [Fact]
    public async Task Failed_hooks_are_not_replayed()
    {
        await ManifestAsync();
        _processes.Result = new(2, string.Empty, "error", false);

        (await Runner().InvokeAsync(Context())).Allowed.Should().BeFalse();
        _processes.Calls.Should().ContainSingle();
    }

    [Fact]
    public async Task Disabled_or_non_matching_hooks_do_not_execute()
    {
        await File.WriteAllTextAsync(Path.Combine(_directory.FullName, HookRunner.ManifestFileName),
            "{\"hooks\":[{\"enabled\":false},{\"name\":\"end\",\"event\":\"TaskCompleted\",\"command\":\"end-command\"}]}");

        (await Runner().InvokeAsync(Context())).Allowed.Should().BeTrue();
        _processes.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Cancellation_propagates_instead_of_becoming_hook_permission()
    {
        await ManifestAsync();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        Func<Task> invoke = () => Runner().InvokeAsync(Context(), cancellation.Token);

        await invoke.Should().ThrowAsync<OperationCanceledException>();
        _processes.Calls.Should().BeEmpty();
    }

    public void Dispose() => _directory.Delete(recursive: true);

    [Fact]
    public async Task Pre_tool_block_stops_before_tool_invocation()
    {
        var tool = new CountingTool();
        var hooks = new BlockingHooks(AgentHookEvent.PreToolUse);
        var runner = ToolRunnerFor(tool, hooks);
        using var arguments = JsonDocument.Parse("{}");

        var result = await runner.ExecuteAsync(tool.Name, arguments.RootElement);

        result.IsSuccess.Should().BeFalse();
        result.StopsExecution.Should().BeTrue();
        tool.Calls.Should().Be(0);
        hooks.Events.Should().Equal(AgentHookEvent.PreToolUse);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Post_tool_block_stops_without_replaying_completed_tool(bool completes)
    {
        var tool = new CountingTool { Completes = completes };
        var hooks = new BlockingHooks(AgentHookEvent.PostToolUse);
        using var arguments = JsonDocument.Parse("{}");

        var result = await ToolRunnerFor(tool, hooks).ExecuteAsync(tool.Name, arguments.RootElement);

        result.StopsExecution.Should().BeTrue();
        result.IsSuccess.Should().BeFalse();
        result.Content.Should().Contain("tool already ran");
        tool.Calls.Should().Be(1);
        hooks.Events.Should().Equal(AgentHookEvent.PreToolUse, AgentHookEvent.PostToolUse);
    }

    private ToolRunner ToolRunnerFor(ITool tool, IAgentHooks hooks) => new(
        new ToolRegistry([tool]), AlwaysApproveToolApprover.Instance, NullToolObserver.Instance,
        PhysicalFileSystem.Instance, _directory.FullName, hooks: hooks);

    [Fact]
    public async Task Task_start_rejection_fails_request_before_model_call()
    {
        var hooks = new BlockingHooks(AgentHookEvent.TaskStarted);
        var harness = new SessionHarness { Hooks = hooks };
        var store = new FileAgentTaskStore(harness.FileSystem);
        harness.TaskStore = store;

        (await harness.Build().RunTaskAsync("task")).Should().Be(1);

        harness.Chat.Requests.Should().BeEmpty();
        hooks.Events.Should().Equal(AgentHookEvent.TaskStarted, AgentHookEvent.TaskFailed);
        (await store.LoadAsync(harness.Root))!.Status.Should().Be(AgentTaskStatus.Failed);
    }

    [Fact]
    public async Task Successful_task_emits_prompt_and_completion_hooks_in_order()
    {
        var hooks = new BlockingHooks(AgentHookEvent.TaskFailed);
        var harness = new SessionHarness { Hooks = hooks };
        harness.Chat.Enqueue("Answer");

        (await harness.Build().RunTaskAsync("task")).Should().Be(0);

        hooks.Events.Should().Equal(AgentHookEvent.TaskStarted, AgentHookEvent.UserPromptSubmitted,
            AgentHookEvent.TaskCompleted);
    }

    [Theory]
    [InlineData(AgentHookEvent.PreCompact, "original")]
    [InlineData(AgentHookEvent.PostCompact, "replacement")]
    public async Task Compaction_policy_preserves_correct_pre_or_post_mutation_state(AgentHookEvent stage, string expected)
    {
        var harness = new SessionHarness();
        harness.History.Add(new ChatTurn(DateTimeOffset.UtcNow, "original", "answer"));
        var hooks = new BlockingHooks(stage);
        var compactor = new HistoryCompactor(harness.Chat, harness.History, harness.Tokens, harness.Access,
            harness.Ui, new TranscriptStore(harness.FileSystem), harness.Options, hooks);
        var plan = new CompactionPlan(1, [new ChatTurn(DateTimeOffset.UtcNow, "replacement", "summary")],
            CompactionMethod.Digest, 100, 20, null);
        Func<Task> apply = () => compactor.ApplyAsync(plan);

        await apply.Should().ThrowAsync<AgentHookRejectedException>();

        harness.History.Turns.Single().UserRequest.Should().Be(expected);
    }

    private sealed class BlockingHooks(AgentHookEvent blockedEvent) : IAgentHooks
    {
        public List<AgentHookEvent> Events { get; } = [];
        public ToolResult? LastResult { get; private set; }

        public Task<AgentHookDecision> InvokeAsync(AgentHookContext context, CancellationToken cancellationToken = default)
        {
            Events.Add(context.Event);
            LastResult = context.Result;
            return Task.FromResult(context.Event == blockedEvent
                ? AgentHookDecision.Block("policy denied") : AgentHookDecision.Continue);
        }
    }

    private sealed class CountingTool : ToolBase
    {
        public int Calls { get; private set; }
        public bool Throws { get; init; }
        public bool Fails { get; init; }
        public bool Completes { get; init; }
        public override string Name => "counting_tool";
        public override string Description => "Count invocations";
        public override bool IsReadOnly => true;
        public override string ParametersSchema => "{\"type\":\"object\",\"properties\":{}}";

        public override Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Throws)
            {
                throw new InvalidOperationException("tool error");
            }

            return Task.FromResult(Fails ? ToolResult.Failure("failed") : ToolResult.Success("done") with { StopsExecution = Completes });
        }
    }

    [Theory]
    [InlineData("false", false, 1)]
    [InlineData("true", false, 0)]
    [InlineData("false", true, 0)]
    public async Task Cli_injected_hooks_require_consent_and_can_be_disabled(string approval, bool disabled, int expected)
    {
        await File.WriteAllTextAsync(Path.Combine(_directory.FullName, HookRunner.ManifestFileName),
            "{\"hooks\":[{\"name\":\"start\",\"event\":\"TaskStarted\",\"command\":\"exit 0\"}]}");
        var app = new CliApplication(new QuietOutput(), () => _directory.FullName,
            input: new TerminalInput(), sessionFactory: provider => new HookSession(provider.GetRequiredService<IAgentHooks>(),
                _directory.FullName));
        List<string> args = ["task", "--hooks-dir", _directory.FullName, "--auto-approve", approval, "--no-sub-agents"];
        if (disabled)
        {
            args.Add("--no-hooks");
        }

        (await app.RunAsync([.. args])).Should().Be(expected);
    }

    private sealed class HookSession(IAgentHooks hooks, string root) : ICodaiSession
    {
        public Task<int> RunAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException();

        public async Task<int> RunTaskAsync(string request, CancellationToken cancellationToken = default) =>
            (await hooks.InvokeAsync(new AgentHookContext(AgentHookEvent.TaskStarted, root), cancellationToken)).Allowed ? 0 : 1;
    }

    private sealed class TerminalInput : IConsoleInput
    {
        public string? ReadLine() => throw new InvalidOperationException("No hook may consume terminal input.");
    }

    private sealed class QuietOutput : IConsoleOutput
    {
        public List<string> Lines { get; } = [];
        public void WriteLine(string message) => Lines.Add(message);
        public void WriteError(string message) { }
    }

    private AgentHookContext Context() => new(AgentHookEvent.PreToolUse, _directory.FullName);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Post_tool_hook_observes_failed_or_thrown_tool_without_replay(bool throws)
    {
        var tool = new CountingTool { Throws = throws, Fails = !throws };
        var hooks = new BlockingHooks(AgentHookEvent.TaskFailed);
        using var args = JsonDocument.Parse("{}");

        var result = await ToolRunnerFor(tool, hooks).ExecuteAsync(tool.Name, args.RootElement);

        result.IsSuccess.Should().BeFalse();
        tool.Calls.Should().Be(1);
        hooks.LastResult.Should().NotBeNull();
        hooks.LastResult!.Value.IsSuccess.Should().BeFalse();
        hooks.Events.Should().Equal(AgentHookEvent.PreToolUse, AgentHookEvent.PostToolUse);
    }

    [Fact]
    public async Task All_manifests_are_validated_before_any_hook_executes()
    {
        await ManifestAsync();
        var invalid = _directory.CreateSubdirectory("invalid");
        await File.WriteAllTextAsync(Path.Combine(invalid.FullName, HookRunner.ManifestFileName), "{\"hooks\":[null]}");
        var runner = new HookRunner(PhysicalFileSystem.Instance, _processes, AlwaysApproveToolApprover.Instance,
            new AgentModeState(), [_directory.FullName, invalid.FullName], true);

        (await runner.InvokeAsync(Context())).Allowed.Should().BeFalse();
        _processes.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Throwing_task_hook_fails_closed_without_model_request()
    {
        var harness = new SessionHarness { Hooks = new ThrowingHooks() };

        (await harness.Build().RunTaskAsync("task")).Should().Be(1);
        harness.Chat.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Json_run_emits_hook_events_separately_from_content()
    {
        await File.WriteAllTextAsync(Path.Combine(_directory.FullName, HookRunner.ManifestFileName),
            "{\"hooks\":[{\"name\":\"start\",\"event\":\"TaskStarted\",\"command\":\"exit 0\"}]}");
        var output = new QuietOutput();
        var app = new CliApplication(output, () => _directory.FullName, input: new TerminalInput(),
            sessionFactory: provider => new HookSession(provider.GetRequiredService<IAgentHooks>(), _directory.FullName));

        (await app.RunAsync(["task", "--json", "--hooks-dir", _directory.FullName, "--auto-approve", "true"])).Should().Be(0);
        var hooks = output.Lines.Select(line => JsonSerializer.Deserialize<JsonElement>(line))
            .Where(entry => entry.GetProperty("type").GetString() == "hook_event").ToArray();
        hooks.Select(entry => entry.GetProperty("event").GetProperty("type").GetString())
            .Should().Equal("HookStarted", "HookCompleted");
        hooks[0].GetProperty("event").GetProperty("description").GetString().Should().Be("TaskStarted");
    }

    private sealed class ThrowingHooks : IAgentHooks
    {
        public Task<AgentHookDecision> InvokeAsync(AgentHookContext context, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("broken hook implementation");
    }

    [Fact]
    public async Task Duplicate_directories_do_not_replay_hooks()
    {
        await ManifestAsync();
        var runner = new HookRunner(PhysicalFileSystem.Instance, _processes, AlwaysApproveToolApprover.Instance,
            new AgentModeState(), [_directory.FullName, _directory.FullName], true);

        (await runner.InvokeAsync(Context())).Allowed.Should().BeTrue();
        _processes.Calls.Should().ContainSingle();
    }

    [Theory]
    [InlineData(false, false, 1)]
    [InlineData(true, false, 0)]
    [InlineData(false, true, 0)]
    public async Task Explicit_directory_overrides_environment_and_disabling_skips_discovery(bool disabled, bool overrideDirectory, int expected)
    {
        var environment = new HookEnvironment(Path.Combine(_directory.FullName, "missing"));
        var app = new CliApplication(new QuietOutput(), () => _directory.FullName, input: new TerminalInput(),
            sessionFactory: provider => new HookSession(provider.GetRequiredService<IAgentHooks>(), _directory.FullName),
            environment: environment);
        List<string> args = ["task", "--config-dir", _directory.FullName];
        if (disabled)
        {
            args.Add("--no-hooks");
        }

        if (overrideDirectory)
        {
            args.AddRange(["--hooks-dir", _directory.FullName]);
        }

        (await app.RunAsync([.. args])).Should().Be(expected);
    }

    private sealed class HookEnvironment(string directory) : IEnvironmentReader
    {
        public string? GetVariable(string name) => name == "CODAI_HOOKS_DIR" ? directory : null;
        public IReadOnlyDictionary<string, string?> All() => new Dictionary<string, string?> { ["CODAI_HOOKS_DIR"] = directory };
    }

    private HookRunner Runner(IToolApprover? approver = null, AgentMode mode = AgentMode.Agent,
        bool allowShell = true, IEnumerable<IPluginAgentHook>? pluginHooks = null) =>
        new(PhysicalFileSystem.Instance, _processes, approver ?? AlwaysApproveToolApprover.Instance,
            new AgentModeState(mode), [_directory.FullName], allowShell, pluginHooks: pluginHooks);

    private Task ManifestAsync() => File.WriteAllTextAsync(Path.Combine(_directory.FullName, HookRunner.ManifestFileName),
        "{\"hooks\":[{\"name\":\"policy\",\"event\":\"PreToolUse\",\"command\":\"policy-command\"}]}");

    private sealed class RecordingProcesses : IProcessRunner
    {
        public ProcessResult Result { get; set; } = new(0, "{\"allow\":true}", string.Empty, false);
        public List<(string Command, string Input)> Calls { get; } = [];

        public Task<ProcessResult> RunAsync(string commandLine, string workingDirectory, TimeSpan timeout,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("Hook stdin must be explicit.");

        public Task<ProcessResult> RunWithInputAsync(string commandLine, string workingDirectory, string standardInput,
            TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add((commandLine, standardInput));
            return Task.FromResult(Result);
        }
    }

    private sealed class BlockingPluginHook : IPluginAgentHook
    {
        public Task<AgentHookDecision> InvokeAsync(AgentHookContext context,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(AgentHookDecision.Block("blocked by plugin policy"));
    }
}
