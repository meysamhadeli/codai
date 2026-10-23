namespace Codai.Application;

/// <summary>
/// Host-facing command metadata supplied by the application runtime.
/// </summary>
public static class CodaiApplicationCommands
{
    /// <summary>Help text for commands accepted during a coding session.</summary>
    public static string HelpText => SlashCommandParser.HelpText;
}
