using Codai.Core.Abstractions;
using Codai.Core.Models;

namespace Codai.Core.Diffs;

/// <summary>
/// Applies a <see cref="CodeChange"/> to disk.
/// </summary>
/// <remarks>
/// Unlike the original Go implementation this applier never rewrites interior
/// characters: a diff marker is only recognised as the first non-whitespace
/// character of a line, and only a single marker character is removed. Lines such
/// as <c>i++</c> are therefore preserved. Text files are written verbatim.
/// </remarks>
public sealed class DiffApplier : IDiffApplier
{
    private readonly IFileSystem _fileSystem;

    public DiffApplier(IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        _fileSystem = fileSystem;
    }

    public async Task<DiffApplyResult> ApplyAsync(
        string rootDirectory,
        CodeChange change,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentNullException.ThrowIfNull(change);

        var fullPath = Path.GetFullPath(Path.Combine(rootDirectory, change.RelativePath));
        var directory = Path.GetDirectoryName(fullPath);

        var content = change.IsTextFile
            ? change.Code
            : StripDiffMarkers(change.Code);

        if (string.IsNullOrWhiteSpace(content))
        {
            return DeleteIfPresent(fullPath, directory, rootDirectory);
        }

        if (!string.IsNullOrEmpty(directory))
        {
            _fileSystem.CreateDirectory(directory);
        }

        await _fileSystem.WriteAllTextAsync(fullPath, content, cancellationToken).ConfigureAwait(false);
        return new DiffApplyResult(fullPath, Written: true, Deleted: false);
    }

    /// <summary>
    /// Removes <c>-</c> lines and strips a single leading <c>+</c> from added lines.
    /// </summary>
    internal static string StripDiffMarkers(string code)
    {
        var lines = code.Split('\n');
        var result = new List<string>(lines.Length);

        foreach (var line in lines)
        {
            var markerIndex = FirstNonWhitespaceIndex(line);

            if (markerIndex >= 0 && IsRemoveMarker(line, markerIndex))
            {
                continue;
            }

            if (markerIndex >= 0 && IsAddMarker(line, markerIndex))
            {
                result.Add(line.Remove(markerIndex, 1));
                continue;
            }

            result.Add(line);
        }

        return string.Join("\n", result);
    }

    private static int FirstNonWhitespaceIndex(string line)
    {
        for (var i = 0; i < line.Length; i++)
        {
            if (!char.IsWhiteSpace(line[i]))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool IsAddMarker(string line, int index) =>
        line[index] == '+' &&
        (index + 1 >= line.Length || line[index + 1] != '+');

    private static bool IsRemoveMarker(string line, int index) =>
        line[index] == '-' &&
        (index + 1 >= line.Length || line[index + 1] != '-');

    private DiffApplyResult DeleteIfPresent(string fullPath, string? directory, string rootDirectory)
    {
        if (!_fileSystem.FileExists(fullPath))
        {
            return new DiffApplyResult(fullPath, Written: false, Deleted: false);
        }

        _fileSystem.DeleteFile(fullPath);
        RemoveDirectoryIfEmpty(directory, rootDirectory);
        return new DiffApplyResult(fullPath, Written: false, Deleted: true);
    }

    private void RemoveDirectoryIfEmpty(string? directory, string rootDirectory)
    {
        if (string.IsNullOrEmpty(directory))
        {
            return;
        }

        // Never delete the scan root itself.
        if (PathsEqual(directory, rootDirectory))
        {
            return;
        }

        if (!_fileSystem.DirectoryExists(directory))
        {
            return;
        }

        if (_fileSystem.EnumerateFiles(directory).Any() || _fileSystem.EnumerateDirectories(directory).Any())
        {
            return;
        }

        _fileSystem.DeleteDirectory(directory, recursive: false);
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            StringComparison.OrdinalIgnoreCase);
}
