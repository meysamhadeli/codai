using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Codai.Core.Abstractions;

namespace Codai.Core.Customization;

/// <summary>Reads and atomically updates private customization state independently of runtime configuration.</summary>
public static class UserCustomizationSettings
{
    private static readonly ConditionalWeakTable<IFileSystem, ConcurrentDictionary<string, SemaphoreSlim>> Locks = new();
    private static readonly string[] SelectionSections = ["mcp", "skills", "skillDefaults"];

    /// <summary>Returns the Codai-managed selection file beneath the configured user directory.</summary>
    public static string GetFilePath(string directory) => Path.Combine(directory, "state", "customization.json");

    /// <summary>Returns the canonical, platform-normalized project identity used for private selections.</summary>
    public static string ProjectKey(IFileSystem fileSystem, string projectRoot)
    {
        var canonical = Path.TrimEndingDirectorySeparator(fileSystem.GetCanonicalPath(projectRoot)).Replace('\\', '/');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(OperatingSystem.IsWindows() ? canonical.ToUpperInvariant() : canonical)));
    }

    /// <summary>Reads private state, migrating legacy JSON selections after a successful atomic state save.</summary>
    public static async Task<JsonObject> ReadAsync(IFileSystem fileSystem, string directory, CancellationToken cancellationToken = default)
    {
        var gate = GetGate(fileSystem, directory);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ReadStateAsync(fileSystem, directory, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Serializes read-modify-write operations for the same filesystem and canonical state path.</summary>
    public static async Task UpdateAsync(IFileSystem fileSystem, string directory, Action<JsonObject> update,
        CancellationToken cancellationToken = default)
    {
        var gate = GetGate(fileSystem, directory);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await ReadStateAsync(fileSystem, directory, cancellationToken).ConfigureAwait(false);
            update(state);
            ValidateSelections(state);
            await WriteDocumentAsync(fileSystem, GetFilePath(directory), state, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private static SemaphoreSlim GetGate(IFileSystem fileSystem, string directory) =>
        Locks.GetValue(fileSystem, _ => new ConcurrentDictionary<string, SemaphoreSlim>(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal))
            .GetOrAdd(fileSystem.GetCanonicalPath(GetFilePath(directory)), _ => new SemaphoreSlim(1, 1));

    private static async Task<JsonObject> ReadStateAsync(IFileSystem fileSystem, string directory, CancellationToken cancellationToken)
    {
        var file = GetFilePath(directory);
        ValidateOwnedPath(fileSystem, directory, file);
        var state = await ReadDocumentAsync(fileSystem, file, cancellationToken).ConfigureAwait(false);
        ValidateSelections(state);
        var settingsFile = Path.Combine(directory, ".codai.json");
        var document = await ReadDocumentAsync(fileSystem, settingsFile, cancellationToken).ConfigureAwait(false);
        if (document["customization"] is not null and not JsonObject)
        {
            throw new IOException("User customization settings must contain a JSON object; no changes were saved.");
        }
        if (document["customization"] is not JsonObject previous || !SelectionSections.Any(previous.ContainsKey))
        {
            return state;
        }
        ValidateOwnedPath(fileSystem, directory, settingsFile);
        ValidateSelections(previous);
        foreach (var section in SelectionSections)
        {
            if (previous[section] is not JsonObject source)
            {
                continue;
            }
            if (state[section] is not JsonObject target)
            {
                state[section] = source.DeepClone();
                continue;
            }
            if (section == "skillDefaults")
            {
                continue;
            }
            foreach (var project in source)
            {
                if (!target.ContainsKey(project.Key))
                {
                    target[project.Key] = project.Value!.DeepClone();
                }
                else if (section == "mcp")
                {
                    var entries = target[project.Key]!.AsObject();
                    foreach (var entry in project.Value!.AsObject())
                    {
                        if (!entries.ContainsKey(entry.Key))
                        {
                            entries[entry.Key] = entry.Value!.DeepClone();
                        }
                    }
                }
            }
        }
        await WriteDocumentAsync(fileSystem, file, state, cancellationToken).ConfigureAwait(false);
        var current = await ReadDocumentAsync(fileSystem, settingsFile, cancellationToken).ConfigureAwait(false);
        if (current["customization"] is not JsonObject remaining ||
            SelectionSections.Any(section => !JsonNode.DeepEquals(remaining[section], previous[section])))
        {
            throw new IOException("User selections changed during migration; private state was saved but runtime settings were not changed.");
        }
        foreach (var section in SelectionSections)
        {
            remaining.Remove(section);
        }
        if (remaining.Count == 0)
        {
            current.Remove("customization");
        }
        await WriteDocumentAsync(fileSystem, settingsFile, current, cancellationToken).ConfigureAwait(false);
        return state;
    }

    private static void ValidateOwnedPath(IFileSystem fileSystem, string directory, string file)
    {
        var expected = Path.GetFullPath(Path.Combine(fileSystem.GetCanonicalPath(directory), Path.GetRelativePath(directory, file)));
        if (!string.Equals(fileSystem.GetCanonicalPath(file), expected,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            throw new IOException("Customization state and migration sources must remain inside their canonical user directory.");
        }
    }

    private static void ValidateSelections(JsonObject state)
    {
        foreach (var section in SelectionSections)
        {
            if (state[section] is not null and not JsonObject)
            {
                throw new IOException("Customization selection state was malformed; no changes were saved.");
            }
        }
        if (state["mcp"] is JsonObject approvals)
        {
            foreach (var project in approvals)
            {
                if (project.Value is not JsonObject entries)
                {
                    throw new IOException("MCP selection state was malformed; no changes were saved.");
                }
                foreach (var entry in entries)
                {
                    if (entry.Value is not JsonObject approval || approval["fingerprint"] is not JsonValue fingerprint ||
                        !fingerprint.TryGetValue<string>(out var hash) || hash is null || hash.Length != 64 ||
                        !hash.All(character => character is >= '0' and <= '9' or >= 'A' and <= 'F') ||
                        approval["enabled"] is not JsonValue enabled || !enabled.TryGetValue<bool>(out _))
                    {
                        throw new IOException("MCP selection state was malformed; no changes were saved.");
                    }
                }
            }
        }
        if (state["skills"] is JsonObject projects)
        {
            foreach (var project in projects)
            {
                ValidateSkillSelection(project.Value);
            }
        }
        if (state["skillDefaults"] is JsonObject defaults)
        {
            ValidateSkillSelection(defaults);
        }
    }

    private static void ValidateSkillSelection(JsonNode? selection)
    {
        if (selection is not JsonObject values || !ValidNames(values["enabled"]) || !ValidNames(values["disabled"]))
        {
            throw new IOException("Skill selection state was malformed; no changes were saved.");
        }
    }

    private static bool ValidNames(JsonNode? node) => node is null || node is JsonArray names &&
        names.All(name => name is JsonValue value && value.TryGetValue<string>(out _));

    private static async Task WriteDocumentAsync(IFileSystem fileSystem, string file, JsonObject document, CancellationToken cancellationToken)
    {
        var json = document.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        if (Encoding.UTF8.GetByteCount(json) > 65536)
        {
            throw new IOException("Customization storage exceeded the 64 KiB limit; no changes were saved.");
        }
        fileSystem.CreateDirectory(Path.GetDirectoryName(file)!);
        await fileSystem.WriteAllTextAtomicallyAsync(file, json, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<JsonObject> ReadDocumentAsync(IFileSystem fileSystem, string file, CancellationToken cancellationToken)
    {
        if (!fileSystem.FileExists(file))
        {
            return new JsonObject();
        }
        if (fileSystem.GetFileSize(file) > 65536)
        {
            throw new IOException("User settings exceeded the 64 KiB limit.");
        }
        try
        {
            var document = JsonNode.Parse(await fileSystem.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false),
                documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip, MaxDepth = 32 })
                as JsonObject ?? throw new IOException("User settings must contain a JSON object.");
            return document;
        }
        catch (JsonException exception)
        {
            throw new IOException("Could not parse user settings; no changes were saved.", exception);
        }
    }
}
