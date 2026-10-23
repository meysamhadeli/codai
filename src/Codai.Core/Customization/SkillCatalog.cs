using System.Text.Json;
using Codai.Core.Abstractions;
using Codai.Core.IO;

namespace Codai.Core.Customization;

/// <summary>
/// Loads skills from Codai project and global skill directories.
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

    /// <summary>Creates the catalog.</summary>
    public SkillCatalog(IFileSystem fileSystem, string? globalConfigurationDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        _fileSystem = fileSystem;
        _globalConfigurationDirectory = globalConfigurationDirectory is null
            ? null : Path.GetFullPath(globalConfigurationDirectory);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SkillDefinition>> LoadAsync(
        string rootDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        cancellationToken.ThrowIfCancellationRequested();

        var projectDirectories = ProjectSkillDirectories(rootDirectory);
        var project = new List<SkillDefinition>();
        foreach (var directory in projectDirectories)
        {
            project.AddRange(await LoadDirectoryAsync(rootDirectory, directory, false, cancellationToken).ConfigureAwait(false));
        }

        var disabled = await LoadDisabledNamesAsync(
            Path.Combine(rootDirectory, CodaiPaths.Folder), cancellationToken).ConfigureAwait(false);
        var result = MergeByName(project).Where(skill => !disabled.Contains(skill.Name)).ToList();
        if (_globalConfigurationDirectory is null || result.Count >= MaxSkills)
        {
            return result;
        }

        var projectPaths = projectDirectories
            .Select(Path.GetFullPath)
            .ToHashSet(PathComparer);
        var globalDirectories = new[] { Path.Combine(_globalConfigurationDirectory, "skills") };
        var global = new List<SkillDefinition>();
        foreach (var directory in globalDirectories.Distinct(PathComparer))
        {
            if (projectPaths.Contains(Path.GetFullPath(directory)))
            {
                continue;
            }

            global.AddRange(await LoadDirectoryAsync(_globalConfigurationDirectory, directory, true, cancellationToken).ConfigureAwait(false));
        }

        var names = result.Select(skill => skill.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return [.. result.Concat(MergeByName(global)
                .Where(skill => !disabled.Contains(skill.Name) && !names.Contains(skill.Name))
                .Take(MaxSkills - result.Count))
            .OrderBy(skill => skill.Name, StringComparer.Ordinal)];
    }

    private async Task<HashSet<string>> LoadDisabledNamesAsync(
        string projectConfigurationDirectory,
        CancellationToken cancellationToken)
    {
        var disabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await ReadDisabledNamesAsync(
            Path.Combine(projectConfigurationDirectory, CodaiPaths.SkillsConfigurationFileName),
            disabled,
            cancellationToken).ConfigureAwait(false);

        if (_globalConfigurationDirectory is not null)
        {
            await ReadDisabledNamesAsync(
                Path.Combine(_globalConfigurationDirectory, CodaiPaths.SkillsConfigurationFileName),
                disabled,
                cancellationToken).ConfigureAwait(false);
        }

        return disabled;
    }

    private async Task ReadDisabledNamesAsync(
        string file,
        HashSet<string> disabled,
        CancellationToken cancellationToken)
    {
        if (!_fileSystem.FileExists(file))
        {
            return;
        }

        try
        {
            var json = await _fileSystem.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);
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

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static IReadOnlyList<string> ProjectSkillDirectories(string rootDirectory) =>
        [Path.Combine(rootDirectory, ".codai", "skills")];

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
            isGlobal ? Path.GetFullPath(file).Replace('\\', '/') : Relative(rootDirectory, file)) { IsGlobal = isGlobal };
    }

    private static string Relative(string rootDirectory, string file) =>
        Path.GetRelativePath(rootDirectory, file).Replace('\\', '/');
}
