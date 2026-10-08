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
        services.AddCodaiSession(options, workingDirectory, parsed.ContinueSession);

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
        _output.WriteLine(
            $"""
            codai — AI code assistant that works from the full context of your project.

            Usage:
              codai code [options]
              codai --version

            Options:
              -c, --config <path>         Configuration file (codai-config.yml|yaml|json).
                  --provider <name>       Chat provider: {string.Join(", ", ProviderCatalog.SupportedNames)}.
                  --base-url <url>        Provider endpoint override.
                  --model <name>          Model identifier (provider default applies when omitted).
                  --temperature <n>       Sampling temperature, 0 to 2.
                  --reasoning-effort <l>  Reasoning effort: low | medium | high.
                  --max-tokens <n>        Maximum number of tokens to generate.
                  --stream <bool>         Stream the response (default: true).
                  --api-key <key>         API key (prefer the API_KEY environment variable).
                  --api-version <v>       Version header for providers that need one (Anthropic).
                  --continue              Resume the conversation stored for this project.
              -v, --version               Print the version and exit.
              -h, --help                  Print this help and exit.

            Flags also accept the legacy snake_case spelling (for example --base_url).

            Session commands:
              {SlashCommandParser.HelpText.Replace("\n", "\n  ", StringComparison.Ordinal)}
            """);
    }
}
