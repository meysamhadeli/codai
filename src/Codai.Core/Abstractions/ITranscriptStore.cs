using Codai.Core.Chat;

namespace Codai.Core.Abstractions;

/// <summary>
/// Keeps the conversation as it was before it was compacted, so a summary can point at
/// the full record instead of claiming to replace it.
/// </summary>
/// <remarks>
/// A summary is lossy by construction: it is written before anyone knows which detail
/// the next request will need. Archiving the turns it replaced turns that loss into
/// latency — the model reads the archive when it does need something the summary dropped,
/// and pays nothing when it does not.
/// </remarks>
public interface ITranscriptStore
{
    /// <summary>
    /// Writes <paramref name="turns"/> to the project's archive.
    /// </summary>
    /// <param name="rootDirectory">Project root the archive is written under.</param>
    /// <param name="turns">Turns to archive, in the order they happened.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>
    /// Path of the archive relative to <paramref name="rootDirectory"/>, using forward
    /// slashes, or null when it could not be written. A failure here must never stop a
    /// compaction: the summary on its own is still better than the turns it replaces.
    /// </returns>
    Task<string?> ArchiveAsync(
        string rootDirectory,
        IReadOnlyList<ChatTurn> turns,
        CancellationToken cancellationToken = default);
}
