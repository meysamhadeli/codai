namespace Codai.Core.Abstractions;

/// <summary>
/// Minimal file-system surface used by the scanner and diff applier so they can be
/// tested without touching a real disk.
/// </summary>
public interface IFileSystem
{
    bool FileExists(string path);

    bool DirectoryExists(string path);

    long GetFileSize(string path);

    Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default);

    Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken = default);

    Task WriteAllTextAtomicallyAsync(string path, string contents, CancellationToken cancellationToken = default);

    void CreateDirectory(string path);

    void DeleteFile(string path);

    void DeleteDirectory(string path, bool recursive);

    IEnumerable<string> EnumerateFiles(string path);

    IEnumerable<string> EnumerateDirectories(string path);
}
