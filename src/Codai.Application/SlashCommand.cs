using Codai.Application.Inspection;

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
}

/// <summary>
/// Recognises the session slash commands. Names match the original Go CLI for
/// backwards compatibility.
/// </summary>
public static class SlashCommandParser
{
    /// <summary>Help text shown by <c>:help</c>.</summary>
    public const string HelpText =
        "/help  Help for code subcommand\n" +
        "/clear  Clear screen\n" +
        "/exit  Exit from codai\n" +
        "/token  Token information\n" +
        "/clear-token  Clear token from session\n" +
        "/clear-history  Clear history of chat from session\n" +
        "/mode [plan|agent]  Show or change what the assistant is allowed to do\n" +
        "/compact [focus]  Summarize the chat history and keep only the summary\n" +
        "/new-task [focus]  Summarize the current work into a fresh-task handoff\n" +
        "/new-rule <name> [| <rule text>]  Create a project rule under .codai/rules\n" +
        "/report-bug [summary]  Save a local diagnostic report under .codai/reports\n" +
        "/task start <description> | <prompt>  Start a background investigation\n" +
        "/task list  Show background investigations\n" +
        "/task cancel <id>  Cancel a running investigation\n" +
        "/team start <coordinator> | <plan> || <description> | <prompt> ; <description> | <prompt>  Start up to four read-only specialists with optional host-provided plan\n" +
        "/team list  Show teams, board, and bounded mailbox messages\n" +
        "/team add <team-id> <item> [| <specialist-id>]  Add a bounded board item\n" +
        "/team complete <team-id> <item-id>  Complete a board item\n" +
        "/team message <team-id> <from> | <message>  Add a bounded mailbox message\n" +
        "/team cancel <team-id>  Cancel all specialists and wait for shutdown\n" +
        "/task history [query]  Search persisted task history\n" +
        "/task show <task-id>  Show task details\n" +
        "/task resume <task-id>  Resume a saved task conversation\n" +
        "/task export <task-id> <path>  Export task metadata without conversation\n" +
        "/task import <path>  Import task metadata without changing workspace files\n" +
        "/task checkpoints <task-id>  List workspace checkpoints\n" +
        "/task storage  Show checkpoint count and retained storage\n" +
        "/task restore <task-id> <checkpoint-id> [files-only|task-only|files-and-task]  Restore a checkpoint after confirmation\n" +
        "/repair  Run bounded test, build, and repair attempts\n" +
        "/act [instructions]  Execute the latest completed Plan proposal after consent\n" +
        "/deep-plan <request>  Explore the repository and produce a detailed Plan proposal\n" +
        "/model [plan|agent] [provider] [model]  Show or change a mode's model\n" +
        $"/config [{ConfigurationTargets.Names}]  Show the resolved configuration of this session\n" +
        "/preferences [config|provider|theme|mcp|plugins|skills]  Open interactive settings\n" +
        "/session [new|fork|history|restore]  Manage saved conversations and checkpoints";

    /// <summary>Maps a raw input line onto a <see cref="SlashCommand"/>.</summary>
    /// <remarks>
    /// Only the command name is considered, so <c>:mode agent</c> parses as
    /// <see cref="SlashCommand.Mode"/>. The arguments are read with
    /// <see cref="Arguments"/>.
    /// </remarks>
    public static SlashCommand Parse(string? input) =>
        TrySplit(input, out var name, out _) ? Map(name) : SlashCommand.None;

    /// <summary>
    /// Everything the user typed after the command name, or an empty string when the
    /// input is not a command.
    /// </summary>
    public static string Arguments(string? input) =>
        TrySplit(input, out _, out var arguments) ? arguments : string.Empty;

    /// <summary>
    /// Splits a <c>:name arguments</c> line into its lower-cased name and the rest.
    /// </summary>
    /// <returns>
    /// False when the input is not a command line at all, which is the case for blank
    /// input, for a bare <c>:</c>, and for anything that does not start with a colon.
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
