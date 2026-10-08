using Codai.Core.Abstractions;

namespace Codai.Core.IO;

/// <summary>
/// Default <see cref="IFileSystem"/> backed by <see cref="System.IO"/>.
/// </summary>
public sealed class PhysicalFileSystem : IFileSystem
{
    /// <summary>The shared instance; this type is stateless.</summary>
    public static PhysicalFileSystem Instance { get; } = new();

    /// <inheritdoc />
    public bool FileExists(string path) => File.Exists(path);

    /// <inheritdoc />
    public bool DirectoryExists(string path) => Directory.Exists(path);

    /// <inheritdoc />
    public long GetFileSize(string path) => new FileInfo(path).Length;

    /// <inheritdoc />
    public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default) =>
        File.ReadAllTextAsync(path, cancellationToken);

    /// <inheritdoc />
    public Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken = default) =>
        File.WriteAllTextAsync(path, contents, cancellationToken);

    /// <inheritdoc />
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    /// <inheritdoc />
    public void DeleteFile(string path) => File.Delete(path);

    /// <inheritdoc />
    public void DeleteDirectory(string path, bool recursive) => Directory.Delete(path, recursive);

    /// <inheritdoc />
    public IEnumerable<string> EnumerateFiles(string path) => Directory.EnumerateFiles(path);

    /// <inheritdoc />
    public IEnumerable<string> EnumerateDirectories(string path) => Directory.EnumerateDirectories(path);
}
