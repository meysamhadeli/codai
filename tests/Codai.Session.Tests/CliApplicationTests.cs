using Codai.Cli;
using Codai.Configuration;
using FluentAssertions;

namespace Codai.Session.Tests;

public class CliApplicationTests
{
    [Fact]
    public async Task Version_flag_prints_the_version_and_exits()
    {
        var output = new RecordingConsole();

        var exitCode = await new CliApplication(output, () => @"C:\project").RunAsync(["--version"]);

        exitCode.Should().Be(0);
        output.Lines.Should().Contain($"version: {CodaiDefaults.Version}");
    }

    [Fact]
    public async Task Short_version_flag_is_supported()
    {
        var output = new RecordingConsole();

        var exitCode = await new CliApplication(output, () => @"C:\project").RunAsync(["-v"]);

        exitCode.Should().Be(0);
        output.Lines.Should().ContainSingle(line => line.StartsWith("version:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task No_arguments_prints_help()
    {
        var output = new RecordingConsole();

        var exitCode = await new CliApplication(output, () => @"C:\project").RunAsync([]);

        exitCode.Should().Be(0);
        output.Lines.Should().Contain(line => line.Contains("codai code [options]", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Help_flag_prints_usage_including_session_commands()
    {
        var output = new RecordingConsole();

        var exitCode = await new CliApplication(output, () => @"C:\project").RunAsync(["--help"]);

        exitCode.Should().Be(0);
        output.Lines.Should().Contain(line => line.Contains(":clear-token", StringComparison.Ordinal));
        output.Lines.Should().Contain(line => line.Contains(":compact", StringComparison.Ordinal));
        output.Lines.Should().Contain(line => line.Contains(":mode", StringComparison.Ordinal));
        output.Lines.Should().Contain(line => line.Contains("--continue", StringComparison.Ordinal));
        output.Lines.Should().Contain(line => line.Contains("--mode", StringComparison.Ordinal));
        output.Lines.Should().Contain(line => line.Contains("--no-shell", StringComparison.Ordinal));
        output.Lines.Should().Contain(line => line.Contains("--no-auto-compact", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Every_option_in_the_help_text_is_on_its_own_line()
    {
        var output = new RecordingConsole();

        await new CliApplication(output, () => @"C:\project").RunAsync(["--help"]);

        // An option that shares a line with the previous one is invisible to a reader
        // scanning the column, and it is how a missing newline in the raw string shows up.
        var options = string.Join("\n", output.Lines)
            .Split('\n')
            .Where(line => line.TrimStart().StartsWith("--", StringComparison.Ordinal))
            .ToArray();

        options.Should().NotBeEmpty();
        options.Should().OnlyContain(line => line.Split("--").Length == 2);
    }

    [Fact]
    public async Task Unknown_option_is_reported_with_a_non_zero_exit_code()
    {
        var output = new RecordingConsole();

        var exitCode = await new CliApplication(output, () => @"C:\project")
            .RunAsync(["code", "--nope", "1"]);

        exitCode.Should().Be(1);
        output.Errors.Should().Contain("Unknown option '--nope'.");
    }

    [Fact]
    public async Task Missing_configuration_file_is_reported()
    {
        var output = new RecordingConsole();

        var exitCode = await new CliApplication(output, () => @"C:\project")
            .RunAsync(["code", "--config", @"C:\project\does-not-exist.yml"]);

        exitCode.Should().Be(1);
        output.Errors.Should().Contain(message => message.Contains("was not found", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Code_subcommand_is_detected_only_in_the_first_position()
    {
        CliApplication.HasCodeCommand(["code"]).Should().BeTrue();
        CliApplication.HasCodeCommand(["code", "--provider", "openai"]).Should().BeTrue();
        CliApplication.HasCodeCommand(["--provider", "openai", "code"]).Should().BeFalse();
        CliApplication.HasCodeCommand([]).Should().BeFalse();
    }

    private sealed class RecordingConsole : IConsoleOutput
    {
        public List<string> Lines { get; } = [];

        public List<string> Errors { get; } = [];

        public void WriteLine(string message) => Lines.Add(message);

        public void WriteError(string message) => Errors.Add(message);
    }
}
