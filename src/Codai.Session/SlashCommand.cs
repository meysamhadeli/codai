namespace Codai.Session;

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
}

/// <summary>
/// Recognises the session slash commands. Names match the original Go CLI for
/// backwards compatibility.
/// </summary>
public static class SlashCommandParser
{
    /// <summary>Help text shown by <c>:help</c>.</summary>
    public const string HelpText =
        ":help  Help for code subcommand\n" +
        ":clear  Clear screen\n" +
        ":exit  Exit from codai\n" +
        ":token  Token information\n" +
        ":clear-token  Clear token from session\n" +
        ":clear-history  Clear history of chat from session\n" +
        ":mode [ask|plan|agent]  Show or change what the assistant is allowed to do\n" +
        ":compact [focus]  Summarize the chat history and keep only the summary";

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

        if (trimmed.Length < 2 || trimmed[0] != ':')
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
        _ => SlashCommand.None,
    };
}
