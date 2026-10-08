using Codai.Configuration;
using Codai.Core.Tests.Fakes;
using FluentAssertions;

namespace Codai.Core.Tests;

public class ConfigurationLoaderTests
{
    private static readonly FakeEnvironmentReader NoEnvironment = new();

    private const string YamlConfig = """
        ai_provider_config:
          provider: "azure-openai"
          base_url: "https://test.openai.azure.com"
          model: "gpt-4o"
          api_version: "2024-04-01-preview"
          temperature: 0.2
          reasoning_effort: "low"
        theme: "dracula"
        """;

    private const string JsonConfig = """
        {
          "theme": "nord",
          "ai_provider_config": {
            "provider": "mistral",
            "model": "mistral-large-latest",
            "max_tokens": 4096
          }
        }
        """;

    private static CodaiOptions Load(TempDirectory temp, string? file = null, IReadOnlyList<string>? args = null,
        IEnvironmentReader? environment = null) =>
        CodaiConfigurationLoader.Load(
            new CodaiConfigurationRequest(temp.Path, file, args),
            environment ?? NoEnvironment);

    [Fact]
    public void Falls_back_to_defaults_when_no_file_exists()
    {
        using var temp = new TempDirectory();

        var options = Load(temp);

        options.Version.Should().Be(CodaiDefaults.Version);
        options.Theme.Should().Be(CodaiDefaults.Theme);
        options.AiProviderConfig.Provider.Should().Be(CodaiDefaults.Provider);
        options.AiProviderConfig.Model.Should().Be(CodaiDefaults.Model);
        options.AiProviderConfig.Stream.Should().BeTrue();
        options.LoadedFrom.Should().BeNull();
    }

    [Fact]
    public void Loads_yaml_configuration()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("codai-config.yml", YamlConfig);

        var options = Load(temp);

        options.AiProviderConfig.Provider.Should().Be("azure-openai");
        options.AiProviderConfig.BaseUrl.Should().Be("https://test.openai.azure.com");
        options.AiProviderConfig.Model.Should().Be("gpt-4o");
        options.AiProviderConfig.ApiVersion.Should().Be("2024-04-01-preview");
        options.AiProviderConfig.Temperature.Should().Be(0.2f);
        options.AiProviderConfig.ReasoningEffort.Should().Be("low");
        options.Theme.Should().Be("dracula");
        options.LoadedFrom.Should().EndWith("codai-config.yml");
    }

    [Fact]
    public void Loads_json_configuration()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("codai-config.json", JsonConfig);

        var options = Load(temp);

        options.AiProviderConfig.Provider.Should().Be("mistral");
        options.AiProviderConfig.Model.Should().Be("mistral-large-latest");
        options.AiProviderConfig.MaxTokens.Should().Be(4096);
        options.Theme.Should().Be("nord");
    }

    [Fact]
    public void Prefers_yml_over_json_when_both_present()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("codai-config.yml", YamlConfig);
        temp.WriteFile("codai-config.json", JsonConfig);

        var options = Load(temp);

        options.AiProviderConfig.Provider.Should().Be("azure-openai");
    }

    [Fact]
    public void Environment_overrides_file()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("codai-config.yml", YamlConfig);

        var env = new FakeEnvironmentReader(new Dictionary<string, string?>
        {
            ["PROVIDER"] = "ollama",
            ["MODEL"] = "llama3",
        });

        var options = Load(temp, environment: env);

        options.AiProviderConfig.Provider.Should().Be("ollama");
        options.AiProviderConfig.Model.Should().Be("llama3");
        options.AiProviderConfig.BaseUrl.Should().Be("https://test.openai.azure.com");
    }

    [Fact]
    public void Supports_nested_environment_variable_form()
    {
        using var temp = new TempDirectory();

        var env = new FakeEnvironmentReader(new Dictionary<string, string?>
        {
            ["AI_PROVIDER_CONFIG__API_KEY"] = "nested-key",
        });

        var options = Load(temp, environment: env);

        options.AiProviderConfig.ApiKey.Should().Be("nested-key");
    }

    [Fact]
    public void Command_line_overrides_environment_and_file()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("codai-config.yml", YamlConfig);

        var env = new FakeEnvironmentReader(new Dictionary<string, string?>
        {
            ["PROVIDER"] = "mistral",
        });

        var args = new[] { "code", "--provider", "grok", "--temperature", "1.5" };

        var options = Load(temp, args: args, environment: env);

        options.AiProviderConfig.Provider.Should().Be("grok");
        options.AiProviderConfig.Temperature.Should().Be(1.5f);
        options.AiProviderConfig.Model.Should().Be("gpt-4o");
    }

    [Fact]
    public void Explicit_config_path_is_honoured()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("custom/settings.yml", YamlConfig);

        var options = Load(temp, file: temp.Combine("custom", "settings.yml"));

        options.AiProviderConfig.Provider.Should().Be("azure-openai");
        options.LoadedFrom.Should().EndWith("settings.yml");
    }

    [Fact]
    public void Throws_when_explicit_config_file_is_missing()
    {
        using var temp = new TempDirectory();

        var act = () => Load(temp, file: temp.Combine("missing.yml"));

        act.Should().Throw<CodaiConfigurationException>()
            .WithMessage("*missing.yml*");
    }

    [Fact]
    public void Throws_for_unsupported_provider()
    {
        using var temp = new TempDirectory();

        var act = () => Load(temp, args: ["--provider", "not-a-provider"]);

        act.Should().Throw<CodaiConfigurationException>()
            .WithMessage("*not-a-provider*");
    }

    [Fact]
    public void Throws_for_temperature_out_of_range()
    {
        using var temp = new TempDirectory();

        var act = () => Load(temp, args: ["--temperature", "3"]);

        act.Should().Throw<CodaiConfigurationException>()
            .WithMessage("*Temperature*");
    }

    [Fact]
    public void Throws_for_unsupported_configuration_file_type()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("codai-config.txt", "irrelevant");

        var act = () => Load(temp, file: temp.Combine("codai-config.txt"));

        act.Should().Throw<CodaiConfigurationException>()
            .WithMessage("*Unsupported configuration file type*");
    }
}
