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
        ":clear-history  Clear history of chat from session";

    /// <summary>Maps a raw input line onto a <see cref="SlashCommand"/>.</summary>
    public static SlashCommand Parse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return SlashCommand.None;
        }

        var trimmed = input.Trim();

        if (trimmed[0] != ':')
        {
            return SlashCommand.None;
        }

        return trimmed.ToLowerInvariant() switch
        {
            ":help" => SlashCommand.Help,
            ":clear" => SlashCommand.Clear,
            ":exit" or ":quit" => SlashCommand.Exit,
            ":token" => SlashCommand.Token,
            ":clear-token" => SlashCommand.ClearToken,
            ":clear-history" => SlashCommand.ClearHistory,
            _ => SlashCommand.None,
        };
    }
}
