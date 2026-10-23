using Codai.Core.Agents;
using Codai.Core.Chat;

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
/// <param name="AllowSubAgents">
/// True unless <c>--no-sub-agents</c> was supplied. <c>task</c> lets the model hand an
/// investigation to a nested agent that explores with read-only tools and reports back,
/// which keeps a long search out of this conversation's context window.
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
    bool AllowSubAgents,
    IReadOnlyDictionary<string, string?> Overrides,
    IReadOnlyList<string> Errors)
{
    public string? ResumeTaskId { get; init; }
    public string? Prompt { get; init; }

    public string? WorkingDirectory { get; init; }

    public bool AutoApprove { get; init; }

    public TimeSpan RunTimeout { get; init; }

    public string? SystemPrompt { get; init; }

    public bool JsonOutput { get; init; }

    public bool Acp { get; init; }

    public string? DataDirectory { get; init; }

    public string? ConfigurationDirectory { get; init; }

    public string? HooksDirectory { get; init; }

    public bool DisableHooks { get; init; }

    public bool ExecutePlan { get; init; }

    public IReadOnlyList<string> DisabledTools { get; init; } = [];

    public CompactionStrategy CompactionStrategy { get; init; } = CompactionStrategy.Agentic;

    public static CommandLineParseResult Empty { get; } =
        new(
            null,
            false,
            false,
            AgentModeParser.Default,
            true,
            true,
            true,
            new Dictionary<string, string?>(),
            []);
}
