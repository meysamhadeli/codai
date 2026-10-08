using Codai.Configuration;
using Codai.Core.Abstractions;
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
    public void Defaults_the_project_root_to_the_current_directory()
    {
        var services = new ServiceCollection();
        services.AddCodaiSession(OptionsFor("ollama"));

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<SessionOptions>();

        options.ProjectRoot.Should().Be(Directory.GetCurrentDirectory());
    }
}
