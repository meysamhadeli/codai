using Codai.Core.Abstractions;
using Codai.Core.IO;

namespace Codai.Core.Prompting;

/// <summary>
/// Reads the project's instruction file from disk.
/// </summary>
/// <remarks>
/// The candidate list is the union of the conventions the agent CLIs in the wild use,
/// so an existing repository does not have to be restructured to be understood. The
/// first match wins: a project that ships several of them has deliberately ordered
/// them by naming one before the other.
/// </remarks>
public sealed class ProjectInstructionsProvider : IProjectInstructionsProvider
{
    /// <summary>
    /// Files searched for, in priority order. Relative paths use forward slashes.
    /// </summary>
    public static readonly IReadOnlyList<string> CandidateFileNames =
    [
        "AGENTS.md",
        "CLAUDE.md",
        "CODAI.md",
        ".codai/instructions.md",
        ".github/copilot-instructions.md",
    ];

    /// <summary>
    /// Instructions longer than this are truncated. An instructions file is meant to
    /// steer the model, not to become the context window.
    /// </summary>
    public const int MaxCharacters = 32 * 1024;

    private const string TruncationNotice =
        "\n\n[... instructions truncated, keep this file shorter ...]";

    private readonly IFileSystem _fileSystem;

    /// <summary>Creates the provider.</summary>
    public ProjectInstructionsProvider(IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        _fileSystem = fileSystem;
    }

    /// <inheritdoc />
    public async Task<ProjectInstructions?> LoadAsync(
        string rootDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);

        foreach (var candidate in CandidateFileNames)
        {
            var fullPath = ProjectPaths.ResolveInside(rootDirectory, candidate);

            if (fullPath is null || !_fileSystem.FileExists(fullPath))
            {
                continue;
            }

            string contents;

            try
            {
                contents = await _fileSystem
                    .ReadAllTextAsync(fullPath, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (IOException)
            {
                // An unreadable file is skipped rather than failing the session.
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(contents))
            {
                continue;
            }

            return new ProjectInstructions(candidate, Truncate(contents.Trim()));
        }

        return null;
    }

    private static string Truncate(string contents) =>
        contents.Length <= MaxCharacters
            ? contents
            : contents[..MaxCharacters] + TruncationNotice;
}
