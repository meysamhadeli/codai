using System.Text;
using Codai.Core.Abstractions;

namespace Codai.Core.Chat;

/// <summary>
/// Writes the conversation to <c>.codai/transcripts/&lt;timestamp&gt;.md</c> before it is
/// compacted.
/// </summary>
/// <remarks>
/// <para>
/// The archive lives inside the project for the same reason the session file does: there
/// is no cache directory to discover, and the built-in ignore rules keep it out of the
/// project summary. Unlike the session file it is deliberately reachable by
/// <c>read_file</c>, because being readable is the whole point — the summary names it, and
/// the model opens it when the summary turns out to be missing something.
/// </para>
/// <para>
/// Writing it is best effort. Compaction still has to succeed on a read-only checkout, a
/// full disk or a hostile file system, so every failure here degrades to a summary with
/// no archive to point at.
/// </para>
/// </remarks>
public sealed class TranscriptStore : ITranscriptStore
{
    /// <summary>Folder, relative to the project root, that holds the archives.</summary>
    public const string RelativeFolder = ".codai/transcripts";

    /// <summary>Extension given to an archive, chosen so it reads as prose.</summary>
    public const string FileExtension = ".md";

    private readonly IFileSystem _fileSystem;

    /// <summary>Creates the store.</summary>
    public TranscriptStore(IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        _fileSystem = fileSystem;
    }

    /// <inheritdoc />
    public async Task<string?> ArchiveAsync(
        string rootDirectory,
        IReadOnlyList<ChatTurn> turns,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentNullException.ThrowIfNull(turns);

        if (turns.Count == 0)
        {
            return null;
        }

        var fileName = $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss-fff}{FileExtension}";

        try
        {
            _fileSystem.CreateDirectory(Path.Combine(rootDirectory, PathOf(RelativeFolder)));
            await _fileSystem
                .WriteAllTextAsync(
                    Path.Combine(rootDirectory, PathOf(RelativeFolder), fileName),
                    Render(turns),
                    cancellationToken)
                .ConfigureAwait(false);

            return $"{RelativeFolder}/{fileName}";
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Renders the conversation as a document. Headings per turn are what make the
    /// archive navigable: a model that jumps to the wrong turn can be told the section it
    /// wanted is a few headings away, which it can be with a second read.
    /// </summary>
    internal static string Render(IReadOnlyList<ChatTurn> turns)
    {
        var builder = new StringBuilder()
            .Append("# Conversation archived before compaction\n\n")
            .Append("This is the full record of the turns that a summary in a later session ")
            .Append("replaced. Read the sections you need; the summary is a shorter account ")
            .Append("of the same conversation and may have dropped a detail.\n");

        for (var i = 0; i < turns.Count; i++)
        {
            builder
                .Append("\n## Turn ").Append(i + 1)
                .Append(" (").Append(turns[i].Timestamp.ToString("u")).Append(")\n\n")
                .Append("### User\n\n").Append(turns[i].UserRequest).Append("\n\n")
                .Append("### Assistant\n\n").Append(turns[i].AiResponse).Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>Absolute folder the archives are written to.</summary>
    public static string GetFolder(string rootDirectory) =>
        Path.Combine(rootDirectory, PathOf(RelativeFolder));

    private static string PathOf(string relativeFolder) =>
        relativeFolder.Replace('/', Path.DirectorySeparatorChar);
}
