namespace Codai.Core.Agents;

/// <summary>
/// The <see cref="AgentMode"/> the session is currently running in.
/// </summary>
/// <remarks>
/// <para>
/// The mode can change in the middle of a session — <c>:mode agent</c> — so it cannot be
/// a constructor argument of the services that need it, and it must not be copied into
/// them when they are built, or the copy goes stale. This holder is the single place that
/// knows the answer, so a consumer that reads it per request always agrees with what the
/// user last asked for.
/// </para>
/// <para>
/// It is deliberately mutable and deliberately this small. It is not configuration: it
/// is the current state of a running session.
/// </para>
/// </remarks>
public sealed class AgentModeState
{
    private AgentMode _mode;

    /// <summary>Creates the state, starting in <paramref name="mode"/>.</summary>
    public AgentModeState(AgentMode mode = AgentModeParser.Default) => _mode = mode;

    /// <summary>
    /// The active mode. Setting it takes effect on the next request; a request already
    /// in flight keeps the mode it started with.
    /// </summary>
    public AgentMode Mode
    {
        get => _mode;
        set => _mode = value;
    }
}
