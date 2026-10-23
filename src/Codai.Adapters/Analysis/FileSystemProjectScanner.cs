using Codai.Analysis.Abstractions;
using Codai.Core.Abstractions;
using Codai.Core.Projects;

namespace Codai.Analysis;

/// <summary>
/// Walks a project directory producing the file set sent to the model.
/// </summary>
/// <remarks>
/// File discovery is delegated to <see cref="IFileSystem"/> so it can be tested
/// without a real disk. Files over the configured maximum size are skipped, and
/// built-in plus <c>.codai-gitignore</c> rules are applied.
/// </remarks>
public sealed class FileSystemProjectScanner : IProjectScanner
{
    /// <summary>The size limit applied when the caller does not supply one.</summary>
    public const long DefaultMaxFileSizeBytes = 100 * 1024;

    private readonly IFileSystem _fileSystem;
    private readonly ISignatureExtractorRegistry _extractors;
    private readonly long _maxFileSizeBytes;

    public FileSystemProjectScanner(
        IFileSystem fileSystem,
        ISignatureExtractorRegistry extractors,
        long maxFileSizeBytes = DefaultMaxFileSizeBytes)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(extractors);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFileSizeBytes);

        _fileSystem = fileSystem;
        _extractors = extractors;
        _maxFileSizeBytes = maxFileSizeBytes;
    }

    public async Task<ProjectContext> ScanAsync(
        string rootDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);

        if (!_fileSystem.DirectoryExists(rootDirectory))
        {
            throw new DirectoryNotFoundException(
                $"Directory '{rootDirectory}' does not exist.");
        }

        var patterns = await ReadIgnorePatternsAsync(rootDirectory, cancellationToken)
            .ConfigureAwait(false);

        var files = new List<FileData>();
        var rawCodes = new List<string>();

        await WalkAsync(rootDirectory, rootDirectory, patterns, files, rawCodes, cancellationToken)
            .ConfigureAwait(false);

        return new ProjectContext
        {
            Files = files,
            RawCodes = rawCodes,
        };
    }

    private async Task<IReadOnlyList<string>> ReadIgnorePatternsAsync(
        string rootDirectory,
        CancellationToken cancellationToken)
    {
        var ignoreFile = Path.Combine(rootDirectory, IgnoreRules.IgnoreFileName);

        if (!_fileSystem.FileExists(ignoreFile))
        {
            return [];
        }

        try
        {
            var contents = await _fileSystem.ReadAllTextAsync(ignoreFile, cancellationToken)
                .ConfigureAwait(false);
            return IgnoreRules.ParsePatterns(contents);
        }
        catch (IOException)
        {
            return [];
        }
    }

    private async Task WalkAsync(
        string directory,
        string rootDirectory,
        IReadOnlyList<string> patterns,
        List<FileData> files,
        List<string> rawCodes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var filePath in _fileSystem.EnumerateFiles(directory).Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relativePath = ToRelativePath(rootDirectory, filePath);

            if (ShouldSkip(relativePath, patterns))
            {
                continue;
            }

            long size;
            try
            {
                size = _fileSystem.GetFileSize(filePath);
            }
            catch (IOException)
            {
                continue;
            }

            if (size > _maxFileSizeBytes)
            {
                continue;
            }

            string content;
            try
            {
                content = await _fileSystem.ReadAllTextAsync(filePath, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (IOException)
            {
                continue;
            }

            var signature = string.Join("\n", _extractors.Extract(relativePath, content));

            files.Add(new FileData(relativePath, content, signature));
            rawCodes.Add($"**File: {relativePath}**\n\n{signature}");
        }

        foreach (var subDirectory in _fileSystem.EnumerateDirectories(directory).Order(StringComparer.Ordinal))
        {
            var relativePath = ToRelativePath(rootDirectory, subDirectory);

            if (ShouldSkip(relativePath + "/", patterns))
            {
                continue;
            }

            await WalkAsync(subDirectory, rootDirectory, patterns, files, rawCodes, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static bool ShouldSkip(string relativePath, IReadOnlyList<string> patterns) =>
        IgnoreRules.IsDefaultIgnored(relativePath) ||
        IgnoreRules.IsIgnoredByPatterns(relativePath, patterns);

    private static string ToRelativePath(string rootDirectory, string path) =>
        Path.GetRelativePath(rootDirectory, path).Replace('\\', '/');
}
