using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Codai.Configuration;
using Codai.Core.Abstractions;
using Codai.Core.Customization;

namespace Codai.Mcp.Client;

/// <summary>Discovers MCP metadata without starting processes or opening network connections.</summary>
public sealed class McpManifestCatalog
{
    private readonly IFileSystem _fileSystem;
    private readonly string? _globalDirectory;
    private readonly string? _home;
    private readonly IEnvironmentReader _environment;

    /// <summary>Creates a metadata catalog with explicit global/home scopes.</summary>
    public McpManifestCatalog(IFileSystem fileSystem, string? globalConfigurationDirectory = null,
        string? userHomeDirectory = null, IEnvironmentReader? environment = null)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        _fileSystem = fileSystem;
        _globalDirectory = globalConfigurationDirectory is null ? null : Path.GetFullPath(globalConfigurationDirectory);
        _home = userHomeDirectory ?? (string.Equals(Path.GetFileName(_globalDirectory), ".codai", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(_globalDirectory) : null);
        _environment = environment ?? ProcessEnvironmentReader.Instance;
    }

    /// <summary>Loads layered manifests and native definitions, suppressing plugin endpoints already provided by enabled servers. Imports require private approval.</summary>
    public Task<IReadOnlyList<McpServerOptions>> LoadAsync(string projectRoot, IReadOnlyList<McpServerOptions> nativeServers,
        CancellationToken cancellationToken = default) => LoadDefinitionsAsync(projectRoot, nativeServers, true, cancellationToken);

    private async Task<IReadOnlyList<McpServerOptions>> LoadDefinitionsAsync(string projectRoot, IReadOnlyList<McpServerOptions> nativeServers,
        bool suppressDuplicates, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(nativeServers);
        var metadata = await DiscoveryDirectories.LoadMetadataAsync(projectRoot, _fileSystem, _home, cancellationToken).ConfigureAwait(false);
        var sources = new List<(string Path, string Scope)>();
        foreach (var plugin in await InstalledPluginCatalog.LoadAsync(projectRoot, _fileSystem, cancellationToken).ConfigureAwait(false))
            sources.AddRange(plugin.Mcp.Select(path => (path, "plugin")));
        if (_home is not null && _globalDirectory is not null)
        {
            sources.Add((Path.Combine(_home, ".claude", ".config.json"), "user"));
            sources.Add((Path.Combine(_home, ".claude.json"), "user"));
        }
        if (_globalDirectory is not null)
        {
            sources.Add((Path.Combine(_globalDirectory, ".codai.json"), "user"));
            sources.Add((Path.Combine(_globalDirectory, "mcp.json"), "user"));
            sources.Add((Path.Combine(_globalDirectory, ".mcp.json"), "user"));
        }
        if (_home is not null && _globalDirectory is not null)
        {
            sources.Add((Path.Combine(_home, ".codai.json"), "user"));
        }
        foreach (var directory in metadata.AdditionalDirectories)
        {
            AddProjectSources(sources, directory, "additional");
        }
        foreach (var directory in DiscoveryDirectories.Ancestors(projectRoot, _fileSystem, _home).Reverse())
        {
            AddProjectSources(sources, directory, "project");
        }
        sources.AddRange(metadata.McpConfigFiles.Select(path => (path, "explicit")));
        var servers = new Dictionary<string, McpServerOptions>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in sources.AsEnumerable().Reverse().DistinctBy(source => _fileSystem.GetCanonicalPath(source.Path), PathComparer).Reverse())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_fileSystem.FileExists(source.Path))
            {
                if (source.Scope == "explicit")
                {
                    throw new IOException("An explicitly configured MCP manifest was not found.");
                }
                continue;
            }
            using var document = await ReadDocumentAsync(source.Path, cancellationToken).ConfigureAwait(false);
            ReadServers(document.RootElement, source.Path, source.Scope, servers, projectRoot);
        }
        if (_home is not null && _globalDirectory is not null)
        {
            foreach (var state in new[] { Path.Combine(_home, ".claude", ".config.json"), Path.Combine(_home, ".claude.json"), Path.Combine(_home, ".codai.json") })
            {
                if (!_fileSystem.FileExists(state))
                {
                    continue;
                }
                using var document = await ReadDocumentAsync(state, cancellationToken).ConfigureAwait(false);
                if (document.RootElement.TryGetProperty("projects", out var projects) && projects.ValueKind == JsonValueKind.Object)
                {
                    foreach (var project in projects.EnumerateObject())
                    {
                        if (PathComparer.Equals(NormalizePath(project.Name), NormalizePath(projectRoot)))
                        {
                            ReadServers(project.Value, state, "local", servers, projectRoot);
                        }
                    }
                }
            }
        }
        var approvals = await ReadApprovalsAsync(projectRoot, cancellationToken).ConfigureAwait(false);
        var projectKey = ProjectKey(projectRoot);
        foreach (var server in servers.Values)
        {
            server.Enabled = approvals[projectKey]?[server.Name.ToLowerInvariant()]?["fingerprint"]?.GetValue<string>() == server.DiscoveryFingerprint &&
                approvals[projectKey]?[server.Name.ToLowerInvariant()]?["enabled"]?.GetValue<bool>() == true;
        }
        foreach (var native in nativeServers)
        {
            servers[native.Name] = native;
        }
        if (servers.Count > 200)
        {
            throw new IOException("MCP discovery exceeded its 200-server limit.");
        }
        var result = suppressDuplicates ? SuppressPluginDuplicates(servers.Values) : servers.Values.ToList();
        return result.OrderBy(server => server.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static List<McpServerOptions> SuppressPluginDuplicates(IReadOnlyCollection<McpServerOptions> servers)
    {
        var result = servers.Where(server => server.DiscoveryScope != "plugin").ToList();
        var endpoints = result.Where(server => server.Enabled).Select(EndpointKey).OfType<string>().ToHashSet(StringComparer.Ordinal);
        foreach (var plugin in servers.Where(server => server.DiscoveryScope == "plugin")
            .OrderByDescending(server => server.Enabled))
        {
            var endpoint = EndpointKey(plugin);
            if (endpoint is not null && endpoints.Contains(endpoint)) continue;
            result.Add(plugin);
            if (plugin.Enabled && endpoint is not null) endpoints.Add(endpoint);
        }
        return result;
    }

    private static string? EndpointKey(McpServerOptions server)
    {
        if (server.Transport == "stdio")
            return string.IsNullOrWhiteSpace(server.Command) ? null : "stdio:" + JsonSerializer.Serialize(new[] { server.Command }.Concat(server.Args));
        if (!Uri.TryCreate(server.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return null;
        return "remote:" + uri.GetComponents(UriComponents.SchemeAndServer, UriFormat.UriEscaped) +
            uri.AbsolutePath.TrimEnd('/') + uri.Query;
    }

    /// <summary>Saves explicit startup approval privately, bound to project and definition.</summary>
    public async Task SetEnabledAsync(string projectRoot, McpServerOptions server, bool enabled,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(server);
        if (_globalDirectory is null || string.IsNullOrWhiteSpace(server.DiscoveryFingerprint))
        {
            throw new IOException("Discovered MCP approval requires an explicit global configuration directory.");
        }
        if (!ApprovalStoreIsPrivate(projectRoot))
        {
            throw new IOException("MCP approval state must be stored outside the project repository.");
        }
        var current = (await LoadDefinitionsAsync(projectRoot, [], false, cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(candidate => string.Equals(candidate.Name, server.Name, StringComparison.OrdinalIgnoreCase));
        if (current?.DiscoveryFingerprint != server.DiscoveryFingerprint)
        {
            throw new IOException("The MCP definition changed during review. Review it again before enabling.");
        }
        await UserCustomizationSettings.UpdateAsync(_fileSystem, _globalDirectory, state =>
        {
            if (state["mcp"] is not null and not JsonObject)
            {
                throw new IOException("MCP approval state was malformed; no imported server was authorized.");
            }
            var approvals = state["mcp"] as JsonObject ?? new JsonObject();
            ValidateApprovals(approvals);
            var key = ProjectKey(projectRoot);
            if (approvals[key] is not JsonObject project)
            {
                project = new JsonObject();
                approvals[key] = project;
            }
            project[server.Name.ToLowerInvariant()] = new JsonObject
            {
                ["fingerprint"] = server.DiscoveryFingerprint,
                ["enabled"] = enabled,
            };
            if (state["mcp"] is null)
            {
                state["mcp"] = approvals;
            }
        }, cancellationToken).ConfigureAwait(false);
        server.Enabled = enabled;
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static string NormalizePath(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)).Replace('\\', '/');
    private string ProjectKey(string root)
    {
        var canonical = NormalizePath(_fileSystem.GetCanonicalPath(root));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(OperatingSystem.IsWindows() ? canonical.ToUpperInvariant() : canonical)));
    }

    private bool ApprovalStoreIsPrivate(string projectRoot, string? fileName = null)
    {
        if (_globalDirectory is null)
        {
            return false;
        }
        var boundary = DiscoveryDirectories.Ancestors(projectRoot, _fileSystem, _home).FirstOrDefault(directory =>
            _fileSystem.FileExists(Path.Combine(directory, ".git")) || _fileSystem.DirectoryExists(Path.Combine(directory, ".git"))) ?? projectRoot;
        var root = NormalizePath(_fileSystem.GetCanonicalPath(boundary));
        var state = NormalizePath(_fileSystem.GetCanonicalPath(fileName is null
            ? UserCustomizationSettings.GetFilePath(_globalDirectory)
            : Path.Combine(_globalDirectory, fileName)));
        return !PathComparer.Equals(root, state) && !state.StartsWith(root + "/", OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static void AddProjectSources(List<(string Path, string Scope)> sources, string directory, string scope)
    {
        sources.Add((Path.Combine(directory, ".vscode", "mcp.json"), scope));
        sources.Add((Path.Combine(directory, ".mcp.json"), scope));
        sources.Add((Path.Combine(directory, ".codai", "mcp.json"), scope));
        sources.Add((Path.Combine(directory, ".codai.json"), scope));
    }

    private async Task<JsonDocument> ReadDocumentAsync(string path, CancellationToken cancellationToken)
    {
        var limit = string.Equals(Path.GetFileName(path), ".codai.json", StringComparison.OrdinalIgnoreCase) ? 65536 : 1024 * 1024;
        if (_fileSystem.GetFileSize(path) > limit)
        {
            throw new IOException("MCP metadata exceeded its file-size limit.");
        }
        try
        {
            var document = JsonDocument.Parse(await _fileSystem.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false),
                new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip, MaxDepth = 32 });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                document.Dispose();
                throw new JsonException("MCP metadata must be an object.");
            }
            return document;
        }
        catch (JsonException exception)
        {
            throw new IOException("Could not parse MCP metadata. No imported server was started.", exception);
        }
    }

    private async Task<JsonObject> ReadApprovalsAsync(string projectRoot, CancellationToken cancellationToken)
    {
        if (_globalDirectory is null || !ApprovalStoreIsPrivate(projectRoot))
        {
            return new JsonObject();
        }
        var state = await UserCustomizationSettings.ReadAsync(_fileSystem, _globalDirectory, cancellationToken).ConfigureAwait(false);
        if (state["mcp"] is not null and not JsonObject)
        {
            throw new IOException("MCP approval state was malformed; no imported server was authorized.");
        }
        var approvals = state["mcp"] as JsonObject ?? new JsonObject();
        ValidateApprovals(approvals);
        var legacy = Path.Combine(_globalDirectory, "mcp-approvals.json");
        if (_fileSystem.FileExists(legacy) && ApprovalStoreIsPrivate(projectRoot, "mcp-approvals.json"))
        {
            using var document = await ReadDocumentAsync(legacy, cancellationToken).ConfigureAwait(false);
            var previous = JsonNode.Parse(document.RootElement.GetRawText())!.AsObject();
            ValidateApprovals(previous);
            await UserCustomizationSettings.UpdateAsync(_fileSystem, _globalDirectory, current =>
            {
                if (current["mcp"] is not null and not JsonObject)
                {
                    throw new IOException("MCP approval state was malformed; no imported server was authorized.");
                }
                var merged = current["mcp"] as JsonObject ?? new JsonObject();
                ValidateApprovals(merged);
                foreach (var project in previous)
                {
                    var entries = merged[project.Key] as JsonObject ?? new JsonObject();
                    foreach (var entry in project.Value!.AsObject())
                    {
                        if (!entries.ContainsKey(entry.Key))
                        {
                            entries[entry.Key] = entry.Value!.DeepClone();
                        }
                    }
                    if (!merged.ContainsKey(project.Key))
                    {
                        merged[project.Key] = entries;
                    }
                }
                if (current["mcp"] is null)
                {
                    current["mcp"] = merged;
                }
            }, cancellationToken).ConfigureAwait(false);
            _fileSystem.DeleteFile(legacy);
            state = await UserCustomizationSettings.ReadAsync(_fileSystem, _globalDirectory, cancellationToken).ConfigureAwait(false);
            approvals = state["mcp"]!.AsObject();
        }
        return approvals;
    }

    private static void ValidateApprovals(JsonObject approvals)
    {
        foreach (var project in approvals)
        {
            if (project.Value is not JsonObject entries)
            {
                throw new IOException("MCP approval state was malformed; no imported server was authorized.");
            }
            foreach (var entry in entries)
            {
                if (entry.Value is not JsonObject approval || approval["fingerprint"] is not JsonValue fingerprint ||
                    !fingerprint.TryGetValue<string>(out var hash) || hash is null || !Regex.IsMatch(hash, "^[A-F0-9]{64}$", RegexOptions.CultureInvariant) ||
                    approval["enabled"] is not JsonValue enabled || !enabled.TryGetValue<bool>(out _))
                {
                    throw new IOException("MCP approval state was malformed; no imported server was authorized.");
                }
            }
        }
    }

    private void ReadServers(JsonElement root, string source, string scope, Dictionary<string, McpServerOptions> servers, string projectRoot)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new IOException("MCP scope metadata must be an object.");
        }
        if (scope == "user" && root.TryGetProperty("mcp", out var native))
        {
            if (native.ValueKind != JsonValueKind.Object)
            {
                throw new IOException("MCP configuration must be an object.");
            }
            if (native.TryGetProperty("servers", out var entries))
            {
                if (entries.ValueKind != JsonValueKind.Array || entries.GetArrayLength() > 200)
                {
                    throw new IOException("Native MCP definitions must be a bounded array.");
                }
                var named = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in entries.EnumerateArray())
                {
                    if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty("name", out var name) ||
                        name.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(name.GetString()) ||
                        !named.TryAdd(name.GetString()!, entry))
                    {
                        throw new IOException("A native MCP server name was invalid or duplicated.");
                    }
                }
                ReadServers(JsonSerializer.SerializeToElement(new { mcpServers = named }), source, scope, servers, projectRoot);
            }
        }
        if (!root.TryGetProperty("mcpServers", out var definitions) && !root.TryGetProperty("servers", out definitions))
        {
            return;
        }
        if (definitions.ValueKind != JsonValueKind.Object)
        {
            throw new IOException("MCP server definitions must be a named object.");
        }
        foreach (var definition in definitions.EnumerateObject())
        {
            if (definition.Name.Length > 100 ||
                !Regex.IsMatch(definition.Name, "^[A-Za-z0-9][A-Za-z0-9_.-]*(?:/[A-Za-z0-9][A-Za-z0-9_.-]*)*$", RegexOptions.CultureInvariant) ||
                definition.Value.ValueKind != JsonValueKind.Object)
            {
                throw new IOException("An MCP server name or definition was invalid.");
            }
            var value = definition.Value;
            var url = Text(value, "url", projectRoot);
            var transport = Text(value, "type", projectRoot) ?? Text(value, "transport", projectRoot) ?? (url is null ? "stdio" : "http");
            var server = new McpServerOptions
            {
                Name = definition.Name,
                Enabled = false,
                Command = Text(value, "command", projectRoot) ?? string.Empty,
                Url = url,
                Gallery = Text(value, "gallery", projectRoot),
                Version = Text(value, "version", projectRoot),
                Transport = transport.ToLowerInvariant() switch
                {
                    "stdio" => "stdio",
                    "http" or "streamablehttp" or "streamable-http" => "streamableHttp",
                    "sse" => "sse",
                    _ => throw new IOException("An MCP transport was unsupported."),
                },
                Args = ReadStrings(value, "args", projectRoot),
                Environment = ReadMap(value, "env", projectRoot),
                Headers = ReadMap(value, "headers", projectRoot),
                DiscoverySourcePath = Path.GetFullPath(source),
                DiscoveryScope = scope,
                WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(source)),
            };
            if ((Text(value, "cwd", projectRoot) ?? Text(value, "working_directory", projectRoot)) is { } workingDirectory)
            {
                server.WorkingDirectory = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(source))!, workingDirectory));
            }
            if (server.Transport == "stdio" ? string.IsNullOrWhiteSpace(server.Command) :
                !Uri.TryCreate(server.Url, UriKind.Absolute, out var address) || address.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(address.UserInfo))
            {
                throw new IOException("An MCP executable or endpoint was invalid.");
            }
            var fingerprint = JsonSerializer.Serialize(new
            {
                server.Name,
                server.Command,
                server.Transport,
                server.Url,
                server.Args,
                server.WorkingDirectory,
                Environment = server.Environment.OrderBy(pair => pair.Key, StringComparer.Ordinal),
                Headers = server.Headers.OrderBy(pair => pair.Key, StringComparer.Ordinal),
                Source = _fileSystem.GetCanonicalPath(source),
                server.DiscoveryScope,
            });
            server.DiscoveryFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint)));
            servers[server.Name] = server;
            if (servers.Count > 200)
            {
                throw new IOException("MCP discovery exceeded its 200-server limit.");
            }
        }
    }

    private string? Text(JsonElement root, string key, string projectRoot)
    {
        if (!root.TryGetProperty(key, out var value))
        {
            return null;
        }
        if (value.ValueKind != JsonValueKind.String)
        {
            throw new IOException("MCP text fields must be strings.");
        }
        return Expand(value.GetString()!, projectRoot);
    }

    private List<string> ReadStrings(JsonElement root, string key, string projectRoot)
    {
        if (!root.TryGetProperty(key, out var values))
        {
            return [];
        }
        if (values.ValueKind != JsonValueKind.Array || values.GetArrayLength() > 200)
        {
            throw new IOException("MCP argument fields must be bounded arrays.");
        }
        return values.EnumerateArray().Select(value => value.ValueKind == JsonValueKind.String ? Expand(value.GetString()!, projectRoot) :
            throw new IOException("MCP arguments must be strings.")).ToList();
    }

    private Dictionary<string, string?> ReadMap(JsonElement root, string key, string projectRoot)
    {
        if (!root.TryGetProperty(key, out var values))
        {
            return new(StringComparer.OrdinalIgnoreCase);
        }
        if (values.ValueKind != JsonValueKind.Object)
        {
            throw new IOException("MCP environment/header fields must be objects.");
        }
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in values.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String || !result.TryAdd(property.Name, Expand(property.Value.GetString()!, projectRoot)))
            {
                throw new IOException("MCP environment/header values must be strings with distinct keys.");
            }
        }
        return result;
    }

    private string Expand(string value, string projectRoot) => Regex.Replace(value, @"\$\{([^}]+)\}", match =>
    {
        var parts = match.Groups[1].Value.Split(":-", 2, StringSplitOptions.None);
        if (parts[0] == "workspaceFolder")
        {
            return Path.GetFullPath(projectRoot);
        }
        if (parts[0].StartsWith("env:", StringComparison.Ordinal))
        {
            parts[0] = parts[0][4..];
        }
        return _environment.GetVariable(parts[0]) ?? (parts.Length == 2 ? parts[1] :
            throw new IOException("An MCP environment variable was missing; configure it before discovery."));
    }, RegexOptions.CultureInvariant);
}
