using System.Text.Json;
using Codai.Core.Abstractions;

namespace Codai.Core.Customization;

/// <summary>Normalized, project-scoped contributions from a trusted installed bundle.</summary>
public sealed record InstalledPluginContributions(string Name, string RootDirectory,
    IReadOnlyList<string> Skills, IReadOnlyList<string> Commands, IReadOnlyList<string> Mcp, IReadOnlyList<string> Hooks);

/// <summary>Reads installed bundle receipts without loading assemblies or executing contributions.</summary>
public static class InstalledPluginCatalog
{
    /// <summary>Receipt written after a bundle has passed installation validation.</summary>
    public const string ReceiptFileName = "codai-plugin.json";

    /// <summary>Loads enabled receipts, requiring all contribution paths to remain inside their bundle.</summary>
    public static async Task<IReadOnlyList<InstalledPluginContributions>> LoadAsync(string projectRoot, IFileSystem fileSystem,
        CancellationToken cancellationToken = default)
    {
        var plugins = Path.Combine(projectRoot, ".codai", "plugins");
        if (!fileSystem.DirectoryExists(plugins)) return [];
        var result = new List<InstalledPluginContributions>();
        foreach (var directory in fileSystem.EnumerateDirectories(plugins).Order(StringComparer.Ordinal).Take(65))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (result.Count >= 64) throw new IOException("At most 64 installed plugin bundles are supported.");
            var file = Path.Combine(directory, ReceiptFileName);
            if (!fileSystem.FileExists(file)) continue;
            if (!Within(fileSystem.GetCanonicalPath(plugins), fileSystem.GetCanonicalPath(file)) || fileSystem.GetFileSize(file) > 65536)
                throw new IOException("Installed plugin receipt is outside its root or too large.");
            using var document = JsonDocument.Parse(await fileSystem.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false));
            var receipt = document.RootElement;
            if (receipt.TryGetProperty("enabled", out var enabled) && enabled.ValueKind == JsonValueKind.False) continue;
            if (!receipt.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String || name.GetString() != Path.GetFileName(directory))
                throw new IOException("Installed plugin receipt has an invalid name.");
            result.Add(new(name.GetString()!, Path.GetFullPath(directory),
                Paths(receipt, "skills", directory, fileSystem), Paths(receipt, "commands", directory, fileSystem),
                Paths(receipt, "mcp", directory, fileSystem), Paths(receipt, "hooks", directory, fileSystem)));
        }
        return result;
    }

    private static string[] Paths(JsonElement receipt, string key, string directory, IFileSystem fileSystem)
    {
        if (!receipt.TryGetProperty(key, out var paths)) return [];
        if (paths.ValueKind != JsonValueKind.Array || paths.GetArrayLength() > 32) throw new IOException("Plugin contribution paths must be bounded arrays.");
        return paths.EnumerateArray().Select(value =>
        {
            if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString())) throw new IOException("Plugin contribution path is invalid.");
            var path = Path.GetFullPath(Path.Combine(directory, value.GetString()!));
            if (!Within(fileSystem.GetCanonicalPath(directory), fileSystem.GetCanonicalPath(path))) throw new IOException("Plugin contribution escapes its bundle.");
            return path;
        }).ToArray();
    }

    private static bool Within(string root, string path) => path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar,
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}