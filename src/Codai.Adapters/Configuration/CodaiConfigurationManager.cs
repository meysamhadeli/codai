using System.Globalization;
using System.Text.Json;
using Codai.Core.Abstractions;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace Codai.Configuration;

/// <summary>Persists enable, disable and remove operations for configured external integrations.</summary>
public sealed class CodaiConfigurationManager
{
    private static readonly string[] SupportedExtensions = [".json", ".yml", ".yaml"];
    private readonly IFileSystem _fileSystem;

    /// <summary>Creates a manager backed by the supplied safe file-system abstraction.</summary>
    public CodaiConfigurationManager(IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        _fileSystem = fileSystem;
    }

    /// <summary>Changes one configured MCP server or plugin path and writes only the config file.</summary>
    public async Task MutateAsync(
        string configurationFile,
        ConfigurationMutation mutation,
        string identifier,
        string baseDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationFile);
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);

        var filePath = ValidateConfigurationFile(configurationFile, baseDirectory);
        var text = await _fileSystem.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        ConfigurationDocument document;
        try
        {
            document = extension == ".json"
                ? ConfigurationDocument.ParseJson(text)
                : ConfigurationDocument.ParseYaml(text);
        }
        catch (Exception ex) when (ex is JsonException or YamlException or InvalidOperationException)
        {
            throw new CodaiConfigurationException($"Could not parse configuration file '{filePath}'.", ex);
        }

        var changed = mutation.Target switch
        {
            ConfigurationMutationTarget.Mcp => MutateMcp(document, mutation.Action, identifier),
            ConfigurationMutationTarget.Plugin => MutatePlugin(document, mutation.Action, identifier, baseDirectory),
            _ => throw new ArgumentOutOfRangeException(nameof(mutation)),
        };

        if (!changed)
        {
            throw new CodaiConfigurationException(
                $"No configured {mutation.Target.ToString().ToLowerInvariant()} entry matched '{identifier}'.");
        }

        await _fileSystem.WriteAllTextAtomicallyAsync(
            filePath,
            document.Serialize(extension == ".json"),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ConfiguredIntegration>> ListAsync(string configurationFile, ConfigurationMutationTarget target, string baseDirectory, CancellationToken cancellationToken = default)
    {
        var path = ValidateConfigurationFile(configurationFile, baseDirectory);
        var text = await _fileSystem.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        ConfigurationDocument document;
        try
        {
            document = Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase) ? ConfigurationDocument.ParseJson(text) : ConfigurationDocument.ParseYaml(text);
        }
        catch (Exception ex) when (ex is JsonException or YamlException or InvalidOperationException or InvalidCastException)
        {
            throw new CodaiConfigurationException("Could not parse the integration configuration.", ex);
        }
        List<ConfiguredIntegration> entries = [];
        var enabled = target == ConfigurationMutationTarget.Mcp ? document.GetArray("mcp", "servers") : document.GetArray("plugins");
        for (var index = 0; index < enabled.Count; index++)
        {
            var identifier = target == ConfigurationMutationTarget.Mcp ? enabled[index].GetString("name") : enabled[index].GetStringValue();
            if (!string.IsNullOrWhiteSpace(identifier))
            {
                entries.Add(new ConfiguredIntegration(identifier, !string.Equals(enabled[index].GetString("enabled"), "false", StringComparison.OrdinalIgnoreCase)));
            }
        }
        if (target == ConfigurationMutationTarget.Plugin)
        {
            var disabled = document.GetArray("disabled_plugins");
            for (var index = 0; index < disabled.Count; index++)
            {
                if (disabled[index].GetStringValue() is { Length: > 0 } identifier)
                {
                    entries.Add(new ConfiguredIntegration(identifier, false));
                }
            }
        }
        return entries;
    }

    public Task SetValueAsync(string configurationFile, string key, string value, string baseDirectory, CancellationToken cancellationToken = default) =>
        SetValuesAsync(configurationFile, new Dictionary<string, string> { [key] = value }, baseDirectory, cancellationToken);

    public async Task SetValuesAsync(string configurationFile, IReadOnlyDictionary<string, string> values, string baseDirectory, CancellationToken cancellationToken = default)
    {
        var parsed = values.ToDictionary(pair => pair.Key, pair => ParseValue(pair.Key, pair.Value), StringComparer.Ordinal);
        var filePath = ValidateConfigurationFile(configurationFile, baseDirectory, requireExisting: false);
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        var text = _fileSystem.FileExists(filePath)
            ? await _fileSystem.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false)
            : "{}";
        ConfigurationDocument document;
        try
        {
            document = extension == ".json" ? ConfigurationDocument.ParseJson(text) : ConfigurationDocument.ParseYaml(text);
        }
        catch (Exception ex) when (ex is JsonException or YamlException or InvalidOperationException or InvalidCastException)
        {
            throw new CodaiConfigurationException("Could not parse the configuration file; no changes were saved.", ex);
        }
        foreach (var (key, value) in parsed)
        {
            document.SetValue(key.Split('.'), value);
        }
        await _fileSystem.WriteAllTextAtomicallyAsync(filePath, document.Serialize(extension == ".json"), cancellationToken).ConfigureAwait(false);
    }

    private static object? ParseValue(string key, string value) => key switch
    {
        "theme" when value is "graphite" or "forest" or "ember" => value,
        "ai_provider_config.provider" or "ai_provider_config.model" when !string.IsNullOrWhiteSpace(value) && !value.Any(char.IsWhiteSpace) => value,
        "ai_provider_config.api_key" => value,
        "ai_provider_config.account" when value.Length == 0 || value.Length == 64 && value.All(char.IsAsciiHexDigit) => value,
        "ai_provider_config.base_url" when value.Length == 0 || Uri.TryCreate(value, UriKind.Absolute, out var endpoint) && endpoint.Scheme is "http" or "https" && endpoint.UserInfo.Length == 0 => value,
        "ai_provider_config.temperature" when float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var temperature) && float.IsFinite(temperature) && temperature is >= 0 and <= 2 => temperature,
        "ai_provider_config.stream" or "checkpoints_enabled" when bool.TryParse(value, out var enabled) => enabled,
        "ai_provider_config.max_tokens" or "budget.max_tokens" when int.TryParse(value, out var tokens) && tokens >= 0 => tokens,
        "checkpoint_retention" or "budget.max_tool_calls" when int.TryParse(value, out var count) && count > 0 => count,
        "budget.max_cost" when decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var cost) && cost >= 0 => cost,
        _ => throw new CodaiConfigurationException("Unsupported setting or invalid value."),
    };

    private string ValidateConfigurationFile(string path, string baseDirectory, bool requireExisting = true)
    {
        var root = Path.GetFullPath(baseDirectory);
        var fullPath = Path.GetFullPath(path, root);
        var extension = Path.GetExtension(fullPath).ToLowerInvariant();
        if (!SupportedExtensions.Contains(extension, StringComparer.Ordinal) ||
            !string.Equals(Path.GetFileNameWithoutExtension(fullPath), CodaiOptions.FileName, StringComparison.OrdinalIgnoreCase))
        {
            throw new CodaiConfigurationException(
                "Configuration management only writes an existing codai-config.yml, codai-config.yaml or codai-config.json file.");
        }

        if (requireExisting && !_fileSystem.FileExists(fullPath))
        {
            throw new CodaiConfigurationException($"Configuration file '{fullPath}' was not found.");
        }

        return fullPath;
    }

    private static bool MutateMcp(ConfigurationDocument document, ConfigurationMutationAction action, string name)
    {
        var servers = document.GetArray("mcp", "servers");
        var index = servers.FindIndex(node => string.Equals(node.GetString("name"), name, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return false;
        }

        if (action == ConfigurationMutationAction.Remove)
        {
            servers.RemoveAt(index);
        }
        else
        {
            servers[index].SetBoolean("enabled", action == ConfigurationMutationAction.Enable);
        }

        return true;
    }

    private static bool MutatePlugin(
        ConfigurationDocument document,
        ConfigurationMutationAction action,
        string path,
        string baseDirectory)
    {
        ValidatePluginPath(path, baseDirectory);
        var enabled = document.GetArray("plugins");
        var disabled = document.GetArray("disabled_plugins");
        var configured = enabled.FindIndex(node => PathsEqual(node.GetStringValue(), path, baseDirectory));
        var withheld = disabled.FindIndex(node => PathsEqual(node.GetStringValue(), path, baseDirectory));

        if (configured < 0 && withheld < 0)
        {
            return false;
        }

        if (action == ConfigurationMutationAction.Remove)
        {
            if (configured >= 0) enabled.RemoveAt(configured);
            if (withheld >= 0) disabled.RemoveAt(withheld);
            return true;
        }

        if (action == ConfigurationMutationAction.Disable)
        {
            if (configured >= 0)
            {
                disabled.Add(enabled[configured]);
                enabled.RemoveAt(configured);
            }

            return true;
        }

        if (withheld >= 0)
        {
            enabled.Add(disabled[withheld]);
            disabled.RemoveAt(withheld);
        }

        return true;
    }

    private static string ValidatePluginPath(string path, string baseDirectory)
    {
        if (path.IndexOfAny(['*', '?']) >= 0 ||
            !string.Equals(Path.GetExtension(path), ".dll", StringComparison.OrdinalIgnoreCase))
        {
            throw new CodaiConfigurationException("Plugin path must be a concrete .dll path; no plugin file is removed or executed.");
        }

        try
        {
            return Path.GetFullPath(path, Path.GetFullPath(baseDirectory));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            throw new CodaiConfigurationException("Plugin path is not valid.", ex);
        }
    }

    private static bool PathsEqual(string? left, string right, string baseDirectory) =>
        left is not null && string.Equals(
            ValidatePluginPath(left, baseDirectory),
            ValidatePluginPath(right, baseDirectory),
            StringComparison.OrdinalIgnoreCase);
}

/// <summary>One persistent external-integration configuration operation.</summary>
public sealed record ConfiguredIntegration(string Identifier, bool Enabled);

public readonly record struct ConfigurationMutation(
    ConfigurationMutationTarget Target,
    ConfigurationMutationAction Action);

/// <summary>External integration collection to mutate.</summary>
public enum ConfigurationMutationTarget
{
    Mcp,
    Plugin,
}

/// <summary>Persistent operation to apply.</summary>
public enum ConfigurationMutationAction
{
    Enable,
    Disable,
    Remove,
}

internal sealed class ConfigurationDocument
{
    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

    private readonly Dictionary<string, object?> _root;

    private ConfigurationDocument(Dictionary<string, object?> root) => _root = root;

    public static ConfigurationDocument ParseJson(string text)
    {
        using var json = JsonDocument.Parse(text);
        return new ConfigurationDocument((Dictionary<string, object?>)ConvertJson(json.RootElement)!);
    }

    public static ConfigurationDocument ParseYaml(string text)
    {
        var raw = new DeserializerBuilder().Build().Deserialize<Dictionary<object, object?>>(text);
        return new ConfigurationDocument((Dictionary<string, object?>?)NormalizeYaml(raw) ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase));
    }

    public ConfigurationArray GetArray(params string[] path)
    {
        object? current = _root;
        foreach (var segment in path)
        {
            if (current is not Dictionary<string, object?> dictionary || !TryGet(dictionary, segment, out current))
            {
                current = null;
                break;
            }
        }

        if (current is not List<object?> list)
        {
            list = [];
            SetValue(path, list);
        }

        return new ConfigurationArray(list);
    }

    public string Serialize(bool json) => json
        ? JsonSerializer.Serialize(_root, IndentedJson)
        : new SerializerBuilder().Build().Serialize(_root);

    public void SetValue(IReadOnlyList<string> path, object? value)
    {
        Dictionary<string, object?> current = _root;
        for (var index = 0; index < path.Count - 1; index++)
        {
            if (!TryGet(current, path[index], out var child) || child is not Dictionary<string, object?> childDictionary)
            {
                childDictionary = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                current[path[index]] = childDictionary;
            }

            current = childDictionary;
        }

        current[path[^1]] = value;
    }

    private static bool TryGet(Dictionary<string, object?> dictionary, string key, out object? value)
    {
        var pair = dictionary.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase));
        value = pair.Value;
        return pair.Key is not null;
    }

    private static object? ConvertJson(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().ToDictionary(
            property => property.Name,
            property => ConvertJson(property.Value),
            StringComparer.OrdinalIgnoreCase),
        JsonValueKind.Array => element.EnumerateArray().Select(ConvertJson).ToList(),
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out var integer) ? integer : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };

    private static object? NormalizeYaml(object? value) => value switch
    {
        IDictionary<object, object?> map => map.ToDictionary(
            pair => pair.Key.ToString() ?? string.Empty,
            pair => NormalizeYaml(pair.Value),
            StringComparer.OrdinalIgnoreCase),
        IDictionary<string, object?> map => map.ToDictionary(
            pair => pair.Key,
            pair => NormalizeYaml(pair.Value),
            StringComparer.OrdinalIgnoreCase),
        IEnumerable<object?> list => list.Select(NormalizeYaml).ToList(),
        _ => value,
    };
}

internal sealed class ConfigurationArray
{
    private readonly List<object?> _values;

    public ConfigurationArray(List<object?> values) => _values = values;

    public ConfigurationNode this[int index] => new(_values[index]);

    public int Count => _values.Count;

    public int FindIndex(Func<ConfigurationNode, bool> predicate) =>
        _values.FindIndex(value => predicate(new ConfigurationNode(value)));

    public void Add(ConfigurationNode node) => _values.Add(node.Value);

    public void RemoveAt(int index) => _values.RemoveAt(index);
}

internal sealed class ConfigurationNode
{
    private readonly object? _value;

    public ConfigurationNode(object? value) => _value = value;

    public object? Value => _value;

    public string? GetString(string key) =>
        _value is Dictionary<string, object?> dictionary
            ? dictionary.FirstOrDefault(pair => string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)).Value?.ToString()
            : null;

    public string? GetStringValue() => _value?.ToString();

    public void SetBoolean(string key, bool value)
    {
        if (_value is Dictionary<string, object?> dictionary)
        {
            var existing = dictionary.Keys.FirstOrDefault(item => string.Equals(item, key, StringComparison.OrdinalIgnoreCase));
            dictionary[existing ?? key] = value;
        }
    }
}
