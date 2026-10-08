namespace Codai.Analysis.Tests.Fakes;

/// <summary>In-memory <see cref="Core.Abstractions.IFileSystem"/> for deterministic tests.</summary>
internal sealed class InMemoryFileSystem : Core.Abstractions.IFileSystem
{
    private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _sizes = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);

    public InMemoryFileSystem AddFile(string path, string contents, long? sizeOverride = null)
    {
        var normalized = Normalize(path);
        _files[normalized] = contents;
        _sizes[normalized] = sizeOverride ?? System.Text.Encoding.UTF8.GetByteCount(contents);

        var directory = NormalizeDirectory(Path.GetDirectoryName(normalized));
        while (!string.IsNullOrEmpty(directory))
        {
            _directories.Add(directory);
            directory = NormalizeDirectory(Path.GetDirectoryName(directory));
        }

        return this;
    }

    public bool FileExists(string path) => _files.ContainsKey(Normalize(path));

    public bool DirectoryExists(string path) => _directories.Contains(Normalize(path));

    public long GetFileSize(string path) => _sizes[Normalize(path)];

    public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default) =>
        Task.FromResult(_files[Normalize(path)]);

    public Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken = default)
    {
        AddFile(path, contents);
        return Task.CompletedTask;
    }

    public void CreateDirectory(string path) => _directories.Add(Normalize(path));

    public void DeleteFile(string path)
    {
        var normalized = Normalize(path);
        _files.Remove(normalized);
        _sizes.Remove(normalized);
    }

    public void DeleteDirectory(string path, bool recursive)
    {
        var normalized = Normalize(path);
        _directories.Remove(normalized);

        foreach (var file in _files.Keys.Where(f => f.StartsWith(normalized + "/", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            _files.Remove(file);
            _sizes.Remove(file);
        }
    }

    public IEnumerable<string> EnumerateFiles(string path)
    {
        var normalized = Normalize(path);
        return _files.Keys.Where(file =>
            string.Equals(NormalizeDirectory(Path.GetDirectoryName(file)), normalized, StringComparison.OrdinalIgnoreCase));
    }

    public IEnumerable<string> EnumerateDirectories(string path)
    {
        var normalized = Normalize(path);
        return _directories.Where(directory =>
            string.Equals(NormalizeDirectory(Path.GetDirectoryName(directory)), normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static string Normalize(string path) =>
        path.Replace('\\', '/').TrimEnd('/');

    private static string NormalizeDirectory(string? path) =>
        string.IsNullOrEmpty(path) ? string.Empty : Normalize(path);
}
