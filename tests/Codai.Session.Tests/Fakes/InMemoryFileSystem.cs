using Codai.Core.Abstractions;

namespace Codai.Session.Tests.Fakes;

/// <summary>
/// In-memory <see cref="IFileSystem"/> used to exercise the diff applier and the
/// requested-context resolver without touching the disk.
/// </summary>
public sealed class InMemoryFileSystem : IFileSystem
{
    private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);

    public InMemoryFileSystem(params string[] existingDirectories)
    {
        foreach (var directory in existingDirectories)
        {
            _directories.Add(Normalize(directory));
        }
    }

    public IReadOnlyDictionary<string, string> Files => _files;

    public void AddFile(string path, string content)
    {
        var normalized = Normalize(path);
        _files[normalized] = content;

        // Walk the parents. Normalize each one: Path.GetDirectoryName gives back
        // platform separators, and mixing them into the key set would make
        // enumeration miss directories that do exist.
        for (var directory = Path.GetDirectoryName(normalized);
             !string.IsNullOrEmpty(directory);
             directory = Path.GetDirectoryName(directory))
        {
            if (!_directories.Add(Normalize(directory)))
            {
                break;
            }
        }
    }

    public static string Normalize(string path) =>
        Path.GetFullPath(path).Replace('\\', '/').TrimEnd('/');

    public bool FileExists(string path) => _files.ContainsKey(Normalize(path));

    public bool DirectoryExists(string path) => _directories.Contains(Normalize(path));

    public long GetFileSize(string path) =>
        _files.TryGetValue(Normalize(path), out var content) ? content.Length : 0;

    public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!_files.TryGetValue(Normalize(path), out var content))
        {
            throw new FileNotFoundException("File not found.", path);
        }

        return Task.FromResult(content);
    }

    public Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken = default)
    {
        AddFile(path, contents);
        return Task.CompletedTask;
    }

    public Task WriteAllTextAtomicallyAsync(string path, string contents, CancellationToken cancellationToken = default)
    {
        AddFile(path, contents);
        return Task.CompletedTask;
    }

    public void CreateDirectory(string path) => _directories.Add(Normalize(path));

    public void DeleteFile(string path) => _files.Remove(Normalize(path));

    public void DeleteDirectory(string path, bool recursive)
    {
        var normalized = Normalize(path);
        _directories.Remove(normalized);

        foreach (var file in _files.Keys.Where(key => key.StartsWith(normalized + "/", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            _files.Remove(file);
        }
    }

    public IEnumerable<string> EnumerateFiles(string path)
    {
        var normalized = Normalize(path);

        // A real file system hands back paths in the platform's own form, and callers
        // derive relative paths from them by string comparison.
        return _files.Keys
            .Where(key =>
                key.StartsWith(normalized + "/", StringComparison.OrdinalIgnoreCase) &&
                !key[(normalized.Length + 1)..].Contains('/'))
            .Select(Native)
            .ToList();
    }

    public IEnumerable<string> EnumerateDirectories(string path)
    {
        var normalized = Normalize(path);

        return _directories
            .Where(key =>
                key.StartsWith(normalized + "/", StringComparison.OrdinalIgnoreCase) &&
                !key[(normalized.Length + 1)..].Contains('/'))
            .Select(Native)
            .ToList();
    }

    private static string Native(string normalized) =>
        normalized.Replace('/', Path.DirectorySeparatorChar);
}
