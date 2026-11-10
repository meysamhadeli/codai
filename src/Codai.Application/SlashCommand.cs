namespace Codai.Application;

/// <summary>
/// The slash commands accepted inside an interactive coding session.
/// </summary>
public enum SlashCommand
{
    /// <summary>The input is a normal request.</summary>
    None,

    /// <summary><c>:help</c> — list the available session commands.</summary>
    Help,

    /// <summary><c>:clear</c> — clear the screen.</summary>
    Clear,

    /// <summary><c>:exit</c> — leave the session.</summary>
    Exit,

    /// <summary><c>:token</c> — show token usage for the session.</summary>
    Token,

    /// <summary><c>:clear-token</c> — reset the token counters.</summary>
    ClearToken,

    /// <summary><c>:clear-history</c> — forget the conversation so far.</summary>
    ClearHistory,

    /// <summary><c>:mode</c> — show or change how much the assistant may do.</summary>
    Mode,

    /// <summary><c>:compact</c> — summarise the conversation so far and keep only the summary.</summary>
    Compact,

    /// <summary><c>:new-task</c> — create a compact handoff for a fresh task.</summary>
    NewTask,

    /// <summary><c>:new-rule</c> — create a project rule file.</summary>
    NewRule,

    /// <summary><c>:report-bug</c> — save a local diagnostic report.</summary>
    ReportBug,

    /// <summary><c>:task</c> — start, list, or cancel background agents.</summary>
    Task,

    /// <summary><c>:team</c> — coordinate a bounded group of read-only specialists.</summary>
    Team,

    /// <summary><c>:repair</c> — run bounded diagnostics and repair attempts.</summary>
    Repair,

    /// <summary><c>:act</c> — review and execute the latest completed Plan proposal.</summary>
    Act,

    /// <summary><c>:deep-plan</c> — create a detailed, read-only implementation plan.</summary>
    DeepPlan,

    /// <summary><c>:model</c> — inspect or change the model for a session mode.</summary>
    Model,

    /// <summary><c>:config</c> — report the resolved configuration of the running session.</summary>
    Config,
    Settings,
    Session,
    Agents,
    Connect,
    Debug,
    Diff,
    Editor,
    Init,
    Mcps,
    Models,
    New,
    Review,
    Sessions,
    Skills,
    Status,
    Themes,
    Variants,
    /// <summary><c>/autocompact</c> — choose the automatic-compaction window.</summary>
    AutoCompact,
}

/// <summary>
/// Recognises OpenCode-style slash commands and legacy colon commands.
/// </summary>
public static class SlashCommandParser
{
    /// <summary>Help text and completion metadata shown by <c>/help</c>.</summary>
    public const string HelpText =
        "/agents [agent|plan]  Switch agent\n" +
        "/auth  Authenticate provider\n" +
        "/autocompact [auto|tokens]  Set how full context gets before auto-summarizing\n" +
        "/compact [focus]  Free up context by summarizing the conversation so far\n" +
        "/diff  View working-tree and staged changes\n" +
        "/exit  Exit the app\n" +
        "/help  Help\n" +
        "/init [focus]  Guided AGENTS.md setup\n" +
        "/mcps  Manage MCPs\n" +
        "/models [model]  Switch model\n" +
        "/new  New session\n" +
        "/session  Switch session\n" +
        "/settings [config|status|usage|stats]  Configuration and runtime views\n" +
        "/skills  Skills\n" +
        "/themes  Switch theme";

    /// <summary>Maps a raw input line onto a <see cref="SlashCommand"/>.</summary>
    /// <remarks>
    /// Only the command name is considered, so <c>:mode agent</c> parses as
    /// <see cref="SlashCommand.Mode"/>. The arguments are read with
    /// <see cref="Arguments"/>.
    /// </remarks>
    public static SlashCommand Parse(string? input) =>
        TrySplit(input, out var name, out _)
            ? input!.TrimStart()[0] == '/' ? MapSlash(name) : Map(name)
            : SlashCommand.None;

    /// <summary>
    /// Everything the user typed after the command name, or an empty string when the
    /// input is not a command.
    /// </summary>
    public static string Arguments(string? input) =>
        TrySplit(input, out _, out var arguments) ? arguments : string.Empty;

    /// <summary>
    /// Splits a <c>/name arguments</c> or <c>:name arguments</c> line into its name and arguments.
    /// </summary>
    /// <returns>
    /// False when the input is not a command line at all, which is the case for blank
    /// input, a bare prefix, or input without a slash or colon prefix.
    /// </returns>
    public static bool TrySplit(string? input, out string name, out string arguments)
    {
        name = string.Empty;
        arguments = string.Empty;

        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var trimmed = input.Trim();

        if (trimmed.Length < 2 || trimmed[0] is not (':' or '/'))
        {
            return false;
        }

        var body = trimmed[1..];
        var separator = body.IndexOfAny([' ', '\t']);

        name = (separator < 0 ? body : body[..separator]).ToLowerInvariant();
        arguments = separator < 0 ? string.Empty : body[(separator + 1)..].Trim();

        return name.Length > 0;
    }

    private static SlashCommand MapSlash(string name) => name switch
    {
        "agents" => SlashCommand.Agents,
        "auth" => SlashCommand.Connect,
        "autocompact" => SlashCommand.AutoCompact,
        "compact" => SlashCommand.Compact,
        "diff" => SlashCommand.Diff,
        "exit" => SlashCommand.Exit,
        "help" => SlashCommand.Help,
        "init" => SlashCommand.Init,
        "mcps" => SlashCommand.Mcps,
        "models" => SlashCommand.Models,
        "new" => SlashCommand.New,
        "session" or "sessions" => SlashCommand.Sessions,
        "settings" => SlashCommand.Settings,
        "skills" => SlashCommand.Skills,
        "themes" => SlashCommand.Themes,
        _ => SlashCommand.None,
    };

    private static SlashCommand Map(string name) => name switch
    {
        "help" => SlashCommand.Help,
        "clear" => SlashCommand.Clear,
        "exit" or "quit" => SlashCommand.Exit,
        "token" => SlashCommand.Token,
        "clear-token" => SlashCommand.ClearToken,
        "clear-history" => SlashCommand.ClearHistory,
        "mode" => SlashCommand.Mode,
        "compact" => SlashCommand.Compact,
        "new-task" => SlashCommand.NewTask,
        "autocompact" => SlashCommand.AutoCompact,
        "new-rule" => SlashCommand.NewRule,
        "report-bug" => SlashCommand.ReportBug,
        "task" => SlashCommand.Task,
        "team" => SlashCommand.Team,
        "repair" => SlashCommand.Repair,
        "act" => SlashCommand.Act,
        "deep-plan" or "deep-planning" => SlashCommand.DeepPlan,
        "model" => SlashCommand.Model,
        "config" or "settings" => SlashCommand.Config,
        "preferences" => SlashCommand.Settings,
        "session" => SlashCommand.Session,
        _ => SlashCommand.None,
    };
}
