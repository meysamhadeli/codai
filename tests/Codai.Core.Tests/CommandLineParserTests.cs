using Codai.Configuration;
using Codai.Core.Agents;
using Codai.Core.Chat;
using FluentAssertions;

namespace Codai.Core.Tests;

public class CommandLineParserTests
{
    [Fact]
    public void Disabled_tools_are_repeatable_run_restrictions_not_prompt_text()
    {
        var result = CommandLineParser.Parse(["--disable-tool", "write_file", "--disable-tool=run_command", "task"]);

        result.Errors.Should().BeEmpty();
        result.DisabledTools.Should().Equal("write_file", "run_command");
        result.Prompt.Should().Be("task");
        result.Overrides.Should().BeEmpty();
        CommandLineParser.Parse(["--", "--disable-tool"]).DisabledTools.Should().BeEmpty();
        CommandLineParser.Parse(["--disable-tool", "--mode", "plan"]).Errors.Should().ContainSingle();
    }

    [Theory]
    [InlineData("--disable-tool")]
    [InlineData("--disable-tool=")]
    [InlineData("--disable-tool= ")]
    public void Missing_disabled_tool_name_is_reported(string option) =>
        CommandLineParser.Parse([option]).Errors.Should().ContainSingle();

    [Fact]
    public void Execute_plan_is_explicit_run_operation_with_optional_instructions()
    {
        var result = CommandLineParser.Parse(["--execute-plan", "Run", "tests"]);

        result.ExecutePlan.Should().BeTrue();
        result.Prompt.Should().Be("Run tests");
        result.Errors.Should().BeEmpty();
        result.Overrides.Should().BeEmpty();
        CommandLineParser.Parse(["--execute-plan"]).Prompt.Should().BeNull();
        CommandLineParser.Parse(["--execute-plan=true"]).Errors.Should().ContainSingle();
        CommandLineParser.Parse(["--", "--execute-plan"]).ExecutePlan.Should().BeFalse();
        CommandLineParser.Parse(["--mode", "ask", "--execute-plan"]).Errors.Should().ContainSingle();
    }
    [Theory]
    [InlineData("agentic", true, CompactionStrategy.Agentic)]
    [InlineData("basic", true, CompactionStrategy.Basic)]
    [InlineData("off", false, CompactionStrategy.Agentic)]
    [InlineData("BASIC", true, CompactionStrategy.Basic)]
    public void Compaction_strategy_is_run_scoped(string option, bool automatic, CompactionStrategy expected)
    {
        var result = CommandLineParser.Parse([$"--compaction={option}", "task"]);

        result.Errors.Should().BeEmpty();
        result.AutoCompact.Should().Be(automatic);
        result.CompactionStrategy.Should().Be(expected);
        result.Prompt.Should().Be("task");
        result.Overrides.Should().BeEmpty();
    }

    [Fact]
    public void Compaction_flags_follow_argument_order()
    {
        CommandLineParser.Parse(["--compaction", "off", "--auto-compact"]).AutoCompact.Should().BeTrue();
        CommandLineParser.Parse(["--compaction", "basic", "--no-auto-compact"]).AutoCompact.Should().BeFalse();
        CommandLineParser.Parse(["--no-auto-compact", "--compaction", "basic"]).AutoCompact.Should().BeTrue();
        CommandLineParser.Parse(["--compaction", "basic", "--compaction", "off"]).CompactionStrategy.Should().Be(CompactionStrategy.Basic);
    }

    [Theory]
    [InlineData("--compaction")]
    [InlineData("--compaction=")]
    [InlineData("--compaction=unknown")]
    [InlineData("--compaction=0")]
    public void Invalid_compaction_strategy_is_reported(string option) =>
        CommandLineParser.Parse([option]).Errors.Should().ContainSingle();

    [Fact]
    public void Structured_output_and_isolated_paths_are_run_options()
    {
        var parsed = CommandLineParser.Parse(["--json", "--data-dir", "../state", "--config-dir=../config", "task"]);
        parsed.JsonOutput.Should().BeTrue();
        parsed.DataDirectory.Should().Be("../state");
        parsed.ConfigurationDirectory.Should().Be("../config");
        parsed.Prompt.Should().Be("task");
        parsed.Overrides.Should().BeEmpty();
        CommandLineParser.Parse(["--json=false"]).Errors.Should().ContainSingle();
        CommandLineParser.Parse(["--data-dir"]).Errors.Should().ContainSingle();
        CommandLineParser.Parse(["--config-dir="]).Errors.Should().ContainSingle();
    }

    [Fact]
    public void System_override_is_run_scoped_and_does_not_consume_task_text()
    {
        var parsed = CommandLineParser.Parse(["-s", "Custom system prompt", "task"]);
        parsed.SystemPrompt.Should().Be("Custom system prompt");
        parsed.Prompt.Should().Be("task");
        parsed.Overrides.Should().BeEmpty();
        CommandLineParser.Parse(["--system="]).Errors.Should().ContainSingle();
        CommandLineParser.Parse(["--system"]).Errors.Should().ContainSingle();
    }

    [Fact]
    public void Timeout_is_run_scoped_and_accepts_invariant_fractional_seconds()
    {
        var parsed = CommandLineParser.Parse(["--timeout", "0.25", "task"]);
        parsed.RunTimeout.Should().Be(TimeSpan.FromMilliseconds(250));
        parsed.Overrides.Should().BeEmpty();
        CommandLineParser.Parse(["-t=0"]).RunTimeout.Should().Be(TimeSpan.Zero);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("4294968")]
    [InlineData("invalid")]
    public void Invalid_timeout_is_reported(string value) =>
        CommandLineParser.Parse([$"--timeout={value}"]).Errors.Should().ContainSingle();

    [Fact]
    public void Auto_approval_requires_explicit_true_and_is_not_a_provider_override()
    {
        CommandLineParser.Parse([]).AutoApprove.Should().BeFalse();
        CommandLineParser.Parse(["task"]).AutoApprove.Should().BeFalse();
        CommandLineParser.Parse(["--auto-approve", "false"]).AutoApprove.Should().BeFalse();
        var parsed = CommandLineParser.Parse(["--auto-approve=true", "task"]);
        parsed.AutoApprove.Should().BeTrue();
        parsed.Prompt.Should().Be("task");
        parsed.Overrides.Should().BeEmpty();
    }

    [Theory]
    [InlineData("yes")]
    [InlineData("")]
    public void Invalid_auto_approval_value_is_an_error(string value)
    {
        CommandLineParser.Parse([$"--auto-approve={value}"]).Errors.Should().ContainSingle();
        CommandLineParser.Parse(["--auto-approve"]).Errors.Should().ContainSingle();
    }

    [Fact]
    public void Positional_prompts_do_not_include_command_or_option_values()
    {
        var parsed = CommandLineParser.Parse(["code", "fix", "tests", "--model", "model-name"]);

        parsed.Prompt.Should().Be("fix tests");
        parsed.Overrides[ConfigKeys.Model].Should().Be("model-name");
        CommandLineParser.Parse(["fix tests"]).Prompt.Should().Be("fix tests");
    }

    [Fact]
    public void End_of_options_preserves_flag_like_prompt_text()
    {
        var parsed = CommandLineParser.Parse(["code", "--", "--help", "--model", "code"]);

        parsed.Prompt.Should().Be("--help --model code");
        parsed.Errors.Should().BeEmpty();
        parsed.Overrides.Should().BeEmpty();
    }

    [Fact]
    public void Working_directory_is_separate_from_configuration_overrides()
    {
        var parsed = CommandLineParser.Parse(["code", "--cwd", "../project", "task"]);

        parsed.WorkingDirectory.Should().Be("../project");
        parsed.Prompt.Should().Be("task");
        parsed.Overrides.Should().BeEmpty();
        CommandLineParser.Parse(["--cwd"]).Errors.Should().ContainSingle();
    }

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
            .Which.Should().Contain("yolo").And.Contain("plan | agent");
    }

    [Theory]
    [InlineData("--mode", "ask")]
    [InlineData("--mode=ASK")]
    public void Removed_ask_mode_is_rejected(params string[] arguments)
    {
        var result = CommandLineParser.Parse(arguments);

        result.Errors.Should().ContainSingle()
            .Which.Should().Contain("Unknown mode").And.Contain("plan | agent");
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
