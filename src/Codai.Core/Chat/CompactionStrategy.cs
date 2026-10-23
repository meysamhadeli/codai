namespace Codai.Core.Chat;

/// <summary>
/// How a compaction is allowed to shorten a conversation.
/// </summary>
/// <remarks>
/// <para>
/// The two are not two ways of doing the same thing: they trade quality against certainty.
/// </para>
/// <list type="bullet">
/// <item>
/// <description>
/// <see cref="Agentic"/> asks a model to write a summary. It keeps the intent of the
/// dropped turns — decisions, errors and fixes — in prose the next request can use, which
/// is why it is the default.
/// </description>
/// </item>
/// <item>
/// <description>
/// <see cref="Basic"/> shortens without a model call: the oldest turns are dropped and the
/// newest kept. It reasons about nothing, so it costs nothing and cannot fail, which is why
/// it is the strategy a request that overflowed falls back to. A summarisation is itself a
/// model call, and recovery must not depend on one succeeding.
/// </description>
/// </item>
/// </list>
/// </remarks>
public enum CompactionStrategy
{
    /// <summary>Summarise the dropped turns with a model.</summary>
    Agentic,

    /// <summary>Drop the oldest turns and keep the newest, without a model call.</summary>
    Basic,
}
