using Codai.Core.Abstractions;
using Codai.Core.IO;

namespace Codai.Core.Customization;

/// <summary>Loads bounded, durable project knowledge from <c>.codai/memory</c>.</summary>
public sealed class ProjectMemoryCatalog : IProjectMemoryCatalog
{
    public const int MaxDocuments = 12;
    public const int MaxCharactersPerDocument = 12 * 1024;
    public const int MaxTotalCharacters = 48 * 1024;
    public const int MaxDepth = 2;

    private readonly IFileSystem _fileSystem;

    public ProjectMemoryCatalog(IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        _fileSystem = fileSystem;
    }

    public async Task<IReadOnlyList<ProjectMemoryDocument>> LoadAsync(
        string rootDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        var memoryDirectory = Path.Combine(rootDirectory, CodaiPaths.ToPlatformPath(CodaiPaths.MemoryDirectory));
        if (!_fileSystem.DirectoryExists(memoryDirectory))
        {
            return [];
        }

        var documents = new List<ProjectMemoryDocument>();
        var totalCharacters = 0;
        foreach (var file in EnumerateMarkdown(memoryDirectory, 0)
                     .OrderBy(path => Path.GetRelativePath(memoryDirectory, path), StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (documents.Count >= MaxDocuments || totalCharacters >= MaxTotalCharacters)
            {
                break;
            }

            try
            {
                var content = await _fileSystem.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
                var remaining = MaxTotalCharacters - totalCharacters;
                var length = Math.Min(Math.Min(content.Length, MaxCharactersPerDocument), remaining);
                if (length == 0)
                {
                    continue;
                }

                if (length < content.Length)
                {
                    content = content[..length] + "\n\n[... memory document truncated ...]";
                }

                var relativePath = Path.GetRelativePath(rootDirectory, file).Replace('\\', '/');
                documents.Add(new ProjectMemoryDocument(Path.GetFileNameWithoutExtension(file), relativePath, content));
                totalCharacters += content.Length;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        return documents;
    }

    private IEnumerable<string> EnumerateMarkdown(string directory, int depth)
    {
        foreach (var file in _fileSystem.EnumerateFiles(directory))
        {
            if (string.Equals(Path.GetExtension(file), ".md", StringComparison.OrdinalIgnoreCase))
            {
                yield return file;
            }
        }

        if (depth >= MaxDepth)
        {
            yield break;
        }

        foreach (var child in _fileSystem.EnumerateDirectories(directory)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var file in EnumerateMarkdown(child, depth + 1))
            {
                yield return file;
            }
        }
    }
}
