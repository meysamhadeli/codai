using Codai.Core.Agents;
using Codai.Core.Chat;

namespace Codai.Application;

/// <summary>
/// Everything the interactive session needs to know before it starts.
/// </summary>
public sealed class SessionOptions
{
    /// <summary>
    /// Directory whose files are summarised, sent to the model, and edited when a
    /// change is accepted. Every relative path in a model response is resolved against
    /// it, so it is also the boundary that keeps edits inside the project.
    /// </summary>
    public required string ProjectRoot { get; init; }

    /// <summary>Active provider identifier, e.g. <c>deepseek</c>.</summary>
    public required string Provider { get; init; }

    /// <summary>Active model identifier, e.g. <c>deepseek-flash</c>.</summary>
    public required string Model { get; init; }

    /// <summary>Terminal colour theme applied to selections and accents.</summary>
    public string Theme { get; init; } = "graphite";

    /// <summary>
    /// When true, a response that asks for more project files triggers a second
    /// request that includes those files.
    /// </summary>
    public bool EnableRequestedContext { get; init; } = true;

    /// <summary>
    /// When true, the stored conversation is loaded before the loop starts, so
    /// <c>codai code --continue</c> picks up where the previous run stopped.
    /// </summary>
    public bool ResumePreviousSession { get; init; }

    /// <summary>Optional task ID whose conversation should be resumed.</summary>
    public string? ResumeTaskId { get; init; }

    /// <summary>
    /// The mode the session starts in. It is the <em>starting</em> mode: the user can
    /// change it at any point with <c>:mode</c>, which is why the running value lives in
    /// <see cref="AgentModeState"/> rather than here.
    /// </summary>
    public AgentMode Mode { get; init; } = AgentModeParser.Default;

    /// <summary>
    /// When true, a conversation that has grown close to the model's context window is
    /// summarised before the next request instead of being sent in full. The manual
    /// <c>:compact</c> command works either way.
    /// </summary>
    public bool AutoCompact { get; init; } = true;

    /// <summary>
    /// How automatic compaction is allowed to shorten a conversation. The manual
    /// <c>:compact</c> command uses the same strategy, and a request the provider refused
    /// for being too long falls back to <see cref="CompactionStrategy.Basic"/> when
    /// automatic compaction is enabled,
    /// because recovery must not depend on another model call succeeding.
    /// </summary>
    public CompactionStrategy CompactionStrategy { get; init; } = CompactionStrategy.Agentic;

    /// <summary>
    /// Tokens of the newest turns a compaction keeps word for word.
    /// </summary>
    /// <remarks>
    /// The tail is what the next request is built on, so it is measured in tokens rather
    /// than counted in turns: a turn that pasted a file is worth keeping on its own, and
    /// twenty short exchanges are worth keeping together. <see cref="HistoryCompactor.KeptTurns"/>
    /// is the floor — however small the turns are, the last few are never summarised.
    /// </remarks>
    public int CompactionPreserveRecentTokens { get; init; } = HistoryCompactor.PreserveRecentTokens;

    /// <summary>
    /// Fraction of the turns being replaced that a compaction without a model is asked to
    /// shrink them to. Half is deliberate: compacting to the edge of the window only
    /// schedules the next compaction.
    /// </summary>
    public double CompactionTargetRatio { get; init; } = HistoryCompactor.TargetRatio;

    /// <summary>Number of newest workspace checkpoints to retain.</summary>
    public int CheckpointRetention { get; init; } = 10;

    /// <summary>When false, new checkpoint snapshots are not captured.</summary>
    public bool CheckpointsEnabled { get; init; } = true;
}
