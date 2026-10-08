using Codai.Core.Abstractions;

namespace Codai.Core.IO;

/// <summary>
/// Default <see cref="IFileSystem"/> backed by <see cref="System.IO"/>.
/// </summary>
public sealed class PhysicalFileSystem : IFileSystem
{
    public static PhysicalFileSystem Instance { get; } = new();

    public bool FileExists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public long GetFileSize(string path) => new FileInfo(path).Length;

    public async Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default)
    {
#if NET10_0_OR_GREATER
        return await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
#else
        cancellationToken.ThrowIfCancellationRequested();
        return await Task.Run(() => File.ReadAllText(path), cancellationToken).ConfigureAwait(false);
#endif
    }

    public async Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken = default)
    {
#if NET10_0_OR_GREATER
        await File.WriteAllTextAsync(path, contents, cancellationToken).ConfigureAwait(false);
#else
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Run(() => File.WriteAllText(path, contents), cancellationToken).ConfigureAwait(false);
#endif
    }

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public void DeleteFile(string path) => File.Delete(path);

    public void DeleteDirectory(string path, bool recursive) => Directory.Delete(path, recursive);

    public IEnumerable<string> EnumerateFiles(string path) => Directory.EnumerateFiles(path);

    public IEnumerable<string> EnumerateDirectories(string path) => Directory.EnumerateDirectories(path);
}
