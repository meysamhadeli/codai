using Codai.Analysis;
using Codai.Core.Abstractions;

namespace Codai.Tools;

/// <summary>
/// The ignore-filtered file and directory listing of a project.
/// </summary>
/// <remarks>
/// Built once per tool invocation. The ignore rules are the same ones the project
/// scanner uses, so a tool can never see a file the summary excluded — which is what
/// keeps <c>bin/</c>, <c>obj/</c> and <c>node_modules/</c> from being searched or
/// reported.
/// </remarks>
public sealed class ProjectFileIndex
{
    /// <summary>
    /// Files above this size are left out of the index. Larger than the scanner's limit
    /// because a tool searching for a symbol should not silently skip a big source file,
    /// while still excluding build artefacts and data blobs.
    /// </summary>
    public const long DefaultMaxFileSizeBytes = 1024 * 1024;

    /// <summary>
    /// An index with no entries, for callers that only need the path-ignore rules and
    /// do not want to walk the project.
    /// </summary>
    public static ProjectFileIndex Empty(string root) =>
        new(root, [], [], []);

    private readonly IReadOnlyList<string> _userPatterns;

    private ProjectFileIndex(        string root,
        IReadOnlyList<string> files,
        IReadOnlyList<string> directories,
        IReadOnlyList<string> userPatterns)
    {
        Root = root;
        Files = files;
        Directories = directories;
        _userPatterns = userPatterns;
    }

    /// <summary>Absolute path the index was built from.</summary>
    public string Root { get; }

    /// <summary>Relative file paths, using forward slashes, sorted.</summary>
    public IReadOnlyList<string> Files { get; }

    /// <summary>Relative directory paths ending in a slash, sorted.</summary>
    public IReadOnlyList<string> Directories { get; }

    /// <summary>Builds the index by walking <paramref name="root"/>.</summary>
    public static async Task<ProjectFileIndex> BuildAsync(
        string root,
        IFileSystem fileSystem,
        long maxFileSizeBytes = DefaultMaxFileSizeBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(fileSystem);

        var patterns = await ReadIgnorePatternsAsync(root, fileSystem, cancellationToken)
            .ConfigureAwait(false);

        var files = new List<string>();
        var directories = new List<string>();

        Walk(root, root, patterns, fileSystem, maxFileSizeBytes, files, directories);

        files.Sort(StringComparer.Ordinal);
        directories.Sort(StringComparer.Ordinal);

        return new ProjectFileIndex(root, files, directories, patterns);
    }

    /// <summary>True when a relative path is excluded by the built-in or project ignore rules.</summary>
    public bool IsIgnored(string relativePath) =>
        IgnoreRules.IsDefaultIgnored(relativePath) ||
        IgnoreRules.IsIgnoredByPatterns(relativePath, _userPatterns);

    /// <summary>
    /// Files under <paramref name="subPath"/>, which must be empty or a relative
    /// directory inside the project.
    /// </summary>
    public IEnumerable<string> FilesUnder(string subPath)
    {
        if (string.IsNullOrWhiteSpace(subPath) || subPath is "." or "./")
        {
            return Files;
        }

        var prefix = Normalize(subPath) + "/";

        return Files.Where(file => file.StartsWith(prefix, StringComparison.Ordinal));
    }

    /// <summary>
    /// Normalises a model-supplied relative path for comparison: trims, converts
    /// backslashes, drops leading <c>./</c> and surrounding slashes.
    /// </summary>
    public static string Normalize(string path)
    {
        var normalized = path.Trim().Replace('\\', '/').Trim('/');

        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..].Trim('/');
        }

        return normalized;
    }

    private static async Task<IReadOnlyList<string>> ReadIgnorePatternsAsync(
        string root,
        IFileSystem fileSystem,
        CancellationToken cancellationToken)
    {
        var ignoreFile = Path.Combine(root, IgnoreRules.IgnoreFileName);

        if (!fileSystem.FileExists(ignoreFile))
        {
            return [];
        }

        try
        {
            var contents = await fileSystem.ReadAllTextAsync(ignoreFile, cancellationToken)
                .ConfigureAwait(false);

            return IgnoreRules.ParsePatterns(contents);
        }
        catch (IOException)
        {
            return [];
        }
    }

    private static void Walk(
        string directory,
        string root,
        IReadOnlyList<string> patterns,
        IFileSystem fileSystem,
        long maxFileSizeBytes,
        List<string> files,
        List<string> directories)
    {
        IEnumerable<string> filePaths;
        IEnumerable<string> directoryPaths;

        try
        {
            filePaths = fileSystem.EnumerateFiles(directory);
            directoryPaths = fileSystem.EnumerateDirectories(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (var filePath in filePaths)
        {
            var relative = Relative(root, filePath);

            if (IgnoreRules.IsDefaultIgnored(relative) ||
                IgnoreRules.IsIgnoredByPatterns(relative, patterns))
            {
                continue;
            }

            try
            {
                if (fileSystem.GetFileSize(filePath) > maxFileSizeBytes)
                {
                    continue;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            files.Add(relative);
        }

        foreach (var directoryPath in directoryPaths)
        {
            var relative = Relative(root, directoryPath);

            if (IgnoreRules.IsDefaultIgnored(relative + "/") ||
                IgnoreRules.IsIgnoredByPatterns(relative + "/", patterns))
            {
                continue;
            }

            directories.Add(relative + "/");
            Walk(directoryPath, root, patterns, fileSystem, maxFileSizeBytes, files, directories);
        }
    }

    private static string Relative(string root, string path) =>
        Path.GetRelativePath(root, path).Replace('\\', '/');
}
