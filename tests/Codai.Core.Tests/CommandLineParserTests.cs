using Codai.Configuration;
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
    public void Maps_all_supported_flags()
    {
        var result = CommandLineParser.Parse(
        [
            "--theme", "dracula",
            "--base_url", "https://example.test",
            "--reasoning_effort", "high",
            "--api_key", "secret",
            "--api_version", "2024-04-01-preview",
        ]);

        result.Overrides[ConfigKeys.Theme].Should().Be("dracula");
        result.Overrides[ConfigKeys.BaseUrl].Should().Be("https://example.test");
        result.Overrides[ConfigKeys.ReasoningEffort].Should().Be("high");
        result.Overrides[ConfigKeys.ApiKey].Should().Be("secret");
        result.Overrides[ConfigKeys.ApiVersion].Should().Be("2024-04-01-preview");
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
