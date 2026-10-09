using Codai.Core.Agents;

namespace Codai.Configuration;

/// <summary>
/// Result of parsing the process command line.
/// </summary>
/// <param name="ConfigPath">Value of <c>--config</c>/<c>-c</c>, when supplied.</param>
/// <param name="ShowVersion">True when <c>--version</c>/<c>-v</c> was supplied.</param>
/// <param name="ContinueSession">True when <c>--continue</c> was supplied.</param>
/// <param name="Mode">Value of <c>--mode</c>, defaulting to the mode the session starts in.</param>
/// <param name="AllowShell">
/// True when the model may be offered the command-running tool. Defaults to true: the
/// CLI can ask before every call, and a coding agent that cannot run the project's tests
/// is not much of an agent.
/// </param>
/// <param name="AutoCompact">
/// True unless <c>--no-auto-compact</c> was supplied. A conversation that would not fit
/// the model's context window is summarised instead of being sent.
/// </param>
/// <param name="Overrides">Flag values keyed by configuration key.</param>
/// <param name="Errors">Problems detected while parsing.</param>
public sealed record CommandLineParseResult(
    string? ConfigPath,
    bool ShowVersion,
    bool ContinueSession,
    AgentMode Mode,
    bool AllowShell,
    bool AutoCompact,
    IReadOnlyDictionary<string, string?> Overrides,
    IReadOnlyList<string> Errors)
{
    public static CommandLineParseResult Empty { get; } =
        new(
            null,
            false,
            false,
            AgentModeParser.Default,
            true,
            true,
            new Dictionary<string, string?>(),
            []);
}
