using Codai.Core.Abstractions;
using Codai.Core.Customization;
using Codai.Core.IO;

namespace Codai.Core.Prompting;

/// <summary>
/// Reads and combines applicable repository instruction documents from disk.
/// </summary>
/// <remarks>
/// The candidate list is the union of the conventions the agent CLIs in the wild use,
/// so an existing repository does not have to be restructured to be understood. The
/// documents are combined from broad repository scope to relevant nested directories.
/// </remarks>
public sealed class ProjectInstructionsProvider : IProjectInstructionsProvider
{
    /// <summary>
    /// Files combined in this stable order within each directory scope. Relative paths use forward slashes.
    /// </summary>
    public static readonly IReadOnlyList<string> CandidateFileNames =
    [
        "AGENTS.md",
        "CLAUDE.md",
        "instructions.md",
        ".claude/CLAUDE.md",
        ".codai/instructions.md",
        ".github/copilot-instructions.md",
    ];

    /// <summary>
    /// Instructions longer than this are truncated. An instructions file is meant to
    /// steer the model, not to become the context window.
    /// </summary>
    public const int MaxCharacters = 32 * 1024;

    /// <summary>Maximum combined instruction payload for one request.</summary>
    public const int MaxTotalCharacters = 128 * 1024;

    /// <summary>Maximum number of applicable instruction documents.</summary>
    public const int MaxDocuments = 64;

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
    public Task<ProjectInstructions?> LoadAsync(
        string rootDirectory,
        CancellationToken cancellationToken = default) => LoadForFilesAsync(rootDirectory, [], cancellationToken);

    /// <inheritdoc />
    public async Task<ProjectInstructions?> LoadForFilesAsync(string rootDirectory, IReadOnlyList<string> files,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentNullException.ThrowIfNull(files);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootDirectory));
        var ancestors = DiscoveryDirectories.Ancestors(root, _fileSystem);
        var repository = ancestors.LastOrDefault(directory => _fileSystem.FileExists(Path.Combine(directory, ".git")) ||
            _fileSystem.DirectoryExists(Path.Combine(directory, ".git"))) ?? root;
        var boundary = _fileSystem.GetCanonicalPath(repository);
        var rootIdentity = _fileSystem.GetCanonicalPath(root);
        var directories = ancestors.TakeWhile(directory => IsInside(repository, directory)).Reverse().ToList();
        if (directories.Count == 0)
        {
            directories.Add(root);
        }
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = ProjectPaths.ResolveInside(root, file);
            if (target is null)
            {
                continue;
            }
            if (!IsInside(rootIdentity, _fileSystem.GetCanonicalPath(target)))
            {
                throw new IOException("Referenced instruction scope resolved outside the workspace.");
            }
            var directory = _fileSystem.DirectoryExists(target) ? target : Path.GetDirectoryName(target);
            while (directory is not null && IsInside(root, directory))
            {
                if (!directories.Contains(directory, PathComparer))
                {
                    directories.Add(directory);
                    if (directories.Count > 128)
                    {
                        throw new IOException("Instruction discovery exceeded its directory limit. Narrow the request.");
                    }
                }
                directory = Path.GetDirectoryName(directory);
            }
        }
        var documents = new List<ProjectInstructions>();
        var seen = new HashSet<string>(PathComparer);
        foreach (var directory in directories.OrderBy(path => path.Count(character => character is '/' or '\\')).ThenBy(path => path, PathComparer))
        {
            foreach (var candidate in CandidateFileNames)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fullPath = Path.Combine(directory, candidate);

                if (!_fileSystem.FileExists(fullPath))
                {
                    continue;
                }
                var identity = _fileSystem.GetCanonicalPath(fullPath);
                if (!IsInside(boundary, identity))
                {
                    throw new IOException("An instruction file resolved outside the repository boundary.");
                }
                if (!seen.Add(identity))
                {
                    continue;
                }
                if (_fileSystem.GetFileSize(fullPath) > 1024 * 1024)
                {
                    throw new IOException("An instruction file exceeded its 1 MiB size limit.");
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

                documents.Add(new ProjectInstructions(Path.GetRelativePath(root, fullPath).Replace('\\', '/'), Truncate(contents.Trim()))
                {
                    ScopePath = Path.GetRelativePath(root, directory).Replace('\\', '/'),
                });
                if (documents.Count > MaxDocuments)
                {
                    throw new IOException("Instruction discovery exceeded its document limit. Narrow the request.");
                }
            }
        }

        if (documents.Count == 0)
        {
            return null;
        }
        var combined = documents.Count == 1 && documents[0].ScopePath == "." ? documents[0].Content :
            "Instructions apply only within their named directory scopes. For conflicting workspace guidance, prefer the more specific directory.\n\n" +
            string.Join("\n\n", documents.Select(document => $"## {document.SourcePath} (scope: {document.ScopePath})\n\n{document.Content}"));
        if (combined.Length > MaxTotalCharacters)
        {
            throw new IOException("Combined instructions exceeded their context limit. Shorten guidance or narrow the request.");
        }
        return new ProjectInstructions(documents[0].SourcePath, combined) { Documents = documents.AsReadOnly() };
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static bool IsInside(string root, string path)
    {
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        return PathComparer.Equals(root, path) || path.StartsWith(prefix,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static string Truncate(string contents) =>
        contents.Length <= MaxCharacters
            ? contents
            : contents[..MaxCharacters] + TruncationNotice;
}
