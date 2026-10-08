using Codai.Core.Chat;

namespace Codai.Core.Abstractions;

/// <summary>
/// Stores the conversation so a later run can continue where the last one stopped.
/// </summary>
/// <remarks>
/// Implementations must treat the store as a convenience, never as a source of truth:
/// a missing, unreadable or corrupt file is reported as "nothing to resume", because
/// losing the transcript must not stop the session from starting.
/// </remarks>
public interface ISessionStore
{
    /// <summary>Reads the stored conversation, or an empty list when there is none.</summary>
    Task<IReadOnlyList<ChatTurn>> LoadAsync(
        string rootDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>Overwrites the stored conversation. An empty list forgets it.</summary>
    Task SaveAsync(
        string rootDirectory,
        IReadOnlyList<ChatTurn> turns,
        CancellationToken cancellationToken = default);
}
