using System.Text.Json;
using Codai.Configuration;
using Codai.Core.Tests.Fakes;
using FluentAssertions;

namespace Codai.Core.Tests;

public class ConfigurationLoaderTests
{
    [Theory]
    [InlineData(".json")]
    [InlineData(".yaml")]
    public async Task Connection_settings_are_validated_together_before_existing_file_changes(string extension)
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "codai-config" + extension);
        var fileSystem = new InMemoryFileSystem().AddFile(path, "{\"custom_note\":\"keep\"}");
        var manager = new CodaiConfigurationManager(fileSystem);
        var values = new Dictionary<string, string>
        {
            ["ai_provider_config.provider"] = ProviderCatalog.Ollama,
            ["ai_provider_config.base_url"] = "file:///private",
        };
        Func<Task> invalid = () => manager.SetValuesAsync(path, values, temp.Path);
        await invalid.Should().ThrowAsync<CodaiConfigurationException>();
        fileSystem.Files[InMemoryFileSystem.Normalize(path)].Should().Be("{\"custom_note\":\"keep\"}");
        values["ai_provider_config.base_url"] = "http://localhost:11434/api";
        values["ai_provider_config.model"] = "codai-local";
        await manager.SetValuesAsync(path, values, temp.Path);
        var saved = fileSystem.Files[InMemoryFileSystem.Normalize(path)];
        saved.Should().Contain("keep").And.Contain("codai-local");
    }

    private static readonly FakeEnvironmentReader NoEnvironment = new();

    [Theory]
    [InlineData(".json")]
    [InlineData(".yaml")]
    public async Task Settings_editor_preserves_document_fields_and_writes_typed_values(string extension)
    {
        using var temp = new TempDirectory();
        var name = "codai-config" + extension;
        var path = Path.Combine(temp.Path, name);
        var fileSystem = new InMemoryFileSystem().AddFile(path, "{\"custom_note\":\"keep\"}");
        var manager = new CodaiConfigurationManager(fileSystem);
        await manager.SetValueAsync(path, "ai_provider_config.temperature", "1.25", temp.Path);
        await manager.SetValueAsync(path, "ai_provider_config.max_tokens", "512", temp.Path);
        await manager.SetValueAsync(path, "ai_provider_config.stream", "false", temp.Path);
        await manager.SetValueAsync(path, "theme", "forest", temp.Path);
        var saved = fileSystem.Files[InMemoryFileSystem.Normalize(path)];
        saved.Should().Contain("keep");
        temp.WriteFile(name, saved);
        var options = Load(temp);
        options.AiProviderConfig.Temperature.Should().Be(1.25f);
        options.AiProviderConfig.MaxTokens.Should().Be(512);
        options.AiProviderConfig.Stream.Should().BeFalse();
        options.Theme.Should().Be("forest");
    }

    [Theory]
    [InlineData("ai_provider_config.temperature", "NaN")]
    [InlineData("ai_provider_config.temperature", "3")]
    [InlineData("budget.max_cost", "-1")]
    [InlineData("ai_provider_config.base_url", "file:///private")]
    [InlineData("ai_provider_config.base_url", "https://user:secret@example.test")]
    [InlineData("checkpoints_enabled", "yes")]
    [InlineData("theme", "dracula")]
    [InlineData("unknown.path", "value")]
    public async Task Settings_editor_rejects_invalid_values_without_touching_file(string key, string value)
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "codai-config.json");
        var fileSystem = new InMemoryFileSystem().AddFile(path, "{}");
        var manager = new CodaiConfigurationManager(fileSystem);
        Func<Task> save = () => manager.SetValueAsync(path, key, value, temp.Path);
        await save.Should().ThrowAsync<CodaiConfigurationException>();
        fileSystem.Files[InMemoryFileSystem.Normalize(path)].Should().Be("{}");
    }

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
        options.AiProviderConfig.Model.Should().Be(ProviderDefaults.GetDefaultModel(CodaiDefaults.Provider));
        options.AiProviderConfig.Stream.Should().BeTrue();
        options.LoadedFrom.Should().BeNull();
    }

    [Fact]
    public void Tool_failure_limit_defaults_to_three()
    {
        using var temp = new TempDirectory();

        Load(temp).Budget.MaxConsecutiveToolFailures.Should().Be(3);
    }

    [Fact]
    public void Checkpoint_retention_defaults_to_ten_and_loads_from_configuration()
    {
        using var temp = new TempDirectory();

        Load(temp).CheckpointRetention.Should().Be(10);
        temp.WriteFile("codai-config.yml", "checkpoint_retention: 4");

        Load(temp).CheckpointRetention.Should().Be(4);
    }

    [Fact]
    public void Checkpoints_are_enabled_by_default_and_can_be_disabled()
    {
        using var temp = new TempDirectory();

        Load(temp).CheckpointsEnabled.Should().BeTrue();
        Load(temp, args: ["--no-checkpoints"]).CheckpointsEnabled.Should().BeFalse();
    }

    [Fact]
    public void Rejects_non_positive_checkpoint_retention()
    {
        using var temp = new TempDirectory();

        Action act = () => Load(temp, args: ["--checkpoint-retention", "0"]);

        act.Should().Throw<CodaiConfigurationException>();
    }

    [Fact]
    public void Loads_mode_provider_and_model_overrides()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("codai-config.yml", "plan_provider: ollama\nplan_model: qwen\nagent_provider: deepseek\nagent_model: deepseek-chat");

        var options = Load(temp);

        options.PlanProvider.Should().Be("ollama");
        options.PlanModel.Should().Be("qwen");
        options.AgentProvider.Should().Be("deepseek");
        options.AgentModel.Should().Be("deepseek-chat");
    }

    [Theory]
    [InlineData("codai-config.yml", "budget:\n  max_consecutive_tool_failures: 5")]
    [InlineData("codai-config.json", "{\"budget\":{\"max_consecutive_tool_failures\":5}}")]
    public void Loads_tool_failure_limit_from_configuration(string name, string contents)
    {
        using var temp = new TempDirectory();
        temp.WriteFile(name, contents);

        Load(temp).Budget.MaxConsecutiveToolFailures.Should().Be(5);
    }

    [Fact]
    public void Retry_flag_overrides_file_and_environment()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("codai-config.yml", "budget:\n  max_consecutive_tool_failures: 5");
        var environment = new FakeEnvironmentReader(new Dictionary<string, string?>
        {
            ["BUDGET__MAX_CONSECUTIVE_TOOL_FAILURES"] = "4",
        });

        Load(temp, environment: environment).Budget.MaxConsecutiveToolFailures.Should().Be(4);
        Load(temp, args: ["--retries", "2"], environment: environment)
            .Budget.MaxConsecutiveToolFailures.Should().Be(2);
        Load(temp, args: ["--retries=1"]).Budget.MaxConsecutiveToolFailures.Should().Be(1);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("not-a-number")]
    public void Rejects_invalid_tool_failure_limits(string value)
    {
        using var temp = new TempDirectory();
        Action act = () => Load(temp, args: [$"--retries={value}"]);

        act.Should().Throw<CodaiConfigurationException>();
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
        options.AiProviderConfig.ReasoningEffort.Should().Be(ReasoningEffortLevel.Low);
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
    public void Loads_external_tool_enablement_and_disabled_plugins()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("codai-config.json", """
            {
              "disabled_plugins": ["plugins/disabled.dll"],
              "mcp": {"servers": [{"name": "offline", "command": "server", "enabled": false}]}
            }
            """);

        var options = Load(temp);

        options.DisabledPlugins.Should().ContainSingle().Which.Should().Be("plugins/disabled.dll");
        options.Mcp.Servers.Should().ContainSingle().Which.Enabled.Should().BeFalse();
    }

    [Fact]
    public async Task Configuration_manager_persists_external_toggles_and_removes_only_entries()
    {
        using var temp = new TempDirectory();
        var configPath = Path.Combine(temp.Path, "codai-config.json");
        var pluginPath = Path.Combine(temp.Path, "plugins", "sample.dll");
        var fileSystem = new InMemoryFileSystem()
            .AddFile(configPath, """
                {
                  "plugins": ["plugins/sample.dll"],
                  "mcp": {"servers": [{"name": "docs", "command": "server", "enabled": true}]}
                }
                """)
            .AddFile(pluginPath, "plugin-binary-placeholder");
        var manager = new CodaiConfigurationManager(fileSystem);

        await manager.MutateAsync(
            configPath,
            new ConfigurationMutation(ConfigurationMutationTarget.Mcp, ConfigurationMutationAction.Disable),
            "docs",
            temp.Path);
        await manager.MutateAsync(
            configPath,
            new ConfigurationMutation(ConfigurationMutationTarget.Plugin, ConfigurationMutationAction.Disable),
            "plugins/sample.dll",
            temp.Path);

        var disabled = JsonDocument.Parse(fileSystem.Files[InMemoryFileSystem.Normalize(configPath)]).RootElement;
        disabled.GetProperty("mcp").GetProperty("servers")[0].GetProperty("enabled").GetBoolean().Should().BeFalse();
        disabled.GetProperty("plugins").GetArrayLength().Should().Be(0);
        disabled.GetProperty("disabled_plugins")[0].GetString().Should().Be("plugins/sample.dll");

        await manager.MutateAsync(
            configPath,
            new ConfigurationMutation(ConfigurationMutationTarget.Plugin, ConfigurationMutationAction.Enable),
            pluginPath,
            temp.Path);
        await manager.MutateAsync(
            configPath,
            new ConfigurationMutation(ConfigurationMutationTarget.Mcp, ConfigurationMutationAction.Remove),
            "docs",
            temp.Path);
        await manager.MutateAsync(
            configPath,
            new ConfigurationMutation(ConfigurationMutationTarget.Plugin, ConfigurationMutationAction.Remove),
            "plugins/sample.dll",
            temp.Path);

        var final = JsonDocument.Parse(fileSystem.Files[InMemoryFileSystem.Normalize(configPath)]).RootElement;
        final.GetProperty("mcp").GetProperty("servers").GetArrayLength().Should().Be(0);
        final.GetProperty("plugins").GetArrayLength().Should().Be(0);
        final.GetProperty("disabled_plugins").GetArrayLength().Should().Be(0);
        fileSystem.Files.Should().ContainKey(InMemoryFileSystem.Normalize(pluginPath));
    }

    [Fact]
    public async Task Configuration_manager_rejects_non_dll_plugin_paths_without_writing()
    {
        using var temp = new TempDirectory();
        var configPath = Path.Combine(temp.Path, "codai-config.json");
        var fileSystem = new InMemoryFileSystem().AddFile(configPath, "{\"plugins\":[\"plugin.dll\"]}");
        var manager = new CodaiConfigurationManager(fileSystem);

        Func<Task> act = () => manager.MutateAsync(
            configPath,
            new ConfigurationMutation(ConfigurationMutationTarget.Plugin, ConfigurationMutationAction.Remove),
            "plugin.exe",
            temp.Path);

        await act.Should().ThrowAsync<CodaiConfigurationException>();
        fileSystem.Files[InMemoryFileSystem.Normalize(configPath)].Should().Be("{\"plugins\":[\"plugin.dll\"]}");
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
