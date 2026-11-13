using System.IO.Compression;
using System.Text.Json;

namespace Codai.Tools.Plugins;

internal sealed class PluginBundleInstaller(HttpClient httpClient)
{
    private static readonly string[] ManifestPaths = ["plugin.json", ".claude-plugin/plugin.json", ".github/plugin/plugin.json"];
    private sealed record Source(string Location, string? Subdirectory = null);

    internal async Task<string> InstallAsync(PluginMarketplaceEntry plugin, string marketplaceSource, string projectRoot,
        bool overwrite, CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(projectRoot);
        var destination = Path.Combine(root, ".codai", "plugins", plugin.Name);
        if (Directory.Exists(destination) && !overwrite) throw new IOException($"Plugin '{plugin.Name}' is already installed.");
        var staging = Path.Combine(root, ".codai", "plugin-staging", Guid.NewGuid().ToString("N"));
        var bundle = Path.Combine(staging, "bundle");
        var backup = Path.Combine(staging, "previous");
        Directory.CreateDirectory(bundle);
        try
        {
            var source = Resolve(plugin.Source, marketplaceSource, root);
            if (PluginMarketplaceClient.TryCreateRemoteUri(source.Location, out var uri))
            {
                PluginMarketplaceClient.ValidateRemoteUri(uri);
                using var response = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > PluginMarketplaceManager.MaxPluginBundleBytes)
                    throw new InvalidDataException("Plugin download exceeds the 50 MB limit.");
                await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                var bytes = await PluginMarketplaceClient.ReadBoundedAsync(input, PluginMarketplaceManager.MaxPluginBundleBytes, cancellationToken).ConfigureAwait(false);
                using var stream = new MemoryStream(bytes, writable: false);
                await ExtractAsync(stream, bundle, source.Subdirectory, cancellationToken).ConfigureAwait(false);
            }
            else if (Directory.Exists(source.Location))
            {
                CopyDirectory(source.Location, bundle, cancellationToken);
            }
            else if (File.Exists(source.Location))
            {
                if (new FileInfo(source.Location).Length > PluginMarketplaceManager.MaxPluginBundleBytes)
                    throw new InvalidDataException("Plugin bundle exceeds the 50 MB limit.");
                await using var input = File.OpenRead(source.Location);
                await ExtractAsync(input, bundle, null, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                throw new FileNotFoundException("Plugin source was not found.", source.Location);
            }

            var manifest = ManifestPaths
                .Select(relative => Path.Combine(bundle, relative)).FirstOrDefault(File.Exists)
                ?? throw new InvalidDataException("Plugin bundle has no supported plugin.json manifest.");
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(manifest, cancellationToken).ConfigureAwait(false));
            if (!document.RootElement.TryGetProperty("name", out var name) || name.GetString() != plugin.Name)
                throw new InvalidDataException("Plugin manifest name does not match the marketplace entry.");
            await PluginBundleManifest.WriteReceiptAsync(document.RootElement, bundle, destination, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (Directory.Exists(destination)) Directory.Move(destination, backup);
            try { Directory.Move(bundle, destination); }
            catch
            {
                if (Directory.Exists(backup)) Directory.Move(backup, destination);
                throw;
            }
            return destination;
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    private static Source Resolve(JsonElement source, string marketplaceSource, string projectRoot)
    {
        if (source.ValueKind == JsonValueKind.Object)
        {
            var kind = String(source, "source");
            if (kind is "github" or "url")
            {
                var repository = kind == "github" ? String(source, "repo") : String(source, "url");
                return GitHub(repository ?? string.Empty, String(source, "sha") ?? String(source, "ref") ?? "HEAD", String(source, "path") ?? string.Empty);
            }
            throw new InvalidDataException("Unsupported repository source; expected github or url.");
        }
        if (source.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(source.GetString()))
            throw new InvalidDataException("Plugin source must be a path, HTTPS archive, or GitHub repository.");
        var value = source.GetString()!;
        if (PluginMarketplaceClient.TryCreateRemoteUri(value, out var remote))
        {
            PluginMarketplaceClient.ValidateRemoteUri(remote);
            return remote.Host == "github.com" ? GitHub(value, "HEAD", string.Empty) : new(value);
        }
        if (PluginMarketplaceClient.TryCreateRemoteUri(marketplaceSource, out var catalog))
        {
            if (catalog.Host == "raw.githubusercontent.com")
            {
                var parts = catalog.AbsolutePath.Trim('/').Split('/');
                if (parts.Length >= 4) return GitHub(parts[0] + "/" + parts[1], parts[2], value);
            }
            return new(new Uri(catalog, value).AbsoluteUri);
        }
        var directory = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(marketplaceSource, projectRoot))!);
        var root = directory.Name == ".claude-plugin" ? directory.Parent! :
            directory.Name == "plugin" && directory.Parent?.Name == ".github" ? directory.Parent.Parent! : directory;
        return new(Path.GetFullPath(value, root.FullName));
    }

    private static string? String(JsonElement value, string name) => value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
        ? property.GetString() : null;

    private static Source GitHub(string repository, string reference, string path)
    {
        if (PluginMarketplaceClient.TryCreateRemoteUri(repository, out var uri))
        {
            PluginMarketplaceClient.ValidateRemoteUri(uri);
            if (uri.Host != "github.com" || uri.Query.Length > 0 || uri.Fragment.Length > 0)
                throw new InvalidDataException("Only public HTTPS GitHub repositories are supported.");
            repository = uri.AbsolutePath.Trim('/');
        }
        var parts = repository.Split('/');
        if (parts.Length != 2 || parts.Any(part => !PluginMarketplaceClient.IsSafeName(part)))
            throw new InvalidDataException("Repository must be a GitHub owner/repo.");
        var name = parts[1].EndsWith(".git", StringComparison.Ordinal) ? parts[1][..^4] : parts[1];
        if (reference.Length is 0 or > 200 || reference.Any(char.IsControl)) throw new InvalidDataException("Repository reference is invalid.");
        path = path.Replace('\\', '/');
        while (path.StartsWith("./", StringComparison.Ordinal)) path = path[2..];
        if (path.StartsWith('/') || path.Split('/').Any(part => part is ".." || part.Contains(':')))
            throw new InvalidDataException("Repository plugin path must stay inside its repository.");
        return new($"https://codeload.github.com/{parts[0]}/{name}/zip/{Uri.EscapeDataString(reference)}", path.Trim('/'));
    }

    private static async Task ExtractAsync(Stream input, string destination, string? subdirectory, CancellationToken cancellationToken)
    {
        using var archive = new ZipArchive(input, ZipArchiveMode.Read);
        if (archive.Entries.Count > 50000) throw new InvalidDataException("Plugin archive has too many entries.");
        var prefix = subdirectory is null ? string.Empty : archive.Entries.FirstOrDefault()?.FullName.Split('/')[0] + "/" +
            (subdirectory.Length > 0 ? subdirectory + "/" : string.Empty);
        long total = 0;
        var count = 0;
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = entry.FullName.Replace('\\', '/');
            if (!relative.StartsWith(prefix, StringComparison.Ordinal)) continue;
            relative = relative[prefix.Length..];
            if (relative.Length == 0) continue;
            var target = Path.GetFullPath(Path.Combine(destination, relative));
            if (!target.StartsWith(Path.GetFullPath(destination) + Path.DirectorySeparatorChar, PathComparison) ||
                relative.Split('/').Any(part => part is ".." || part.Contains(':')) || (entry.ExternalAttributes >> 16 & 0xF000) == 0xA000)
            {
                throw new InvalidDataException("Plugin archive contains an unsafe path or symbolic link.");
            }
            total += entry.Length;
            if (total > PluginMarketplaceManager.MaxPluginBundleBytes || ++count > 10000)
                throw new InvalidDataException("Plugin bundle exceeded its size or file-count limit.");
            if (relative.EndsWith('/')) { Directory.CreateDirectory(target); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var source = entry.Open();
            await using var output = File.Create(target);
            await source.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            if (!OperatingSystem.IsWindows() && (entry.ExternalAttributes >> 16 & 0x49) != 0)
                File.SetUnixFileMode(target, File.GetUnixFileMode(target) | UnixFileMode.UserExecute);
        }
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static void CopyDirectory(string source, string destination, CancellationToken cancellationToken)
    {
        if (Path.GetFullPath(destination).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(source)) + Path.DirectorySeparatorChar, PathComparison))
            throw new InvalidDataException("Cannot install a directory into itself.");
        var pending = new Stack<string>();
        pending.Push(source);
        long total = 0;
        var count = 0;
        while (pending.TryPop(out var directory))
        {
            foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++count > 10000 || entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    throw new InvalidDataException("Plugin directory contains links or too many files.");
                var target = Path.Combine(destination, Path.GetRelativePath(source, entry.FullName));
                if (entry is DirectoryInfo) { Directory.CreateDirectory(target); pending.Push(entry.FullName); }
                else
                {
                    total += ((FileInfo)entry).Length;
                    if (total > PluginMarketplaceManager.MaxPluginBundleBytes) throw new InvalidDataException("Plugin bundle exceeds the 50 MB limit.");
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(entry.FullName, target);
                }
            }
        }
    }
}