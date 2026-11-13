using System.Text.Json;
using System.Text.Json.Serialization;

namespace Codai.Tools.Plugins;

public sealed record PluginMarketplace(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("owner")] PluginMarketplaceOwner? Owner,
    [property: JsonPropertyName("plugins")] IReadOnlyList<PluginMarketplaceEntry> Plugins);

public sealed record PluginMarketplaceOwner(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("email")] string? Email = null,
    [property: JsonPropertyName("url")] string? Url = null);

public sealed record PluginMarketplaceEntry(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("source")] JsonElement Source,
    [property: JsonPropertyName("description")] string? Description = null,
    [property: JsonPropertyName("version")] string? Version = null,
    [property: JsonPropertyName("category")] string? Category = null,
    [property: JsonPropertyName("strict")] bool Strict = false);

public sealed record InstalledPlugin(
    string Name,
    string Marketplace,
    string Version,
    string Directory,
    string? Description);

/// <summary>Reads Claude-style marketplace.json documents without loading plugin code.</summary>
public sealed class PluginMarketplaceClient(HttpClient? httpClient = null)
{
    public const long MaxMarketplaceBytes = 5 * 1024 * 1024;
    private readonly HttpClient _httpClient = httpClient ?? new HttpClient();

    /// <summary>Resolves GitHub repository shorthand and URLs to validated marketplace metadata.</summary>
    public async Task<string> ResolveSourceAsync(string source, string baseDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        source = source.Trim();
        if (File.Exists(Path.GetFullPath(source, baseDirectory)) || Directory.Exists(Path.GetFullPath(source, baseDirectory)))
        {
            var local = Path.GetFullPath(source, baseDirectory);
            if (File.Exists(local)) return local;
            foreach (var relative in new[] { ".github/plugin/marketplace.json", ".claude-plugin/marketplace.json", "marketplace.json" })
            {
                var candidate = Path.Combine(local, relative);
                if (File.Exists(candidate)) return candidate;
            }
            throw new InvalidDataException("Local repository has no supported marketplace.json.");
        }

        var repository = source;
        if (TryCreateRemoteUri(source, out var address))
        {
            ValidateRemoteUri(address);
            if (address.Host != "github.com") return address.AbsoluteUri;
            if (address.Query.Length > 0 || address.Fragment.Length > 0) throw new InvalidDataException("GitHub marketplace URLs cannot contain query strings or fragments.");
            repository = address.AbsolutePath.Trim('/');
        }
        var parts = repository.Split('/');
        if (parts.Length != 2 || parts.Any(part => !IsSafeName(part)))
        {
            if (TryCreateRemoteUri(source, out _)) return source;
            return Path.GetFullPath(source, baseDirectory);
        }
        var name = parts[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? parts[1][..^4] : parts[1];
        foreach (var relative in new[] { ".github/plugin/marketplace.json", ".claude-plugin/marketplace.json" })
        {
            var candidate = $"https://raw.githubusercontent.com/{parts[0]}/{name}/HEAD/{relative}";
            try
            {
                await LoadAsync(candidate, baseDirectory, cancellationToken).ConfigureAwait(false);
                return candidate;
            }
            catch (HttpRequestException exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
            }
        }
        throw new InvalidDataException("GitHub repository has no supported marketplace catalog.");
    }

    public async Task<PluginMarketplace> LoadAsync(string source, string baseDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);

        byte[] content;
        if (TryCreateRemoteUri(source, out var uri))
        {
            ValidateRemoteUri(uri);
            using var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > MaxMarketplaceBytes)
            {
                throw new InvalidDataException("Marketplace metadata exceeds the 5 MB limit.");
            }
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            content = await ReadBoundedAsync(stream, MaxMarketplaceBytes, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var path = Path.GetFullPath(source, baseDirectory);
            if (!File.Exists(path)) throw new FileNotFoundException("Marketplace metadata was not found.", path);
            await using var stream = File.OpenRead(path);
            content = await ReadBoundedAsync(stream, MaxMarketplaceBytes, cancellationToken).ConfigureAwait(false);
        }

        var marketplace = JsonSerializer.Deserialize<PluginMarketplace>(content)
            ?? throw new InvalidDataException("Marketplace metadata is empty.");
        ValidateMarketplace(marketplace);
        return marketplace;
    }

    internal static void ValidateMarketplace(PluginMarketplace marketplace)
    {
        if (!IsSafeName(marketplace.Name) || string.Equals(marketplace.Name, "inline", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(marketplace.Name, "builtin", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Marketplace name must be a safe kebab-case identifier.");
        }
        if (marketplace.Plugins is null || marketplace.Plugins.Count > 500)
        {
            throw new InvalidDataException("Marketplace must contain a plugins array with at most 500 entries.");
        }
        foreach (var plugin in marketplace.Plugins)
        {
            if (plugin is null || !IsSafeName(plugin.Name) || plugin.Source.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            {
                throw new InvalidDataException("Marketplace contains invalid plugin metadata.");
            }
        }
    }

    internal static void ValidateRemoteUri(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidOperationException("Marketplace and plugin URLs must use HTTPS and contain no credentials.");
    }

    internal static bool TryCreateRemoteUri(string value, out Uri uri) =>
        Uri.TryCreate(value, UriKind.Absolute, out uri!) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    internal static bool IsSafeName(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 80 &&
        value is not ("." or "..") &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');

    internal static async Task<byte[]> ReadBoundedAsync(Stream stream, long maxBytes, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            if (buffer.Length + read > maxBytes) throw new InvalidDataException($"Remote content exceeds the {maxBytes / 1024 / 1024} MB limit.");
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
        return buffer.ToArray();
    }
}

/// <summary>Installs marketplace plugin bundles into the existing .codai plugin directory.</summary>
public sealed class PluginMarketplaceManager(PluginMarketplaceClient? client = null, HttpClient? httpClient = null)
{
    /// <summary>Public Anthropic marketplace metadata; browsing never executes its plugins.</summary>
    public const string OfficialMarketplaceSource = "https://raw.githubusercontent.com/anthropics/claude-plugins-official/main/.claude-plugin/marketplace.json";
    /// <summary>Public GitHub-maintained Copilot plugin collection.</summary>
    public const string CopilotMarketplaceSource = "https://raw.githubusercontent.com/github/copilot-plugins/HEAD/.github/plugin/marketplace.json";
    /// <summary>Community marketplace included with GitHub Copilot.</summary>
    public const string AwesomeCopilotMarketplaceSource = "https://raw.githubusercontent.com/github/awesome-copilot/HEAD/.github/plugin/marketplace.json";
    /// <summary>Default metadata-only catalogs offered to the plugin browser.</summary>
    public static IReadOnlyList<string> DefaultMarketplaceSources { get; } = Array.AsReadOnly(new[]
    {
        OfficialMarketplaceSource, CopilotMarketplaceSource, AwesomeCopilotMarketplaceSource,
    });
    public const long MaxPluginBundleBytes = 50 * 1024 * 1024;
    private readonly PluginMarketplaceClient _client = client ?? new PluginMarketplaceClient(httpClient);
    private readonly HttpClient _httpClient = httpClient ?? new HttpClient();

    /// <summary>Loads a marketplace including its identity and plugin metadata.</summary>
    public Task<PluginMarketplace> LoadAsync(string marketplaceSource, string baseDirectory, CancellationToken cancellationToken = default) =>
        _client.LoadAsync(marketplaceSource, baseDirectory, cancellationToken);

    /// <summary>Resolves supported repository inputs to an explicit metadata source.</summary>
    public Task<string> ResolveSourceAsync(string source, string baseDirectory, CancellationToken cancellationToken = default) =>
        _client.ResolveSourceAsync(source, baseDirectory, cancellationToken);

    public async Task<IReadOnlyList<PluginMarketplaceEntry>> DiscoverAsync(string marketplaceSource, string baseDirectory, CancellationToken cancellationToken = default)
        => (await _client.LoadAsync(marketplaceSource, baseDirectory, cancellationToken).ConfigureAwait(false)).Plugins;

    public async Task<string> InstallAsync(string marketplaceSource, string pluginName, string projectRoot, bool overwrite = false, CancellationToken cancellationToken = default)
    {
        var marketplace = await _client.LoadAsync(marketplaceSource, projectRoot, cancellationToken).ConfigureAwait(false);
        var plugin = marketplace.Plugins.FirstOrDefault(entry => string.Equals(entry.Name, pluginName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Plugin '{pluginName}' was not found in marketplace '{marketplace.Name}'.");
        return await new PluginBundleInstaller(_httpClient).InstallAsync(plugin, marketplaceSource, projectRoot, overwrite, cancellationToken).ConfigureAwait(false);
    }
}