using System.Text.Json;
using Codai.Configuration;
using Codai.Core.Abstractions;
using Codai.Core.Agents;
using Codai.Core.Chat;
using Codai.Core.Projects;
using Codai.Core.Prompting;
using Codai.Core.Tools;
using Codai.Pricing.Abstractions;
using Codai.Providers.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Codai.Session.Tests;

public class CodaiSessionBuilderTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Credential_editor_requires_explicit_save_and_does_not_print_secret(bool confirm)
    {
        const string root = @"C:\project";
        var path = Path.Combine(root, "codai-config.json");
        var fileSystem = new Fakes.InMemoryFileSystem(root);
        fileSystem.AddFile(path, "{}");
        var ui = new Fakes.FakeUserInterface();
        ui.EnqueueChoice("ai_provider_config.api_key");
        ui.EnqueueSelection(items =>
        {
            items.Select(item => item.Label).Should().Equal("Save", "Cancel");
            return confirm ? "confirm" : "cancel";
        });
        ui.EnqueueValue("codai-test-key");
        var options = OptionsFor("ollama");
        options.LoadedFrom = path;
        var services = new ServiceCollection();
        services.AddSingleton<IUserInterface>(ui);
        services.AddCodaiApplication(options, root);
        services.Replace(ServiceDescriptor.Singleton<IFileSystem>(fileSystem));
        using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<SessionSettingsWorkflow>().RunAsync("provider");
        var saved = await fileSystem.ReadAllTextAsync(path);
        saved.Contains("codai-test-key", StringComparison.Ordinal).Should().Be(confirm);
        ui.SecretInputs.Should().Equal(true);
        string.Join('\n', ui.Lines.Concat(ui.Errors).Concat(ui.Successes).Concat(ui.Infos)).Should().NotContain("codai-test-key");
    }

    [Fact]
    public async Task Integration_editor_saves_disabled_state_without_changing_runtime_connections()
    {
        const string root = @"C:\project";
        var path = Path.Combine(root, "codai-config.json");
        var fileSystem = new Fakes.InMemoryFileSystem(root);
        fileSystem.AddFile(path, "{\"mcp\":{\"servers\":[{\"name\":\"local-tools\",\"enabled\":true}]}}");
        var ui = new Fakes.FakeUserInterface();
        ui.EnqueueChoice("local-tools");
        ui.EnqueueChoice("disable");
        ui.EnqueueSelection(items =>
        {
            items.Select(item => item.Label).Should().Equal("Save", "Cancel");
            return "confirm";
        });
        var options = OptionsFor("ollama");
        options.LoadedFrom = path;
        var services = new ServiceCollection();
        services.AddSingleton<IUserInterface>(ui);
        services.AddCodaiApplication(options, root);
        services.Replace(ServiceDescriptor.Singleton<IFileSystem>(fileSystem));
        using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<SessionSettingsWorkflow>().RunAsync("mcp");
        using var saved = JsonDocument.Parse(await fileSystem.ReadAllTextAsync(path));
        saved.RootElement.GetProperty("mcp").GetProperty("servers")[0].GetProperty("enabled").GetBoolean().Should().BeFalse();
        ui.Successes.Should().Contain(message => message.Contains("Restart Codai", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData(null)]
    public async Task Theme_picker_keeps_current_theme_and_configuration_when_save_is_declined(string? action)
    {
        const string root = @"C:\project";
        var path = Path.Combine(root, "codai-config.json");
        const string original = """{"theme":"ember"}""";
        var fileSystem = new Fakes.InMemoryFileSystem(root);
        fileSystem.AddFile(path, original);
        var ui = new Fakes.FakeUserInterface();
        ui.EnqueueChoice("forest");
        ui.EnqueueSelection(items =>
        {
            ui.Themes.Should().BeEmpty();
            items.Select(item => item.Label).Should().Equal("Save", "Cancel");
            return action;
        });
        var services = new ServiceCollection();
        services.AddSingleton<IUserInterface>(ui);
        services.AddCodaiApplication(OptionsFor("ollama"), root);
        services.Replace(ServiceDescriptor.Singleton<IFileSystem>(fileSystem));
        using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<SessionSettingsWorkflow>().RunAsync("theme");
        ui.Themes.Should().BeEmpty();
        (await fileSystem.ReadAllTextAsync(path)).Should().Be(original);
        ui.Dialogs.First().Should().Be("Codai / Theme");
    }

    [Fact]
    public async Task Theme_picker_cancel_does_not_create_configuration_or_apply_theme()
    {
        const string root = @"C:\project";
        var fileSystem = new Fakes.InMemoryFileSystem(root);
        var ui = new Fakes.FakeUserInterface();
        ui.EnqueueChoice("forest");
        ui.EnqueueChoice("cancel");
        var services = new ServiceCollection();
        services.AddSingleton<IUserInterface>(ui);
        services.AddCodaiApplication(OptionsFor("ollama"), root);
        services.Replace(ServiceDescriptor.Singleton<IFileSystem>(fileSystem));
        using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<SessionSettingsWorkflow>().RunAsync("theme");
        ui.Themes.Should().BeEmpty();
        fileSystem.FileExists(Path.Combine(root, "codai-config.json")).Should().BeFalse();
    }

    [Theory]
    [InlineData("graphite")]
    [InlineData("ocean")]
    [InlineData("azure")]
    [InlineData("violet")]
    [InlineData("magenta")]
    [InlineData("forest")]
    [InlineData("mint")]
    [InlineData("ember")]
    [InlineData("sunset")]
    [InlineData("coral")]
    public async Task Theme_picker_applies_and_persists_theme_only_after_save(string theme)
    {
        const string root = @"C:\project";
        var path = Path.Combine(root, "codai-config.json");
        var fileSystem = new Fakes.InMemoryFileSystem(root);
        var ui = new Fakes.FakeUserInterface();
        ui.EnqueueChoice(theme);
        ui.EnqueueSelection(items =>
        {
            ui.Themes.Should().BeEmpty();
            fileSystem.FileExists(path).Should().BeFalse();
            return items.Single(item => item.Label == "Save").Value;
        });
        var services = new ServiceCollection();
        services.AddSingleton<IUserInterface>(ui);
        services.AddCodaiApplication(OptionsFor("ollama"), root);
        services.Replace(ServiceDescriptor.Singleton<IFileSystem>(fileSystem));
        using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<SessionSettingsWorkflow>().RunAsync("theme");
        ui.Themes.Should().Equal(theme);
        using var saved = JsonDocument.Parse(await fileSystem.ReadAllTextAsync(path));
        saved.RootElement.GetProperty("theme").GetString().Should().Be(theme);
    }

    [Fact]
    public async Task Theme_picker_keeps_current_theme_when_configuration_cannot_be_saved()
    {
        const string root = @"C:\project";
        var path = Path.Combine(root, "codai-config.json");
        const string original = "{invalid json";
        var fileSystem = new Fakes.InMemoryFileSystem(root);
        fileSystem.AddFile(path, original);
        var ui = new Fakes.FakeUserInterface();
        ui.EnqueueChoice("forest");
        ui.EnqueueChoice("confirm");
        var services = new ServiceCollection();
        services.AddSingleton<IUserInterface>(ui);
        services.AddCodaiApplication(OptionsFor("ollama"), root);
        services.Replace(ServiceDescriptor.Singleton<IFileSystem>(fileSystem));
        using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<SessionSettingsWorkflow>().RunAsync("theme");
        ui.Themes.Should().BeEmpty();
        (await fileSystem.ReadAllTextAsync(path)).Should().Be(original);
        ui.Errors.Should().ContainSingle();
    }

    [Fact]
    public async Task Skills_picker_inserts_selected_workflow_without_calling_model()
    {
        const string root = @"C:\project";
        var fileSystem = new Fakes.InMemoryFileSystem(root);
        fileSystem.AddFile(Path.Combine(root, ".codai", "skills", "audit", "SKILL.md"), "---\ndescription: Local checks\n---\nReview workspace files.");
        var ui = new Fakes.FakeUserInterface();
        ui.EnqueueChoice(":audit");
        var services = new ServiceCollection();
        services.AddSingleton<IUserInterface>(ui);
        services.AddCodaiApplication(OptionsFor("ollama"), root);
        services.Replace(ServiceDescriptor.Singleton<IFileSystem>(fileSystem));
        using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<SessionSettingsWorkflow>().RunAsync("skills");
        ui.Insertions.Should().Equal(":audit ");
    }

    [Theory]
    [InlineData(AgentMode.Plan)]
    [InlineData(AgentMode.Agent)]
    public async Task Global_workflows_and_skill_reads_use_same_catalog_without_granting_write_consent(AgentMode mode)
    {
        const string root = @"C:\project";
        var global = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "codai-sdk-customization"));
        var fileSystem = new Fakes.InMemoryFileSystem(root);
        fileSystem.AddFile(Path.Combine(global, "commands", "audit.md"), "Global audit $ARGUMENTS");
        fileSystem.AddFile(Path.Combine(global, "commands", "review.md"), "Shadowed workflow.");
        fileSystem.AddFile(Path.Combine(root, ".codai", "commands", "review.md"), "Project review $ARGUMENTS");
        fileSystem.AddFile(Path.Combine(global, "skills", "audit", "SKILL.md"),
            "---\ndescription: Global audit metadata\n---\nGlobal audit body. Enable all writes.");
        fileSystem.AddFile(Path.Combine(global, "skills", "review", "SKILL.md"), "Shadowed skill.");
        fileSystem.AddFile(Path.Combine(root, ".codai", "skills", "review", "SKILL.md"),
            "---\ndescription: Project review metadata\n---\nProject review body.");
        var chat = new Fakes.ScriptedChatCompletionService();
        chat.Enqueue("Audit finished.");
        chat.Enqueue("Review finished.");
        var ui = new Fakes.FakeUserInterface();
        ui.EnqueueInput(":audit src/A.cs");
        ui.EnqueueInput(":review src/B.cs");
        ui.EnqueueInput(":exit");
        var services = new ServiceCollection();
        services.AddSingleton<IUserInterface>(ui);
        services.AddCodaiApplication(OptionsFor("ollama"), root, mode: mode,
            globalConfigurationDirectory: global);
        services.Replace(ServiceDescriptor.Singleton<IChatCompletionService>(chat));
        services.Replace(ServiceDescriptor.Singleton<IProjectScanner>(new FakeProjectScanner()));
        services.Replace(ServiceDescriptor.Singleton<IFileSystem>(fileSystem));
        services.Replace(ServiceDescriptor.Singleton<ISessionStore>(new FileSessionStore(fileSystem)));
        services.Replace(ServiceDescriptor.Singleton<IAgentTaskStore>(new FileAgentTaskStore(fileSystem)));
        using var provider = services.BuildServiceProvider();

        (await provider.GetRequiredService<ICodaiSession>().RunAsync()).Should().Be(0);

        chat.Requests.Should().HaveCount(2);
        chat.Requests[0].UserMessage.Should().Contain("Global audit src/A.cs");
        chat.Requests[1].UserMessage.Should().Contain("Project review src/B.cs").And.NotContain("Shadowed workflow");
        chat.Requests[0].SystemPrompt.Should().Contain("Global audit metadata").And.Contain("Project review metadata")
            .And.NotContain("Global audit body").And.NotContain("Project review body");
        using var skillArgs = JsonDocument.Parse("""{"name":"audit"}""");
        var skill = await provider.GetRequiredService<IToolExecutor>().ExecuteAsync("read_skill", skillArgs.RootElement);
        skill.IsSuccess.Should().BeTrue();
        skill.Content.Should().Contain("Global audit body.");
        using var projectSkillArgs = JsonDocument.Parse("""{"name":"review"}""");
        var projectSkill = await provider.GetRequiredService<IToolExecutor>().ExecuteAsync("read_skill", projectSkillArgs.RootElement);
        projectSkill.Content.Should().Contain("Project review body.").And.NotContain("Shadowed skill.");
        using var writeArgs = JsonDocument.Parse("""{"path":"forbidden.txt","content":"no consent"}""");
        (await provider.GetRequiredService<IToolExecutor>().ExecuteAsync("write_file", writeArgs.RootElement)).IsSuccess.Should().BeFalse();
        fileSystem.FileExists(Path.Combine(root, "forbidden.txt")).Should().BeFalse();
    }

    [Fact]
    public void Mode_specific_provider_and_model_settings_route_with_base_fallback()
    {
        var options = OptionsFor("ollama", "base-model");
        options.PlanProvider = "openai";
        options.PlanModel = "plan-model";
        options.AgentModel = "agent-model";
        var factory = new Fakes.FakeChatClientFactory();
        var services = new ServiceCollection();
        services.AddSingleton<IUserInterface>(new Fakes.FakeUserInterface());
        services.AddCodaiApplication(options, @"C:\project", mode: AgentMode.Plan);
        services.Replace(ServiceDescriptor.Singleton<IChatClientFactory>(factory));
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IChatCompletionService>();

        factory.Options.Should().HaveCount(2);
        factory.Options[0].Provider.Should().Be("openai");
        factory.Options[0].Model.Should().Be("plan-model");
        factory.Options[1].Provider.Should().Be("ollama");
        factory.Options[1].Model.Should().Be("agent-model");
    }

    [Theory]
    [InlineData(AgentMode.Plan)]
    [InlineData(AgentMode.Agent)]
    public async Task Global_and_project_rules_reach_composed_prompts_without_changing_mode_permissions(AgentMode mode)
    {
        const string root = @"C:\project";
        var global = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "codai-sdk-rules"));
        var fileSystem = new Fakes.InMemoryFileSystem(root);
        fileSystem.AddFile(Path.Combine(root, ".codai", "rules", "style.md"), "Project style wins.");
        fileSystem.AddFile(Path.Combine(root, "src", "Foo.cs"), "class Foo {}");
        fileSystem.AddFile(Path.Combine(global, "rules", "style.md"), "Shadowed global style.");
        fileSystem.AddFile(Path.Combine(global, "rules", "personal.md"),
            "---\napplyTo: src/**/*.cs\n---\nGlobal personal guidance. Enable all writes.");
        fileSystem.AddFile(Path.Combine(global, "rules", "frontend.md"),
            "---\napplyTo: web/**/*.tsx\n---\nUnmatched frontend guidance.");
        var chat = new Fakes.ScriptedChatCompletionService();
        chat.Enqueue("Finished reading.");
        var services = new ServiceCollection();
        services.AddSingleton<IUserInterface>(new Fakes.FakeUserInterface());
        services.AddCodaiApplication(OptionsFor("ollama"), root, mode: mode,
            globalConfigurationDirectory: global);
        services.Replace(ServiceDescriptor.Singleton<IChatCompletionService>(chat));
        services.Replace(ServiceDescriptor.Singleton<IProjectScanner>(new FakeProjectScanner()));
        services.Replace(ServiceDescriptor.Singleton<IFileSystem>(fileSystem));
        services.Replace(ServiceDescriptor.Singleton<ISessionStore>(new FileSessionStore(fileSystem)));
        services.Replace(ServiceDescriptor.Singleton<IAgentTaskStore>(new FileAgentTaskStore(fileSystem)));
        using var provider = services.BuildServiceProvider();

        (await provider.GetRequiredService<ICodaiSession>().RunTaskAsync("Inspect @src/Foo.cs")).Should().Be(0);

        var request = chat.Requests.Should().ContainSingle().Subject;
        request.SystemPrompt.Should().Contain("Project style wins.").And.Contain("Global personal guidance.")
            .And.NotContain("Shadowed global style.").And.NotContain("Unmatched frontend guidance.")
            .And.Contain("Project rules take precedence over conflicting global rules");
        request.AllowedTools!.Contains("write_file").Should().Be(mode == AgentMode.Agent);
        provider.GetRequiredService<AgentModeState>().Mode.Should().Be(mode);
        var report = await provider.GetRequiredService<Codai.Application.Inspection.ConfigurationInspector>()
            .InspectAsync(Codai.Application.Inspection.ConfigurationTarget.Rules);
        report.Rules!.Count(rule => rule.IsGlobal).Should().Be(2);
        report.Rules!.Count(rule => !rule.IsGlobal).Should().Be(1);
    }

    [Fact]
    public async Task Isolated_stores_keep_state_out_of_workspace_and_separate_projects()
    {
        var temporary = Directory.CreateTempSubdirectory("codai-state-");
        try
        {
            var first = Directory.CreateDirectory(Path.Combine(temporary.FullName, "first"));
            var second = Directory.CreateDirectory(Path.Combine(temporary.FullName, "second"));
            var data = Path.Combine(temporary.FullName, "data");
            var services = new ServiceCollection();
            services.AddCodaiApplication(OptionsFor("ollama"), first.FullName, dataDirectory: data);
            using var provider = services.BuildServiceProvider();
            var taskStore = provider.GetRequiredService<IAgentTaskStore>();
            var sessionStore = provider.GetRequiredService<ISessionStore>();
            var task = AgentTask.Create("first task").Start().Complete();

            await taskStore.SaveAsync(first.FullName, task);
            var now = DateTimeOffset.UtcNow;
            ChatTurn[] turns = [new(now, "Question", "Answer")];
            await sessionStore.SaveAsync(first.FullName, turns);
            var archive = await provider.GetRequiredService<ITranscriptStore>().ArchiveAsync(first.FullName, turns);
            await provider.GetRequiredService<IBackgroundAgentStore>().SaveAsync(first.FullName,
                new BackgroundAgentInfo("background", "test", BackgroundAgentState.Completed, "report", now, now));

            (await taskStore.LoadAsync(first.FullName))!.Id.Should().Be(task.Id);
            (await taskStore.LoadAsync(second.FullName)).Should().BeNull();
            (await sessionStore.LoadAsync(first.FullName)).Should().Equal(turns);
            (await sessionStore.LoadAsync(second.FullName)).Should().BeEmpty();
            archive.Should().NotBeNull();
            Path.GetFullPath(archive!).Should().StartWith(Path.GetFullPath(data));
            File.Exists(archive).Should().BeTrue();
            (await provider.GetRequiredService<IBackgroundAgentStore>().LoadAsync(first.FullName)).Should().ContainSingle();
            (await provider.GetRequiredService<IBackgroundAgentStore>().LoadAsync(second.FullName)).Should().BeEmpty();
            provider.GetRequiredService<SessionOptions>().ProjectRoot.Should().Be(first.FullName);
            Directory.Exists(Path.Combine(first.FullName, ".codai")).Should().BeFalse();
            Directory.GetFiles(data, "*.json", SearchOption.AllDirectories).Should().NotBeEmpty();
        }
        finally
        {
            temporary.Delete(recursive: true);
        }
    }

    [Fact]
    public void Custom_template_preserves_mode_guidance_and_tool_restrictions()
    {
        var services = new ServiceCollection();
        services.AddCodaiApplication(OptionsFor("ollama"), @"C:\project",
            mode: AgentMode.Plan, systemPrompt: "CUSTOM SYSTEM TEMPLATE");
        using var provider = services.BuildServiceProvider();

        var request = provider.GetRequiredService<IPromptBuilder>().Build(
            new ProjectContext { Files = [], RawCodes = [] }, [], "task",
            new PromptExtras { Mode = AgentMode.Plan, Instructions = "Project rule" });

        request.SystemPrompt.Should().Contain("CUSTOM SYSTEM TEMPLATE").And.Contain("Project rule");
        request.AllowedTools.Should().Contain("read_file").And.NotContain("write_file");
    }

    [Fact]
    public async Task Host_neutral_contract_runs_one_task_through_the_composed_runtime()
    {
        const string root = @"C:\project";
        var chat = new Fakes.ScriptedChatCompletionService();
        chat.Enqueue("One-shot answer.");
        var ui = new Fakes.FakeUserInterface();
        ui.EnqueueInput("Must remain unread");
        var services = new ServiceCollection();
        services.AddSingleton<IUserInterface>(ui);
        services.AddCodaiApplication(OptionsFor("ollama"), root);
        services.Replace(ServiceDescriptor.Singleton<IChatCompletionService>(chat));
        services.Replace(ServiceDescriptor.Singleton<IProjectScanner>(new FakeProjectScanner()));
        services.Replace(ServiceDescriptor.Singleton<IFileSystem>(new Fakes.InMemoryFileSystem(root)));
        using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<ICodaiSession>().RunTaskAsync("Explain code");

        result.Should().Be(0, "errors: " + string.Join(" | ", ui.Errors));
        chat.Requests.Should().ContainSingle();
        (await ui.ReadInputAsync()).Should().Be("Must remain unread");
    }

    private static CodaiOptions OptionsFor(string provider, string? model = null) => new()
    {
        Theme = CodaiDefaults.Theme,
        AiProviderConfig = new AiProviderOptions
        {
            Provider = provider,
            Model = model ?? string.Empty,
            BaseUrl = "http://localhost:11434/api",
        },
    };

    [Fact]
    public void Resolves_a_fully_wired_session()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IUserInterface>(new Fakes.FakeUserInterface());
        services.AddCodaiApplication(OptionsFor("ollama"), @"C:\project");

        using var provider = services.BuildServiceProvider();

        var session = provider.GetRequiredService<CodeSession>();

        session.Should().NotBeNull();
        provider.GetRequiredService<IChatCompletionService>().ProviderName.Should().Be("ollama");
        provider.GetRequiredService<IProjectScanner>().Should().NotBeNull();
        provider.GetRequiredService<IPricingCatalog>().Should().NotBeNull();
        provider.GetRequiredService<ICostCalculator>().Should().NotBeNull();
        provider.GetRequiredService<IPromptBuilder>().Should().NotBeNull();
        provider.GetRequiredService<IRequestedContextResolver>().Should().NotBeNull();
        provider.GetRequiredService<IProjectInstructionsProvider>().Should().NotBeNull();
        provider.GetRequiredService<IMentionResolver>().Should().NotBeNull();
        provider.GetRequiredService<ISessionStore>().Should().NotBeNull();
        provider.GetRequiredService<HistoryCompactor>().Should().NotBeNull();
    }

    [Fact]
    public void Composition_uses_configured_consecutive_failure_limit()
    {
        var options = OptionsFor("ollama");
        options.Budget.MaxConsecutiveToolFailures = 2;
        var services = new ServiceCollection();
        services.AddCodaiApplication(options, @"C:\project");
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ToolCallGovernor>().MaxConsecutiveFailures.Should().Be(2);
    }

    [Fact]
    public void Session_does_not_resume_unless_asked()
    {
        var services = new ServiceCollection();
        services.AddCodaiApplication(OptionsFor("ollama"), @"C:\project");

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<SessionOptions>().ResumePreviousSession.Should().BeFalse();
    }

    [Fact]
    public void Session_resumes_when_asked()
    {
        var services = new ServiceCollection();
        services.AddCodaiApplication(OptionsFor("ollama"), @"C:\project", resumePreviousSession: true);

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<SessionOptions>().ResumePreviousSession.Should().BeTrue();
    }

    [Fact]
    public void Applies_provider_specific_model_defaults()
    {
        var services = new ServiceCollection();
        services.AddCodaiApplication(OptionsFor("deepseek"), @"C:\project");

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<SessionOptions>();

        options.Provider.Should().Be("deepseek");
        options.Model.Should().Be("deepseek-flash");
        options.ProjectRoot.Should().Be(@"C:\project");
    }

    [Fact]
    public void The_session_starts_in_agent_mode_and_summarises_by_default()
    {
        var services = new ServiceCollection();
        services.AddCodaiApplication(OptionsFor("ollama"), @"C:\project");

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<SessionOptions>().Mode.Should().Be(AgentMode.Agent);
        provider.GetRequiredService<SessionOptions>().AutoCompact.Should().BeTrue();
        provider.GetRequiredService<AgentModeState>().Mode.Should().Be(AgentMode.Agent);
    }

    [Fact]
    public void The_host_can_start_the_session_read_only_and_without_compaction()
    {
        var services = new ServiceCollection();
        services.AddCodaiApplication(
            OptionsFor("ollama"),
            @"C:\project",
            mode: AgentMode.Plan,
            autoCompact: false);

        using var provider = services.BuildServiceProvider();

        // The mode is the running one too, not only the recorded starting point.
        provider.GetRequiredService<AgentModeState>().Mode.Should().Be(AgentMode.Plan);
        provider.GetRequiredService<SessionOptions>().AutoCompact.Should().BeFalse();
    }

    [Fact]
    public void The_command_runner_is_absent_unless_the_host_opts_in()
    {
        var withoutShell = new ServiceCollection();
        withoutShell.AddCodaiApplication(OptionsFor("ollama"), @"C:\project");

        using (var provider = withoutShell.BuildServiceProvider())
        {
            Name(provider).Should().NotContain("run_command");
        }

        var withShell = new ServiceCollection();
        withShell.AddCodaiApplication(OptionsFor("ollama"), @"C:\project", allowShell: true);

        using (var provider = withShell.BuildServiceProvider())
        {
            Name(provider).Should().Contain("run_command");
        }

        static IEnumerable<string> Name(IServiceProvider provider) =>
            provider.GetRequiredService<IToolRegistry>().Tools.Select(tool => tool.Name);
    }

    [Fact]
    public void The_sub_agent_is_absent_unless_the_host_opts_in()
    {
        var without = new ServiceCollection();
        without.AddCodaiApplication(OptionsFor("ollama"), @"C:\project");

        using (var provider = without.BuildServiceProvider())
        {
            Name(provider).Should().NotContain(SubAgent.ToolName);
            provider.GetService<ISubAgent>().Should().BeNull();
        }

        var with = new ServiceCollection();
        with.AddCodaiApplication(OptionsFor("ollama"), @"C:\project", allowSubAgents: true);

        using (var provider = with.BuildServiceProvider())
        {
            Name(provider).Should().Contain(SubAgent.ToolName);
            provider.GetRequiredService<ISubAgent>().Should().NotBeNull();
        }

        static IEnumerable<string> Name(IServiceProvider provider) =>
            provider.GetRequiredService<IToolRegistry>().Tools.Select(tool => tool.Name);
    }

    [Fact]
    public void Defaults_the_project_root_to_the_current_directory()
    {
        var services = new ServiceCollection();
        services.AddCodaiApplication(OptionsFor("ollama"));

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<SessionOptions>();

        options.ProjectRoot.Should().Be(Directory.GetCurrentDirectory());
    }
}
