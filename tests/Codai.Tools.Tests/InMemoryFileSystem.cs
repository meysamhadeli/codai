using System.Text;
using Codai.Core.Abstractions;

namespace Codai.Tools.Tests;

/// <summary>
/// In-memory <see cref="IFileSystem"/> so the tools can be exercised without a disk.
/// Paths are stored in canonical (<see cref="Path.GetFullPath(string)"/>) form, which
/// means a test can add files with either separator and still have enumeration match.
/// </summary>
internal sealed class InMemoryFileSystem : IFileSystem
{
    private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);

    public InMemoryFileSystem AddDirectory(string path)
    {
        var full = Canonical(path);
        for (var current = full; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            if (!_directories.Add(current))
            {
                break;
            }
        }

        return this;
    }

    public InMemoryFileSystem AddFile(string path, string contents)
    {
        var full = Canonical(path);
        _files[full] = contents;
        return AddDirectory(Path.GetDirectoryName(full)!);
    }

    public bool FileExists(string path) => _files.ContainsKey(Canonical(path));

    public bool DirectoryExists(string path) => _directories.Contains(Canonical(path));

    public long GetFileSize(string path) =>
        _files.TryGetValue(Canonical(path), out var contents)
            ? Encoding.UTF8.GetByteCount(contents)
            : throw new FileNotFoundException(path);

    public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return _files.TryGetValue(Canonical(path), out var contents)
            ? Task.FromResult(contents)
            : throw new FileNotFoundException(path);
    }

    public Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken = default)
    {
        AddFile(path, contents);
        return Task.CompletedTask;
    }

    public void CreateDirectory(string path) => AddDirectory(path);

    public void DeleteFile(string path) => _files.Remove(Canonical(path));

    public void DeleteDirectory(string path, bool recursive)
    {
        var full = Canonical(path);

        foreach (var file in _files.Keys.Where(f => IsUnder(f, full)).ToList())
        {
            _files.Remove(file);
        }

        foreach (var directory in _directories.Where(d => IsUnder(d, full)).ToList())
        {
            _directories.Remove(directory);
        }
    }

    public IEnumerable<string> EnumerateFiles(string path)
    {
        var full = Canonical(path);

        return _files.Keys
            .Where(file => string.Equals(Path.GetDirectoryName(file), full, StringComparison.OrdinalIgnoreCase))
            .OrderBy(file => file, StringComparer.Ordinal)
            .ToList();
    }

    public IEnumerable<string> EnumerateDirectories(string path)
    {
        var full = Canonical(path);

        return _directories
            .Where(directory => string.Equals(Path.GetDirectoryName(directory), full, StringComparison.OrdinalIgnoreCase))
            .OrderBy(directory => directory, StringComparer.Ordinal)
            .ToList();
    }

    private static bool IsUnder(string candidate, string directory) =>
        candidate.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static string Canonical(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
