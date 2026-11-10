using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Codai.Core.Abstractions;
using Codai.Core.IO;

namespace Codai.Core.Customization;

/// <summary>
/// Loads skills from Codai and shared agent project/global skill directories.
/// </summary>
/// <remarks>
/// The folder name is what identifies a skill, so a skill cannot point at a file outside
/// the configured skill roots and a name that is not usable as a folder name is rejected rather
/// than resolved.
/// </remarks>
public sealed class SkillCatalog : ISkillCatalog
{
    /// <summary>Upper bound on how many skills a project may define.</summary>
    public const int MaxSkills = 200;

    private readonly IFileSystem _fileSystem;
    private readonly string? _globalConfigurationDirectory;
    private readonly string? _userHomeDirectory;
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _visitedDirectories = new(PathComparer);

    /// <summary>Creates a catalog with optional global and shared-home discovery.</summary>
    /// <param name="fileSystem">Filesystem used for every skill read.</param>
    /// <param name="globalConfigurationDirectory">Opt-in global Codai directory; null restricts discovery to project roots.</param>
    /// <param name="userHomeDirectory">Shared agent home; otherwise inferred only from a global directory named .codai.</param>
    public SkillCatalog(IFileSystem fileSystem, string? globalConfigurationDirectory = null, string? userHomeDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        _fileSystem = fileSystem;
        _globalConfigurationDirectory = globalConfigurationDirectory is null
            ? null : Path.GetFullPath(globalConfigurationDirectory);
        _userHomeDirectory = userHomeDirectory is null ? null : Path.GetFullPath(userHomeDirectory);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SkillDefinition>> LoadAsync(
        string rootDirectory,
        CancellationToken cancellationToken = default) => (await LoadCoreAsync(rootDirectory, false, cancellationToken).ConfigureAwait(false))
            .Where(skill => !skill.DisableModelInvocation).ToArray();

    /// <inheritdoc />
    public Task<IReadOnlyList<SkillDefinition>> LoadAllAsync(string rootDirectory, CancellationToken cancellationToken = default) =>
        LoadCoreAsync(rootDirectory, true, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<SkillDefinition>> LoadForFilesAsync(string rootDirectory, IReadOnlyList<string> referencedFiles,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(referencedFiles);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootDirectory));
        var visited = _visitedDirectories.GetOrAdd(root, _ => new ConcurrentDictionary<string, byte>(PathComparer));
        foreach (var file in referencedFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(_fileSystem.GetCanonicalPath(root), _fileSystem.GetCanonicalPath(Path.GetFullPath(Path.Combine(root, file))));
            if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith("../", StringComparison.Ordinal) || relative.StartsWith("..\\", StringComparison.Ordinal))
            {
                continue;
            }
            var directory = Path.GetDirectoryName(Path.GetFullPath(Path.Combine(root, file)));
            while (directory is not null && !PathComparer.Equals(directory, root) && visited.Count < 128)
            {
                visited.TryAdd(directory, 0);
                directory = Path.GetDirectoryName(directory);
            }
        }
        return LoadAsync(rootDirectory, cancellationToken);
    }

    private async Task<IReadOnlyList<SkillDefinition>> LoadCoreAsync(
        string rootDirectory, bool includeDisabled, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        cancellationToken.ThrowIfCancellationRequested();

        var homeDirectory = _userHomeDirectory ?? (string.Equals(Path.GetFileName(_globalConfigurationDirectory), ".codai", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(_globalConfigurationDirectory) : null);
        var metadata = await DiscoveryDirectories.LoadMetadataAsync(rootDirectory, _fileSystem, homeDirectory, cancellationToken).ConfigureAwait(false);
        var rootKey = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootDirectory));
        var nestedDirectories = _visitedDirectories.TryGetValue(rootKey, out var visited)
            ? visited.Keys.OrderByDescending(directory => directory.Length).ThenBy(directory => directory, PathComparer)
            : Enumerable.Empty<string>();
        var projectDirectories = nestedDirectories.SelectMany(directory => new[]
        {
            Path.Combine(directory, ".codai", "skills"), Path.Combine(directory, ".agents", "skills"),
            Path.Combine(directory, ".claude", "skills"), Path.Combine(directory, ".opencode", "skills"),
            Path.Combine(directory, ".github", "skills"),
        }).Concat(ProjectSkillDirectories(rootDirectory)).Concat(metadata.AdditionalDirectories.SelectMany(directory => new[]
        {
            Path.Combine(directory, ".codai", "skills"), Path.Combine(directory, ".agents", "skills"),
            Path.Combine(directory, ".claude", "skills"), Path.Combine(directory, ".opencode", "skills"),
            Path.Combine(directory, ".github", "skills"),
        })).Concat(metadata.SkillDirectories).DistinctBy(_fileSystem.GetCanonicalPath, PathComparer).ToArray();
        var project = new List<SkillDefinition>();
        foreach (var directory in projectDirectories)
        {
            project.AddRange(await LoadDirectoryAsync(rootDirectory, directory, false, cancellationToken).ConfigureAwait(false));
        }

        var disabled = await LoadDisabledNamesAsync(
            Path.Combine(rootDirectory, CodaiPaths.Folder), cancellationToken).ConfigureAwait(false);
        var result = project.DistinctBy(skill => skill.Name, StringComparer.OrdinalIgnoreCase)
            .Where(skill => includeDisabled || !disabled.Contains(skill.Name))
            .Select(skill => skill with { IsEnabled = !disabled.Contains(skill.Name) }).Take(MaxSkills)
            .OrderBy(skill => skill.Name, StringComparer.Ordinal).ToList();
        if (_globalConfigurationDirectory is null || result.Count >= MaxSkills)
        {
            return result;
        }

        var projectPaths = projectDirectories
            .Select(_fileSystem.GetCanonicalPath)
            .ToHashSet(PathComparer);
        var globalDirectories = new List<string> { Path.Combine(_globalConfigurationDirectory, "skills") };
        var home = _userHomeDirectory ?? (string.Equals(Path.GetFileName(_globalConfigurationDirectory), ".codai", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(_globalConfigurationDirectory) : null);
        if (home is not null)
        {
            globalDirectories.Add(Path.Combine(home, ".agents", "skills"));
            globalDirectories.Add(Path.Combine(home, ".claude", "skills"));
            globalDirectories.Add(Path.Combine(home, ".config", "opencode", "skills"));
            globalDirectories.Add(Path.Combine(home, ".copilot", "skills"));
        }
        var global = new List<SkillDefinition>();
        foreach (var directory in globalDirectories.DistinctBy(_fileSystem.GetCanonicalPath, PathComparer))
        {
            if (projectPaths.Contains(_fileSystem.GetCanonicalPath(directory)))
            {
                continue;
            }

            global.AddRange(await LoadDirectoryAsync(_globalConfigurationDirectory, directory, true, cancellationToken).ConfigureAwait(false));
        }

        var names = result.Select(skill => skill.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return [.. result.Concat(MergeByName(global)
                .Where(skill => (includeDisabled || !disabled.Contains(skill.Name)) && !names.Contains(skill.Name))
                .Select(skill => skill with { IsEnabled = !disabled.Contains(skill.Name) })
                .Take(MaxSkills - result.Count))
            .OrderBy(skill => skill.Name, StringComparer.Ordinal)];
    }

    private async Task<HashSet<string>> LoadDisabledNamesAsync(
        string projectConfigurationDirectory,
        CancellationToken cancellationToken)
    {
        var disabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (_globalConfigurationDirectory is not null)
        {
            var projectRoot = Path.GetDirectoryName(projectConfigurationDirectory)!;
            var state = await MigrateSettingsAsync(projectRoot, cancellationToken).ConfigureAwait(false);
            ApplySelection(GetSelection(state["skillDefaults"]), disabled, false);
            ApplySelection(GetSelection(GetSkillProjects(state)[UserCustomizationSettings.ProjectKey(_fileSystem, projectRoot)]), disabled, true);
            return disabled;
        }

        await ReadDisabledNamesAsync(
            Path.Combine(projectConfigurationDirectory, CodaiPaths.SkillsConfigurationFileName),
            disabled,
            cancellationToken, applyEnabled: true).ConfigureAwait(false);

        return disabled;
    }

    private async Task<JsonObject> MigrateSettingsAsync(string projectRoot, CancellationToken cancellationToken)
    {
        var globalFile = Path.Combine(_globalConfigurationDirectory!, CodaiPaths.SkillsConfigurationFileName);
        var projectFile = Path.Combine(projectRoot, CodaiPaths.Folder, CodaiPaths.SkillsConfigurationFileName);
        var global = await ReadLegacySettingsAsync(globalFile, cancellationToken).ConfigureAwait(false);
        var project = await ReadLegacySettingsAsync(projectFile, cancellationToken).ConfigureAwait(false);
        if (global is not null || project is not null)
        {
            await UserCustomizationSettings.UpdateAsync(_fileSystem, _globalConfigurationDirectory!, state =>
            {
                GetSelection(state["skillDefaults"]);
                GetSelection(GetSkillProjects(state)[UserCustomizationSettings.ProjectKey(_fileSystem, projectRoot)]);
                if (global is not null && state["skillDefaults"] is null)
                {
                    state["skillDefaults"] = global.DeepClone();
                }
                if (project is not null)
                {
                    var projects = GetSkillProjects(state);
                    var key = UserCustomizationSettings.ProjectKey(_fileSystem, projectRoot);
                    if (projects[key] is null)
                    {
                        projects[key] = project.DeepClone();
                    }
                    if (state["skills"] is null)
                    {
                        state["skills"] = projects;
                    }
                }
            }, cancellationToken).ConfigureAwait(false);
            if (global is not null)
            {
                _fileSystem.DeleteFile(globalFile);
            }
            if (project is not null && _fileSystem.FileExists(projectFile))
            {
                _fileSystem.DeleteFile(projectFile);
            }
        }
        return await UserCustomizationSettings.ReadAsync(_fileSystem, _globalConfigurationDirectory!, cancellationToken).ConfigureAwait(false);
    }

    private async Task<JsonObject?> ReadLegacySettingsAsync(string file, CancellationToken cancellationToken)
    {
        if (!_fileSystem.FileExists(file))
        {
            return null;
        }
        try
        {
            var document = JsonNode.Parse(await _fileSystem.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false)) as JsonObject;
            return document is not null && document["enabled"] is null or JsonArray && document["disabled"] is null or JsonArray
                ? document : null;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static JsonObject GetSkillProjects(JsonObject state) => state["skills"] switch
    {
        null => new JsonObject(),
        JsonObject projects => projects,
        _ => throw new IOException("User skill selections must contain a JSON object."),
    };

    private static JsonObject? GetSelection(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }
        if (node is not JsonObject selection || selection["enabled"] is not null and not JsonArray || selection["disabled"] is not null and not JsonArray)
        {
            throw new IOException("User skill selections must contain enabled and disabled arrays.");
        }
        return selection;
    }

    private static void ApplySelection(JsonObject? document, HashSet<string> disabled, bool applyEnabled)
    {
        if (document is null)
        {
            return;
        }
        foreach (var key in applyEnabled ? new[] { "enabled", "disabled" } : new[] { "disabled" })
        {
            if (document[key] is not JsonArray values)
            {
                continue;
            }
            foreach (var value in values.OfType<JsonValue>())
            {
                if (!value.TryGetValue<string>(out var name) || name is null)
                {
                    continue;
                }
                name = name.Trim().ToLowerInvariant();
                if (!CustomizationNames.IsValid(name))
                {
                    continue;
                }
                if (key == "enabled")
                {
                    disabled.Remove(name);
                }
                else
                {
                    disabled.Add(name);
                }
            }
        }
    }

    private async Task ReadDisabledNamesAsync(
        string file,
        HashSet<string> disabled,
        CancellationToken cancellationToken,
        bool applyEnabled = false)
    {
        if (!_fileSystem.FileExists(file))
        {
            return;
        }

        try
        {
            var json = await _fileSystem.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return;
            }
            if (applyEnabled && document.RootElement.TryGetProperty("enabled", out var enabled) && enabled.ValueKind == JsonValueKind.Array)
            {
                foreach (var value in enabled.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.String))
                {
                    disabled.Remove(value.GetString()!.Trim());
                }
            }
            if (!document.RootElement.TryGetProperty("disabled", out var values) ||
                values.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var value in values.EnumerateArray())
            {
                if (value.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var name = value.GetString()!.Trim().ToLowerInvariant();
                if (CustomizationNames.IsValid(name))
                {
                    disabled.Add(name);
                }
            }
        }
        catch (JsonException)
        {
            // Invalid optional settings must not prevent valid skills from loading.
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <inheritdoc />
    public async Task SaveEnabledAsync(string rootDirectory, IReadOnlyList<string> enabledNames, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(enabledNames);
        var skills = await LoadAllAsync(rootDirectory, cancellationToken).ConfigureAwait(false);
        var known = skills.Select(skill => skill.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var enabled = enabledNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (enabled.Any(name => !known.Contains(name)))
        {
            throw new ArgumentException("Only discovered skills can be enabled.", nameof(enabledNames));
        }
        if (skills.All(skill => skill.IsEnabled == enabled.Contains(skill.Name)))
        {
            return;
        }

        if (_globalConfigurationDirectory is not null)
        {
            await UserCustomizationSettings.UpdateAsync(_fileSystem, _globalConfigurationDirectory, state =>
            {
                var projects = GetSkillProjects(state);
                var key = UserCustomizationSettings.ProjectKey(_fileSystem, rootDirectory);
                var selection = GetSelection(projects[key]) ?? new JsonObject();
                UpdateSelection(selection, known, enabled);
                if (projects[key] is null)
                {
                    projects[key] = selection;
                }
                if (state["skills"] is null)
                {
                    state["skills"] = projects;
                }
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        var directory = Path.Combine(rootDirectory, CodaiPaths.Folder);
        var file = Path.Combine(directory, CodaiPaths.SkillsConfigurationFileName);
        JsonObject document;
        try
        {
            document = _fileSystem.FileExists(file)
                ? JsonNode.Parse(await _fileSystem.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false)) as JsonObject
                    ?? throw new IOException("Skill settings must contain a JSON object.")
                : new JsonObject();
        }
        catch (JsonException exception)
        {
            throw new IOException("Could not parse skill settings; no changes were saved.", exception);
        }
        UpdateSelection(document, known, enabled);
        _fileSystem.CreateDirectory(directory);
        await _fileSystem.WriteAllTextAtomicallyAsync(file, document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), cancellationToken)
            .ConfigureAwait(false);
    }

    private static void UpdateSelection(JsonObject document, HashSet<string> known, HashSet<string> enabled)
    {
        foreach (var key in new[] { "enabled", "disabled" })
        {
            if (document[key] is not null && document[key] is not JsonArray)
            {
                throw new IOException("Skill availability settings must contain arrays.");
            }
            var previous = (document[key] as JsonArray)?.OfType<JsonValue>()
                .Select(value => value.TryGetValue<string>(out var name) ? name : null)
                .Where(name => name is not null && !known.Contains(name)).Cast<string>() ?? [];
            var selected = key == "enabled" ? known.Where(enabled.Contains) : known.Where(name => !enabled.Contains(name));
            document[key] = new JsonArray(previous.Concat(selected).Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.Ordinal).Select(name => (JsonNode?)JsonValue.Create(name)).ToArray());
        }
    }

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private IReadOnlyList<string> ProjectSkillDirectories(string rootDirectory)
    {
        var home = _userHomeDirectory ?? (string.Equals(Path.GetFileName(_globalConfigurationDirectory), ".codai", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(_globalConfigurationDirectory) : null);
        return [.. DiscoveryDirectories.Ancestors(rootDirectory, _fileSystem, home).SelectMany(directory => new[]
        {
            Path.Combine(directory, ".codai", "skills"), Path.Combine(directory, ".agents", "skills"),
            Path.Combine(directory, ".claude", "skills"), Path.Combine(directory, ".opencode", "skills"),
            Path.Combine(directory, ".github", "skills"),
        })];
    }

    private static IReadOnlyList<SkillDefinition> MergeByName(IEnumerable<SkillDefinition> skills) =>
        [.. skills.GroupBy(skill => skill.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(skill => skill.Name, StringComparer.Ordinal)];

    private async Task<IReadOnlyList<SkillDefinition>> LoadDirectoryAsync(
        string rootDirectory, string directory, bool isGlobal, CancellationToken cancellationToken)
    {
        if (!_fileSystem.DirectoryExists(directory))
        {
            return [];
        }

        var skills = new List<SkillDefinition>();

        foreach (var folder in Enumerate(directory).Take(MaxSkills))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var folderName = Path.GetFileName(folder).ToLowerInvariant();

            if (!CustomizationNames.IsValid(folderName))
            {
                continue;
            }

            var skill = await TryReadAsync(rootDirectory, folder, folderName, isGlobal, cancellationToken)
                .ConfigureAwait(false);

            if (skill is not null)
            {
                skills.Add(skill);
            }
        }

        skills.Sort(static (left, right) => string.CompareOrdinal(left.Name, right.Name));

        return skills;
    }

    /// <inheritdoc />
    public async Task<SkillDefinition?> FindAsync(
        string rootDirectory,
        string name,
        CancellationToken cancellationToken = default)
    {
        // Names are advertised lower-cased, and a model or a user may still capitalise
        // one, so the request is normalised before it is validated: rejecting "Testing"
        // as an unusable name would be a confusing answer to a name that does exist.
        var requested = name?.Trim().ToLowerInvariant() ?? string.Empty;

        if (!CustomizationNames.IsValid(requested))
        {
            return null;
        }

        var skills = await LoadAsync(rootDirectory, cancellationToken).ConfigureAwait(false);

        return skills.FirstOrDefault(
            skill => string.Equals(skill.Name, requested, StringComparison.Ordinal));
    }

    private List<string> Enumerate(string directory)
    {
        try
        {
            return
            [
                .. _fileSystem
                    .EnumerateDirectories(directory)
                    .OrderBy(folder => folder, StringComparer.Ordinal),
            ];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private async Task<SkillDefinition?> TryReadAsync(
        string rootDirectory,
        string folder,
        string folderName,
        bool isGlobal,
        CancellationToken cancellationToken)
    {
        var file = Path.Combine(folder, CodaiPaths.SkillFileName);

        if (!_fileSystem.FileExists(file))
        {
            return null;
        }

        string text;

        try
        {
            text = await _fileSystem.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileNotFoundException)
        {
            return null;
        }

        var (fields, body) = FrontMatter.Parse(text);

        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        var description = fields.TryGetValue("description", out var declaredDescription)
            ? MarkdownSummary.Truncate(declaredDescription)
            : MarkdownSummary.FromBody(body);

        // The folder names the skill, because the folder is what read_skill resolves.
        // A "name" key in front matter is therefore informational only.
        return new SkillDefinition(
            folderName,
            description,
            body,
            isGlobal ? Path.GetFullPath(file).Replace('\\', '/') : Relative(rootDirectory, file))
        {
            IsGlobal = isGlobal,
            UserInvocable = !fields.TryGetValue("user-invocable", out var invocable) || !string.Equals(invocable, "false", StringComparison.OrdinalIgnoreCase),
            DisableModelInvocation = fields.TryGetValue("disable-model-invocation", out var disabled) && string.Equals(disabled, "true", StringComparison.OrdinalIgnoreCase),
            Paths = fields.TryGetValue("paths", out var paths) ? ProjectRuleCatalog.ParsePatterns(paths) : [],
        };
    }

    private static string Relative(string rootDirectory, string file) =>
        Path.GetRelativePath(rootDirectory, file).Replace('\\', '/');
}
