using Codai.Cli.Ui;
using Codai.Configuration;
using Codai.Core.Abstractions;
using Codai.Providers;
using Codai.Session;
using Microsoft.Extensions.DependencyInjection;

namespace Codai.Cli;

/// <summary>
/// Entry point wiring: parses the command line, loads configuration, composes the
/// service provider and runs the interactive coding session.
/// </summary>
public sealed class CliApplication
{
    /// <summary>Subcommand that starts the interactive assistant.</summary>
    public const string CodeCommand = "code";

    private readonly IConsoleOutput _output;
    private readonly Func<string> _currentDirectory;
    private readonly IUserInterface? _uiOverride;

    /// <summary>
    /// Creates the application.
    /// </summary>
    /// <param name="output">Console used for diagnostics (defaults to the real console).</param>
    /// <param name="currentDirectory">Working-directory provider (defaults to <see cref="Directory.GetCurrentDirectory"/>).</param>
    /// <param name="uiOverride">User interface override, used by tests.</param>
    public CliApplication(
        IConsoleOutput? output = null,
        Func<string>? currentDirectory = null,
        IUserInterface? uiOverride = null)
    {
        _output = output ?? SystemConsoleOutput.Instance;
        _currentDirectory = currentDirectory ?? Directory.GetCurrentDirectory;
        _uiOverride = uiOverride;
    }

    /// <summary>Runs the CLI and returns the process exit code.</summary>
    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (WantsHelp(args))
        {
            WriteHelp();
            return 0;
        }

        var parsed = CommandLineParser.Parse(args);

        if (parsed.Errors.Count > 0)
        {
            foreach (var error in parsed.Errors)
            {
                _output.WriteError(error);
            }

            _output.WriteError("Run 'codai --help' for usage.");
            return 1;
        }

        if (parsed.ShowVersion)
        {
            _output.WriteLine($"version: {CodaiDefaults.Version}");
            return 0;
        }

        if (!HasCodeCommand(args))
        {
            WriteHelp();
            return 0;
        }

        var workingDirectory = _currentDirectory();

        CodaiOptions options;
        try
        {
            options = CodaiConfigurationLoader.Load(new CodaiConfigurationRequest(
                WorkingDirectory: workingDirectory,
                ConfigFilePath: parsed.ConfigPath,
                CommandLineArgs: args));
        }
        catch (CodaiConfigurationException ex)
        {
            _output.WriteError(ex.Message);
            return 1;
        }

        var services = new ServiceCollection();
        services.AddSingleton<IUserInterface>(_uiOverride ?? new SpectreUserInterface());
        services.AddCodaiSession(options, workingDirectory);

        await using var provider = services.BuildServiceProvider();

        try
        {
            var session = provider.GetRequiredService<CodeSession>();
            return await session.RunAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ProviderConfigurationException or ChatProviderException)
        {
            _output.WriteError(ex.Message);
            return 1;
        }
    }

    /// <summary>True when the first argument is the <c>code</c> subcommand.</summary>
    public static bool HasCodeCommand(string[] args) =>
        args.Length > 0 && string.Equals(args[0], CodeCommand, StringComparison.OrdinalIgnoreCase);

    private static bool WantsHelp(string[] args) =>
        args.Length == 0 || args.Any(arg => arg is "--help" or "-h" or "help");

    private void WriteHelp()
    {
        _output.WriteLine("codai — AI code assistant that works from the full context of your project.");
        _output.WriteLine(string.Empty);
        _output.WriteLine("Usage:");
        _output.WriteLine("  codai code [options]");
        _output.WriteLine("  codai --version");
        _output.WriteLine(string.Empty);
        _output.WriteLine("Options:");
        _output.WriteLine("  -c, --config <path>        Configuration file (codai-config.yml|yaml|json).");
        _output.WriteLine("      --provider <name>      Chat provider: openai, azure-openai, anthropic, gemini,");
        _output.WriteLine("                             ollama, deepseek, openrouter, qwen, mistral, grok.");
        _output.WriteLine("      --base_url <url>       Provider endpoint override.");
        _output.WriteLine("      --model <name>         Model identifier (default: deepseek-flash for deepseek).");
        _output.WriteLine("      --temperature <n>      Sampling temperature.");
        _output.WriteLine("      --reasoning_effort <l> Reasoning effort (low|medium|high).");
        _output.WriteLine("      --max_tokens <n>       Maximum output tokens.");
        _output.WriteLine("      --stream <bool>        Stream the response (default: true).");
        _output.WriteLine("      --api_key <key>        API key (prefer the API_KEY environment variable).");
        _output.WriteLine("      --api_version <v>      API version (Azure OpenAI).");
        _output.WriteLine("  -v, --version              Print the version and exit.");
        _output.WriteLine("  -h, --help                 Print this help and exit.");
        _output.WriteLine(string.Empty);
        _output.WriteLine("Session commands:");
        _output.WriteLine("  " + SlashCommandParser.HelpText.Replace("\n", "\n  ", StringComparison.Ordinal));
    }
}
