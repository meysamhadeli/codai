using Codai.Configuration;
using Codai.Core.Agents;
using FluentAssertions;

namespace Codai.Core.Tests;

public class CommandLineParserTests
{
    [Fact]
    public void Empty_args_produce_empty_result()
    {
        var result = CommandLineParser.Parse([]);

        result.ConfigPath.Should().BeNull();
        result.ShowVersion.Should().BeFalse();
        result.Overrides.Should().BeEmpty();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Parses_separated_flag_values()
    {
        var result = CommandLineParser.Parse(["code", "--provider", "anthropic", "--model", "claude-3"]);

        result.Overrides[ConfigKeys.Provider].Should().Be("anthropic");
        result.Overrides[ConfigKeys.Model].Should().Be("claude-3");
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Parses_inline_flag_values()
    {
        var result = CommandLineParser.Parse(["code", "--provider=gemini", "--temperature=0.8"]);

        result.Overrides[ConfigKeys.Provider].Should().Be("gemini");
        result.Overrides[ConfigKeys.Temperature].Should().Be("0.8");
    }

    [Fact]
    public void Parses_short_and_long_config_flags()
    {
        CommandLineParser.Parse(["--config", "./a.yml"]).ConfigPath.Should().Be("./a.yml");
        CommandLineParser.Parse(["-c", "./b.yml"]).ConfigPath.Should().Be("./b.yml");
        CommandLineParser.Parse(["--config=./c.yml"]).ConfigPath.Should().Be("./c.yml");
    }

    [Fact]
    public void Parses_version_flag()
    {
        CommandLineParser.Parse(["-v"]).ShowVersion.Should().BeTrue();
        CommandLineParser.Parse(["--version"]).ShowVersion.Should().BeTrue();
    }

    [Fact]
    public void Parses_the_continue_flag()
    {
        var result = CommandLineParser.Parse(["code", "--continue"]);

        result.ContinueSession.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.Overrides.Should().BeEmpty();
    }

    [Fact]
    public void Continue_is_false_unless_asked_for()
    {
        CommandLineParser.Parse(["code"]).ContinueSession.Should().BeFalse();
    }

    [Fact]
    public void Continue_can_be_combined_with_other_flags()
    {
        var result = CommandLineParser.Parse(["code", "--continue", "--model", "gpt-4o"]);

        result.ContinueSession.Should().BeTrue();
        result.Overrides[ConfigKeys.Model].Should().Be("gpt-4o");
    }

    [Theory]
    [InlineData("ask", AgentMode.Ask)]
    [InlineData("plan", AgentMode.Plan)]
    [InlineData("agent", AgentMode.Agent)]
    [InlineData("edit", AgentMode.Agent)]
    public void Parses_the_mode_flag_in_both_spellings(string value, AgentMode expected)
    {
        CommandLineParser.Parse(["code", "--mode", value]).Mode.Should().Be(expected);
        CommandLineParser.Parse(["code", $"--mode={value}"]).Mode.Should().Be(expected);
    }

    [Fact]
    public void The_mode_defaults_to_the_one_that_can_change_files()
    {
        var result = CommandLineParser.Parse(["code"]);

        result.Mode.Should().Be(AgentModeParser.Default);
        result.Mode.Should().Be(AgentMode.Agent);
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void The_mode_is_not_a_configuration_override()
    {
        // It belongs to the session, not to the provider settings, so it must not reach
        // the configuration loader where an unknown key would be ignored silently.
        CommandLineParser.Parse(["code", "--mode", "agent"]).Overrides.Should().BeEmpty();
    }

    [Fact]
    public void An_unknown_mode_is_an_error_that_names_the_accepted_ones()
    {
        var result = CommandLineParser.Parse(["code", "--mode", "yolo"]);

        result.Errors.Should().ContainSingle()
            .Which.Should().Contain("yolo").And.Contain("ask | plan | agent");
    }

    [Fact]
    public void A_missing_mode_value_is_an_error()
    {
        CommandLineParser.Parse(["code", "--mode"]).Errors.Should().ContainSingle()
            .Which.Should().Contain("requires a value");
    }

    [Fact]
    public void The_shell_and_compaction_switches_start_on()
    {
        var result = CommandLineParser.Parse(["code"]);

        result.AllowShell.Should().BeTrue();
        result.AutoCompact.Should().BeTrue();
        result.AllowSubAgents.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData("--no-shell")]
    [InlineData("--no_shell")]
    public void The_shell_switch_turns_command_running_off(string flag)
    {
        var result = CommandLineParser.Parse(["code", flag]);

        result.AllowShell.Should().BeFalse();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void The_compaction_switch_turns_the_automatic_summary_off()
    {
        var result = CommandLineParser.Parse(["code", "--no-auto-compact"]);

        result.AutoCompact.Should().BeFalse();
        result.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData("--no-sub-agents")]
    [InlineData("--no_sub_agents")]
    public void The_delegation_switch_turns_sub_agents_off(string flag)
    {
        var result = CommandLineParser.Parse(["code", flag]);

        result.AllowSubAgents.Should().BeFalse();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Every_switch_can_be_given_back_explicitly()
    {
        var result = CommandLineParser.Parse(
        [
            "code",
            "--no-shell", "--allow-shell",
            "--no-auto-compact", "--auto-compact",
            "--no-sub-agents", "--sub-agents",
        ]);

        result.AllowShell.Should().BeTrue();
        result.AutoCompact.Should().BeTrue();
        result.AllowSubAgents.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void The_switches_are_not_configuration_overrides()
    {
        CommandLineParser
            .Parse(["code", "--no-shell", "--no-auto-compact", "--no-sub-agents"])
            .Overrides
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void Maps_all_supported_flags()
    {
        var result = CommandLineParser.Parse(
        [
            "--theme", "dracula",
            "--base-url", "https://example.test",
            "--reasoning-effort", "high",
            "--api-key", "secret",
            "--api-version", "2024-04-01-preview",
            "--max-tokens", "1024",
            "--stream", "false",
        ]);

        result.Errors.Should().BeEmpty();
        result.Overrides[ConfigKeys.Theme].Should().Be("dracula");
        result.Overrides[ConfigKeys.BaseUrl].Should().Be("https://example.test");
        result.Overrides[ConfigKeys.ReasoningEffort].Should().Be("high");
        result.Overrides[ConfigKeys.ApiKey].Should().Be("secret");
        result.Overrides[ConfigKeys.ApiVersion].Should().Be("2024-04-01-preview");
        result.Overrides[ConfigKeys.MaxTokens].Should().Be("1024");
        result.Overrides[ConfigKeys.Stream].Should().Be("false");
    }

    [Theory]
    [InlineData("base_url", ConfigKeys.BaseUrl)]
    [InlineData("base-url", ConfigKeys.BaseUrl)]
    [InlineData("api_key", ConfigKeys.ApiKey)]
    [InlineData("api-key", ConfigKeys.ApiKey)]
    [InlineData("api_version", ConfigKeys.ApiVersion)]
    [InlineData("api-version", ConfigKeys.ApiVersion)]
    [InlineData("max_tokens", ConfigKeys.MaxTokens)]
    [InlineData("max-tokens", ConfigKeys.MaxTokens)]
    [InlineData("reasoning_effort", ConfigKeys.ReasoningEffort)]
    [InlineData("reasoning-effort", ConfigKeys.ReasoningEffort)]
    public void Accepts_both_kebab_case_and_legacy_snake_case_spellings(
        string flag,
        string expectedKey)
    {
        var result = CommandLineParser.Parse([$"--{flag}", "value"]);

        result.Errors.Should().BeEmpty();
        result.Overrides[expectedKey].Should().Be("value");
    }

    [Fact]
    public void Rejects_the_removed_encoding_format_flag()
    {
        var result = CommandLineParser.Parse(["--encoding_format", "float"]);

        result.Errors.Should().ContainMatch("*encoding_format*");
    }

    [Fact]
    public void Rejects_negative_numbers_as_values()
    {
        // Sanity check that the value scanner still treats a leading '-' followed by a
        // digit as a value rather than the next flag.
        CommandLineParser.Parse(["--temperature", "-1"])
            .Overrides[ConfigKeys.Temperature].Should().Be("-1");
    }

    [Fact]
    public void Accepts_negative_numbers_as_values()
    {
        var result = CommandLineParser.Parse(["--temperature", "-1"]);

        result.Overrides[ConfigKeys.Temperature].Should().Be("-1");
    }

    [Fact]
    public void Reports_unknown_flags()
    {
        var result = CommandLineParser.Parse(["--nope", "value"]);

        result.Errors.Should().ContainSingle().Which.Should().Contain("nope");
    }

    [Fact]
    public void Reports_missing_value()
    {
        var result = CommandLineParser.Parse(["--provider"]);

        result.Errors.Should().ContainSingle().Which.Should().Contain("--provider");
    }

    [Fact]
    public void Ignores_positional_arguments()
    {
        var result = CommandLineParser.Parse(["code", "extra", "--model", "gpt-4o"]);

        result.Errors.Should().BeEmpty();
        result.Overrides[ConfigKeys.Model].Should().Be("gpt-4o");
    }
}
