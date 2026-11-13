using System.Text.Json;
using System.Text.Json.Nodes;
using Codai.Core.Customization;

namespace Codai.Tools.Plugins;

internal static class PluginBundleManifest
{
    internal static async Task WriteReceiptAsync(JsonElement manifest, string bundle, string destination, CancellationToken cancellationToken)
    {
        var name = manifest.GetProperty("name").GetString()!;
        var skills = Paths(manifest, "skills", bundle, "skills");
        var commands = Paths(manifest, "commands", bundle, "commands");
        var hooks = new JsonArray();
        foreach (var file in Paths(manifest, "hooks", bundle, "hooks/hooks.json", "hooks.json", "com.github.copilot/hooks/hooks.json"))
        {
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(bundle, file), cancellationToken).ConfigureAwait(false));
            AddHooks(document.RootElement, hooks, name, destination);
        }
        if (manifest.TryGetProperty("hooks", out var inlineHooks) && inlineHooks.ValueKind == JsonValueKind.Object)
            AddHooks(inlineHooks, hooks, name, destination);
        var hookFiles = new List<string>();
        if (hooks.Count > 0)
        {
            hookFiles.Add("codai-hooks.json");
            await File.WriteAllTextAsync(Path.Combine(bundle, hookFiles[0]), new JsonObject { ["hooks"] = hooks }.ToJsonString(), cancellationToken).ConfigureAwait(false);
        }
        var servers = new JsonObject();
        foreach (var file in Paths(manifest, "mcpServers", bundle, ".mcp.json", "mcp.json"))
        {
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(bundle, file), cancellationToken).ConfigureAwait(false));
            AddServers(document.RootElement, servers, name, destination);
        }
        if (manifest.TryGetProperty("mcpServers", out var inline) && inline.ValueKind == JsonValueKind.Object)
            AddServers(inline, servers, name, destination);
        var mcp = new List<string>();
        if (servers.Count > 0)
        {
            mcp.Add("codai-mcp.json");
            await File.WriteAllTextAsync(Path.Combine(bundle, mcp[0]), new JsonObject { ["mcpServers"] = servers }.ToJsonString(), cancellationToken).ConfigureAwait(false);
        }
        await File.WriteAllTextAsync(Path.Combine(bundle, InstalledPluginCatalog.ReceiptFileName), JsonSerializer.Serialize(new
        {
            name, enabled = true, skills, commands, mcp, hooks = hookFiles,
        }), cancellationToken).ConfigureAwait(false);
        foreach (var file in Directory.EnumerateFiles(bundle, "*.md", SearchOption.AllDirectories))
        {
            var text = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
            var expanded = Expand(text, destination.Replace('\\', '/'));
            if (text != expanded) await File.WriteAllTextAsync(file, expanded, cancellationToken).ConfigureAwait(false);
        }
    }

    private static List<string> Paths(JsonElement manifest, string key, string bundle, params string[] defaults)
    {
        var paths = defaults.Where(path => File.Exists(Path.Combine(bundle, path)) || Directory.Exists(Path.Combine(bundle, path))).ToList();
        if (manifest.TryGetProperty(key, out var value))
        {
            if (value.ValueKind == JsonValueKind.String) paths.Add(value.GetString()!);
            else if (value.ValueKind == JsonValueKind.Array) paths.AddRange(value.EnumerateArray().Select(item => item.GetString()!));
            else if (value.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Plugin contribution paths have an unsupported format.");
        }
        return paths.Distinct(StringComparer.Ordinal).Select(path =>
        {
            var full = Path.GetFullPath(Path.Combine(bundle, path));
            if (!full.StartsWith(Path.TrimEndingDirectorySeparator(bundle) + Path.DirectorySeparatorChar,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                throw new InvalidDataException("Plugin contribution path escapes its bundle.");
            }
            if (!File.Exists(full) && !Directory.Exists(full)) throw new InvalidDataException("A declared plugin contribution was not found.");
            return Path.GetRelativePath(bundle, full).Replace('\\', '/');
        }).ToList();
    }

    private static void AddServers(JsonElement root, JsonObject servers, string name, string destination)
    {
        if (root.TryGetProperty("mcpServers", out var nested)) root = nested;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Plugin MCP definitions must be a named object.");
        foreach (var server in root.EnumerateObject())
        {
            var definition = JsonNode.Parse(server.Value.GetRawText()) as JsonObject ?? throw new InvalidDataException("Plugin MCP server is invalid.");
            ExpandRoot(definition, destination);
            if (definition["url"] is JsonValue endpoint && endpoint.TryGetValue<string>(out var address) &&
                Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Host == "mcp.context7.com")
            {
                var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
                if (query["client"] is "claude-code-plugin" or "copilot-plugin")
                {
                    query.Remove("client");
                    definition["url"] = new UriBuilder(uri) { Query = query.ToString() }.Uri.AbsoluteUri;
                }
            }
            if (definition["type"]?.GetValue<string>() == "local") definition["type"] = "stdio";
            if (definition["type"]?.GetValue<string>() == "http") definition["type"] = "streamableHttp";
            definition["cwd"] ??= destination;
            servers[name + "/" + server.Name] = definition;
        }
    }

    private static void AddHooks(JsonElement root, JsonArray output, string name, string destination)
    {
        if (root.TryGetProperty("hooks", out var nested)) root = nested;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Plugin hooks must be an event map.");
        foreach (var entry in root.EnumerateObject())
        {
            var eventName = entry.Name.ToLowerInvariant() switch
            {
                "sessionstart" => "TaskStarted", "userpromptsubmit" or "userpromptsubmitted" => "UserPromptSubmitted",
                "pretooluse" => "PreToolUse", "posttooluse" => "PostToolUse", "precompact" => "PreCompact",
                "stop" or "sessionend" => "TaskCompleted", "erroroccurred" => "TaskFailed",
                _ => throw new InvalidDataException($"Plugin hook event '{entry.Name}' is not supported."),
            };
            foreach (var group in entry.Value.EnumerateArray())
            {
                var definitions = group.TryGetProperty("hooks", out var children) ? children.EnumerateArray().ToArray() : [group];
                foreach (var hook in definitions)
                {
                    if (hook.TryGetProperty("type", out var type) && type.GetString() != "command" ||
                        hook.TryGetProperty("async", out var asynchronous) && asynchronous.ValueKind == JsonValueKind.True)
                    {
                        throw new InvalidDataException("Only synchronous command hooks are supported.");
                    }
                    var command = hook.TryGetProperty("command", out var declared) ? declared.GetString() :
                        hook.TryGetProperty(OperatingSystem.IsWindows() ? "powershell" : "bash", out declared) ? declared.GetString() : null;
                    if (string.IsNullOrWhiteSpace(command)) throw new InvalidDataException("Plugin command hook has no executable command for this platform.");
                    output.Add(new JsonObject
                    {
                        ["name"] = name + "/" + entry.Name + "/" + output.Count, ["event"] = eventName,
                        ["command"] = Expand(command, destination.Replace('\\', '/')), ["workingDirectory"] = destination,
                        ["inputFormat"] = "external", ["externalEvent"] = entry.Name,
                        ["matcher"] = eventName is "PreToolUse" or "PostToolUse" && group.TryGetProperty("matcher", out var matcher) ? matcher.GetString() : null,
                        ["timeoutSeconds"] = hook.TryGetProperty("timeout", out var timeout) ? timeout.GetInt32() :
                            hook.TryGetProperty("timeoutSec", out timeout) ? timeout.GetInt32() : 10,
                    });
                    if (output.Count > 32) throw new InvalidDataException("At most 32 plugin hooks are supported.");
                }
            }
        }
    }

    private static void ExpandRoot(JsonNode node, string destination)
    {
        if (node is JsonObject obj)
        {
            foreach (var key in obj.Select(pair => pair.Key).ToArray())
            {
                if (obj[key] is JsonValue value && value.TryGetValue<string>(out var text)) obj[key] = Expand(text, destination);
                else if (obj[key] is { } child) ExpandRoot(child, destination);
            }
        }
        else if (node is JsonArray array)
        {
            for (var index = 0; index < array.Count; index++)
            {
                if (array[index] is JsonValue value && value.TryGetValue<string>(out var text)) array[index] = Expand(text, destination);
                else if (array[index] is { } child) ExpandRoot(child, destination);
            }
        }
    }

    private static string Expand(string text, string destination) => text.Replace("${CLAUDE_PLUGIN_ROOT}", destination, StringComparison.Ordinal)
        .Replace("${COPILOT_PLUGIN_ROOT}", destination, StringComparison.Ordinal).Replace("${PLUGIN_ROOT}", destination, StringComparison.Ordinal);
}