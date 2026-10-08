using System.Text;
using Codai.Core.Abstractions;

namespace Codai.Core.Prompting;

/// <summary>
/// Reads requested file contents from disk using the abstracted
/// <see cref="IFileSystem"/>. Unreadable files are skipped instead of failing the
/// whole request.
/// </summary>
public sealed class RequestedContextResolver : IRequestedContextResolver
{
    private const string FileSeparator = "\n---------\n\n";

    private readonly IFileSystem _fileSystem;

    public RequestedContextResolver(IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        _fileSystem = fileSystem;
    }

    /// <inheritdoc />
    public async Task<string?> ResolveAsync(
        string rootDirectory,
        IReadOnlyList<string> relativePaths,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentNullException.ThrowIfNull(relativePaths);

        if (relativePaths.Count == 0)
        {
            return null;
        }

        var blocks = new List<string>(relativePaths.Count);

        foreach (var relativePath in relativePaths)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                continue;
            }

            var normalized = relativePath.Trim().Replace('\\', '/');
            var fullPath = Path.GetFullPath(Path.Combine(rootDirectory, normalized));

            // Never escape the project root, even if the model asks for it.
            if (!IsInsideRoot(rootDirectory, fullPath) || !_fileSystem.FileExists(fullPath))
            {
                continue;
            }

            try
            {
                var content = await _fileSystem
                    .ReadAllTextAsync(fullPath, cancellationToken)
                    .ConfigureAwait(false);

                var builder = new StringBuilder();
                builder.Append("**File: ").Append(normalized).Append("**\n\n").Append(content);

                blocks.Add(builder.ToString());
            }
            catch (IOException)
            {
                // Skip files that disappear or cannot be read.
            }
            catch (UnauthorizedAccessException)
            {
                // Skip files we are not allowed to read.
            }
        }

        return blocks.Count == 0 ? null : string.Join(FileSeparator, blocks);
    }

    private static bool IsInsideRoot(string rootDirectory, string fullPath)
    {
        var root = Path.GetFullPath(rootDirectory);
        if (!root.EndsWith(Path.DirectorySeparatorChar))
        {
            root += Path.DirectorySeparatorChar;
        }

        return fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            || string.Equals(fullPath, root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
    }
}
