namespace Codai.Core.Agents;

/// <summary>
/// Reads an <see cref="AgentMode"/> from the spelling used on the command line and by
/// the <c>:mode</c> session command.
/// </summary>
public static class AgentModeParser
{
    /// <summary>
    /// The mode a session starts in when nothing else is asked for.
    /// </summary>
    /// <remarks>
    /// This is <c>agent</c>, like every other coding agent CLI: the point of the tool is
    /// to make the change, and being asked before each one is what makes that safe. A
    /// read-only session is the deliberate choice, so it is the one that has to be asked
    /// for with <c>--mode plan</c> or <c>:mode plan</c>.
    /// </remarks>
    public const AgentMode Default = AgentMode.Agent;

    /// <summary>Accepted spellings, in the order they are shown to the user.</summary>
    public static string Names => "plan | agent";

    /// <summary>Parses <paramref name="value"/>, ignoring case and surrounding space.</summary>
    public static bool TryParse(string? value, out AgentMode mode)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "plan":
                mode = AgentMode.Plan;
                return true;
            case "agent":
            case "edit":
                // "edit" is accepted because that is what other agent CLIs call the
                // mode that is allowed to change files.
                mode = AgentMode.Agent;
                return true;
            default:
                mode = Default;
                return false;
        }
    }

    /// <summary>Parses <paramref name="value"/>, falling back to <see cref="Default"/>.</summary>
    public static AgentMode ParseOrDefault(string? value) =>
        TryParse(value, out var mode) ? mode : Default;

    /// <summary>The spelling shown back to the user.</summary>
    public static string ToName(AgentMode mode) => mode switch
    {
        AgentMode.Plan => "plan",
        AgentMode.Agent => "agent",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown agent mode."),
    };
}
