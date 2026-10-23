using System.Text;
using Codai.Core.Abstractions;
using Codai.Core.Context;

namespace Codai.Core.Chat;

/// <summary>
/// Puts back the files a compaction had to leave out of its summary.
/// </summary>
/// <remarks>
/// <para>
/// A summary can say "the parser was rewritten to stream tokens", and that is enough to carry
/// a decision. It cannot carry the file, so the first thing the model does afterwards is read
/// it again — which costs the tokens the compaction just saved, in the turn where the user
/// was waiting for an answer. Restoring the most recently used files before the conversation
/// resumes is what makes the saving real instead of deferred by one turn.
/// </para>
/// <para>
/// The restore is deliberately bounded three ways: a few files, a cap per file, and a total
/// budget. A restore that filled the window would schedule the next compaction immediately,
/// so the point is a small, high-value set rather than completeness. What does not fit stays
/// in the archive the summary names.
/// </para>
/// </remarks>
public sealed class ContextRestorer
{
    /// <summary>Files restored after a compaction at most.</summary>
    public const int MaxFiles = 5;

    /// <summary>
    /// Tokens restored per file. Enough for a source file that matters, small enough that
    /// five of them do not undo the compaction on their own.
    /// </summary>
    public const int MaxTokensPerFile = 5_000;

    /// <summary>Tokens the restored files may cost in total.</summary>
    public const int FileBudgetTokens = 50_000;

    /// <summary>Heading the restored block starts with.</summary>
    public const string Heading = "## Context restored after compaction";

    private const string Preamble =
        "These files were read or edited before the conversation was compacted. They are " +
        "restored below in full because a summary cannot carry them. Work from this text " +
        "rather than reading the same files again.\n";

    private readonly IFileSystem _fileSystem;

    /// <summary>Creates a restorer.</summary>
    /// <param name="fileSystem">Used to read the files back.</param>
    public ContextRestorer(IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        _fileSystem = fileSystem;
    }

    /// <summary>
    /// Renders the files worth restoring, or null when none of them can be read.
    /// </summary>
    /// <param name="projectRoot">Root the relative paths are resolved against.</param>
    /// <param name="files">Candidate paths, most recently touched first.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    public async Task<string?> BuildAsync(
        string projectRoot,
        IReadOnlyList<string> files,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(files);

        if (files.Count == 0)
        {
            return null;
        }

        var builder = new StringBuilder();
        var budget = FileBudgetTokens;
        var restored = 0;

        foreach (var path in files)
        {
            if (restored >= MaxFiles || budget <= 0)
            {
                break;
            }

            var fullPath = Resolve(projectRoot, path);

            if (fullPath is null || !_fileSystem.FileExists(fullPath))
            {
                continue;
            }

            string content;

            try
            {
                content = await _fileSystem
                    .ReadAllTextAsync(fullPath, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A file that cannot be read is not a reason to fail a compaction: the
                // summary already stands, and the archive still has the original turns.
                continue;
            }

            if (string.IsNullOrEmpty(content) || content.Contains('\0'))
            {
                continue;
            }

            var allowance = Math.Min(MaxTokensPerFile, budget);
            var characters = (int)Math.Min(content.Length, allowance * TokenEstimator.CharactersPerToken);
            var truncated = characters < content.Length;

            builder
                .Append("\n### ")
                .Append(path.Replace('\\', '/'))
                .Append("\n\n```\n")
                .Append(content.AsSpan(0, characters))
                .Append("\n```\n");

            if (truncated)
            {
                builder.Append("\n(truncated)\n");
            }

            budget -= TokenEstimator.FromCharacters(characters);
            restored++;
        }

        if (restored == 0)
        {
            return null;
        }

        return new StringBuilder(Heading.Length + Preamble.Length + builder.Length)
            .Append(Heading)
            .Append("\n\n")
            .Append(Preamble)
            .Append(builder)
            .ToString();
    }

    /// <summary>
    /// Turns a path the tools were given into a full path inside the project, or null when
    /// it would point outside it.
    /// </summary>
    private static string? Resolve(string projectRoot, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var candidate = Path.IsPathRooted(path) ? path : Path.Combine(projectRoot, path);
            var fullPath = Path.GetFullPath(candidate);
            var root = Path.GetFullPath(projectRoot);

            return fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                ? fullPath
                : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}
