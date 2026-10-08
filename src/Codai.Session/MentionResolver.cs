using System.Text;
using Codai.Core.Abstractions;
using Codai.Core.IO;
using Codai.Core.Prompting;
using Codai.Tools;

namespace Codai.Session;

/// <summary>
/// Resolves the <c>@path</c> mentions of a request into file contents.
/// </summary>
/// <remarks>
/// Resolution goes through <see cref="ProjectFileIndex"/>, so a mention can never
/// attach a file the project scan would have skipped: build output, ignored folders and
/// oversized blobs stay out, and a path that escapes the project root is refused by
/// <see cref="ProjectPaths"/> like every other model- or user-supplied path.
/// </remarks>
public sealed class MentionResolver : IMentionResolver
{
    /// <summary>Most files one request may attach, whatever the user typed.</summary>
    public const int MaxFiles = 20;

    /// <summary>Total characters of attached file content allowed in one request.</summary>
    public const int MaxTotalCharacters = 256 * 1024;

    private const string FileSeparator = "\n---------\n\n";

    private const string TruncationNotice = "\n\n[... truncated, reference fewer files ...]";

    private static readonly char[] PatternCharacters = ['*', '?', '[', '{'];

    private readonly IFileSystem _fileSystem;

    /// <summary>Creates the resolver.</summary>
    public MentionResolver(IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        _fileSystem = fileSystem;
    }

    /// <inheritdoc />
    public async Task<MentionResolution> ResolveAsync(
        string rootDirectory,
        string userInput,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);

        var mentions = MentionParser.Parse(userInput);

        if (mentions.Count == 0)
        {
            return MentionResolution.Empty;
        }

        var index = await ProjectFileIndex
            .BuildAsync(rootDirectory, _fileSystem, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var (selected, unresolved) = Select(index, mentions);

        if (selected.Count == 0)
        {
            return new MentionResolution(null, [], unresolved);
        }

        return await ReadAsync(rootDirectory, selected, unresolved, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Expands the mentions into relative paths. A mention that matches nothing is
    /// reported rather than dropped, so a typo is visible instead of silently costing
    /// the user the context they asked for.
    /// </summary>
    private static (List<string> Selected, List<string> Unresolved) Select(
        ProjectFileIndex index,
        IReadOnlyList<string> mentions)
    {
        var selected = new List<string>();
        var unresolved = new List<string>();

        foreach (var mention in mentions)
        {
            var normalized = ProjectFileIndex.Normalize(mention);

            if (normalized.Length == 0)
            {
                continue;
            }

            var before = selected.Count;
            var room = MaxFiles - selected.Count;

            if (normalized.IndexOfAny(PatternCharacters) >= 0)
            {
                selected.AddRange(index.Files
                    .Where(file => GlobMatcher.IsMatchPathOrFileName(file, normalized))
                    .Take(room));
            }
            else if (index.Files.FirstOrDefault(file => GlobMatcher.PathsEqual(file, normalized)) is { } file)
            {
                selected.Add(file);
            }
            else if (index.Directories.Any(directory => GlobMatcher.PathsEqual(directory, normalized + "/")))
            {
                selected.AddRange(index.FilesUnder(normalized).Take(room));
            }

            if (selected.Count == before)
            {
                unresolved.Add(mention);
            }

            if (selected.Count >= MaxFiles)
            {
                break;
            }
        }

        return (selected, unresolved);
    }

    /// <summary>Reads the selected files and renders them as context blocks.</summary>
    private async Task<MentionResolution> ReadAsync(
        string rootDirectory,
        IReadOnlyList<string> selected,
        IReadOnlyList<string> unresolved,
        CancellationToken cancellationToken)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var blocks = new List<string>();
        var files = new List<string>();
        var totalCharacters = 0;

        foreach (var relativePath in selected)
        {
            if (totalCharacters >= MaxTotalCharacters || !seen.Add(relativePath))
            {
                continue;
            }

            var fullPath = ProjectPaths.ResolveInside(rootDirectory, relativePath);

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
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            content = Cap(content, MaxTotalCharacters - totalCharacters);

            blocks.Add(new StringBuilder()
                .Append("**File: ").Append(relativePath).Append("**\n\n")
                .Append(content)
                .ToString());

            files.Add(relativePath);
            totalCharacters += content.Length;
        }

        return new MentionResolution(
            blocks.Count == 0 ? null : string.Join(FileSeparator, blocks),
            files,
            unresolved);
    }

    private static string Cap(string content, int remaining) =>
        content.Length <= remaining ? content : content[..remaining] + TruncationNotice;
}
