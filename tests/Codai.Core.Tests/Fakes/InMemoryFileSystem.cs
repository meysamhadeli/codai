using Codai.Core.Abstractions;

namespace Codai.Core.Tests.Fakes;

/// <summary>
/// In-memory <see cref="IFileSystem"/> for the core services that read or write files:
/// the instructions provider, the session store and the customization catalogs.
/// </summary>
/// <remarks>
/// Enumeration is scoped to the directory asked for rather than returning everything in
/// the store, so a catalog that walks <c>.codai/commands</c> cannot see the rest of the
/// project. A double that ignores its argument would make those tests pass for the wrong
/// reason.
/// </remarks>
internal sealed class InMemoryFileSystem : IFileSystem
{
    private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, string> Files => _files;

    public IReadOnlyCollection<string> Directories => _directories;

    public List<string> ReadPaths { get; } = [];

    public bool FailAtomicWrites { get; set; }

    public InMemoryFileSystem AddDirectory(string path)
    {
        for (var current = Normalize(path); !string.IsNullOrEmpty(current); current = Parent(current))
        {
            if (!_directories.Add(current))
            {
                break;
            }
        }

        return this;
    }

    public InMemoryFileSystem AddFile(string path, string content)
    {
        var full = Normalize(path);
        AddDirectory(Parent(full) ?? full);
        _files[full] = content;

        return this;
    }

    public static string Normalize(string path) =>
        Path.GetFullPath(path).Replace('\\', '/').TrimEnd('/');

    public bool FileExists(string path) => _files.ContainsKey(Normalize(path));

    public bool DirectoryExists(string path) => _directories.Contains(Normalize(path));

    public long GetFileSize(string path) =>
        _files.TryGetValue(Normalize(path), out var content) ? content.Length : 0;

    public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReadPaths.Add(Normalize(path));

        return _files.TryGetValue(Normalize(path), out var content)
            ? Task.FromResult(content)
            : throw new FileNotFoundException("File not found.", path);
    }

    public Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken = default)
    {
        AddFile(path, contents);
        return Task.CompletedTask;
    }

    public Task WriteAllTextAtomicallyAsync(string path, string contents, CancellationToken cancellationToken = default)
    {
        if (FailAtomicWrites)
        {
            throw new IOException("Atomic replacement failed.");
        }

        AddFile(path, contents);
        return Task.CompletedTask;
    }

    public void CreateDirectory(string path) => AddDirectory(path);

    public void DeleteFile(string path) => _files.Remove(Normalize(path));

    public void DeleteDirectory(string path, bool recursive) => _directories.Remove(Normalize(path));

    public IEnumerable<string> EnumerateFiles(string path)
    {
        var full = Normalize(path);

        return
        [
            .. _files.Keys
                .Where(file => string.Equals(Parent(file), full, StringComparison.OrdinalIgnoreCase))
                .OrderBy(file => file, StringComparer.Ordinal),
        ];
    }

    public IEnumerable<string> EnumerateDirectories(string path)
    {
        var full = Normalize(path);

        return
        [
            .. _directories
                .Where(directory => string.Equals(Parent(directory), full, StringComparison.OrdinalIgnoreCase))
                .OrderBy(directory => directory, StringComparer.Ordinal),
        ];
    }

    private static string? Parent(string path)
    {
        var separator = path.LastIndexOf('/');
        return separator <= 1 ? null : path[..separator];
    }
}

