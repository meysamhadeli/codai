using System.Text.Json;
using Codai.Core.Abstractions;

namespace Codai.Core.Customization;

/// <summary>Bounded nearest-first discovery scopes shared by skills and MCP manifests.</summary>
public static class DiscoveryDirectories
{
    /// <summary>Loads discovery-only metadata from home and bounded project ancestors.</summary>
    public static async Task<DiscoveryRoots> LoadMetadataAsync(string rootDirectory, IFileSystem fileSystem,
        string? userHomeDirectory = null, CancellationToken cancellationToken = default)
    {
        var repositories = new List<string>();
        var skills = new List<string>();
        var manifests = new List<string>();
        var files = Ancestors(rootDirectory, fileSystem, userHomeDirectory).Reverse().Select(directory => Path.Combine(directory, ".codai.json")).ToList();
        if (userHomeDirectory is not null)
        {
            files.Insert(0, Path.Combine(userHomeDirectory, ".codai.json"));
        }
        foreach (var file in files.DistinctBy(fileSystem.GetCanonicalPath, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!fileSystem.FileExists(file))
            {
                continue;
            }
            if (fileSystem.GetFileSize(file) > 65536)
            {
                throw new IOException("Discovery metadata exceeded its 64 KiB limit.");
            }
            try
            {
                using var document = JsonDocument.Parse(await fileSystem.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false),
                    new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip, MaxDepth = 32 });
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    throw new JsonException("Discovery metadata must be an object.");
                }
                ReadPaths(root, "additionalDirectories", repositories, Path.GetDirectoryName(file)!, userHomeDirectory);
                ReadPaths(root, "skillDirectories", skills, Path.GetDirectoryName(file)!, userHomeDirectory);
                ReadPaths(root, "mcpConfigFiles", manifests, Path.GetDirectoryName(file)!, userHomeDirectory);
            }
            catch (Exception exception) when (exception is JsonException or ArgumentException or NotSupportedException)
            {
                throw new IOException("Could not load discovery metadata. Check its JSON fields and paths.", exception);
            }
        }
        return new(repositories, skills, manifests);
    }

    private static void ReadPaths(JsonElement root, string key, List<string> paths, string baseDirectory, string? home)
    {
        if (!root.TryGetProperty(key, out var values))
        {
            return;
        }
        if (values.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("Discovery paths must be arrays.");
        }
        foreach (var value in values.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            {
                throw new JsonException("Discovery paths must contain nonempty strings.");
            }
            var path = value.GetString()!;
            if (home is not null && (path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal)))
            {
                path = Path.Combine(home, path[2..]);
            }
            path = Path.GetFullPath(Path.Combine(baseDirectory, path));
            if (!paths.Contains(path, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal))
            {
                if (paths.Count >= 32)
                {
                    throw new JsonException("Discovery supports at most 32 additional paths per kind.");
                }
                paths.Add(path);
            }
        }
    }

    /// <summary>Walks through the nearest Git root, stopping before inherited home or filesystem root; an explicit home root is included.</summary>
    public static IReadOnlyList<string> Ancestors(string rootDirectory, IFileSystem fileSystem, string? userHomeDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentNullException.ThrowIfNull(fileSystem);
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var boundaryHome = userHomeDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var home = string.IsNullOrWhiteSpace(boundaryHome) ? null : Path.TrimEndingDirectorySeparator(Path.GetFullPath(boundaryHome));
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var systemHome = string.IsNullOrWhiteSpace(profile) ? null : Path.TrimEndingDirectorySeparator(Path.GetFullPath(profile));
        var current = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootDirectory));
        var directories = new List<string>();
        for (var depth = 0; depth < 128; depth++)
        {
            if ((directories.Count > 0 && (comparer.Equals(current, home) || comparer.Equals(current, systemHome))) || comparer.Equals(current, Path.GetPathRoot(current)))
            {
                break;
            }
            directories.Add(current);
            var git = Path.Combine(current, ".git");
            if (fileSystem.DirectoryExists(git) || fileSystem.FileExists(git))
            {
                break;
            }
            var parent = Path.GetDirectoryName(current);
            if (parent is null || comparer.Equals(parent, current))
            {
                break;
            }
            current = parent;
        }
        return directories;
    }
}

/// <summary>Explicit opt-in directories and MCP files from discovery metadata.</summary>
/// <param name="AdditionalDirectories">Additional repositories whose native/shared discovery folders are included.</param>
/// <param name="SkillDirectories">Directories containing immediate skill folders.</param>
/// <param name="McpConfigFiles">Explicit MCP manifest paths.</param>
public sealed record DiscoveryRoots(IReadOnlyList<string> AdditionalDirectories, IReadOnlyList<string> SkillDirectories, IReadOnlyList<string> McpConfigFiles);
