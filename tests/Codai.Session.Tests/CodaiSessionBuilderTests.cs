using Codai.Configuration;
using Codai.Core.Abstractions;
using Codai.Core.Agents;
using Codai.Core.Tools;
using Codai.Tokens.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Codai.Session.Tests;

public class CodaiSessionBuilderTests
{
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
        services.AddCodaiSession(OptionsFor("ollama"), @"C:\project");

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
    public void Session_does_not_resume_unless_asked()
    {
        var services = new ServiceCollection();
        services.AddCodaiSession(OptionsFor("ollama"), @"C:\project");

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<SessionOptions>().ResumePreviousSession.Should().BeFalse();
    }

    [Fact]
    public void Session_resumes_when_asked()
    {
        var services = new ServiceCollection();
        services.AddCodaiSession(OptionsFor("ollama"), @"C:\project", resumePreviousSession: true);

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<SessionOptions>().ResumePreviousSession.Should().BeTrue();
    }

    [Fact]
    public void Applies_provider_specific_model_defaults()
    {
        var services = new ServiceCollection();
        services.AddCodaiSession(OptionsFor("deepseek"), @"C:\project");

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
        services.AddCodaiSession(OptionsFor("ollama"), @"C:\project");

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<SessionOptions>().Mode.Should().Be(AgentMode.Agent);
        provider.GetRequiredService<SessionOptions>().AutoCompact.Should().BeTrue();
        provider.GetRequiredService<AgentModeState>().Mode.Should().Be(AgentMode.Agent);
    }

    [Fact]
    public void The_host_can_start_the_session_read_only_and_without_compaction()
    {
        var services = new ServiceCollection();
        services.AddCodaiSession(
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
        withoutShell.AddCodaiSession(OptionsFor("ollama"), @"C:\project");

        using (var provider = withoutShell.BuildServiceProvider())
        {
            Name(provider).Should().NotContain("run_command");
        }

        var withShell = new ServiceCollection();
        withShell.AddCodaiSession(OptionsFor("ollama"), @"C:\project", allowShell: true);

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
        without.AddCodaiSession(OptionsFor("ollama"), @"C:\project");

        using (var provider = without.BuildServiceProvider())
        {
            Name(provider).Should().NotContain(SubAgent.ToolName);
            provider.GetService<ISubAgent>().Should().BeNull();
        }

        var with = new ServiceCollection();
        with.AddCodaiSession(OptionsFor("ollama"), @"C:\project", allowSubAgents: true);

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
        services.AddCodaiSession(OptionsFor("ollama"));

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<SessionOptions>();

        options.ProjectRoot.Should().Be(Directory.GetCurrentDirectory());
    }
}
