using Codai.Core.Abstractions;

namespace Codai.Core.Tests.Fakes;

/// <summary>
/// Minimal in-memory <see cref="IFileSystem"/> for the core services that read or
/// write files: the instructions provider and the session store.
/// </summary>
internal sealed class InMemoryFileSystem : IFileSystem
{
    private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, string> Files => _files;

    public IReadOnlyCollection<string> Directories => _directories;

    public void AddFile(string path, string content) => _files[Normalize(path)] = content;

    public static string Normalize(string path) =>
        Path.GetFullPath(path).Replace('\\', '/').TrimEnd('/');

    public bool FileExists(string path) => _files.ContainsKey(Normalize(path));

    public bool DirectoryExists(string path) => _directories.Contains(Normalize(path));

    public long GetFileSize(string path) =>
        _files.TryGetValue(Normalize(path), out var content) ? content.Length : 0;

    public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default) =>
        _files.TryGetValue(Normalize(path), out var content)
            ? Task.FromResult(content)
            : throw new FileNotFoundException("File not found.", path);

    public Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken = default)
    {
        AddFile(path, contents);
        return Task.CompletedTask;
    }

    public void CreateDirectory(string path) => _directories.Add(Normalize(path));

    public void DeleteFile(string path) => _files.Remove(Normalize(path));

    public void DeleteDirectory(string path, bool recursive) => _directories.Remove(Normalize(path));

    public IEnumerable<string> EnumerateFiles(string path) => _files.Keys;

    public IEnumerable<string> EnumerateDirectories(string path) => _directories;
}
