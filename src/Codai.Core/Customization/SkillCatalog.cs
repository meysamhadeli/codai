using Codai.Core.Abstractions;
using Codai.Core.IO;

namespace Codai.Core.Customization;

/// <summary>
/// Loads skills from <c>.codai/skills/&lt;name&gt;/SKILL.md</c>.
/// </summary>
/// <remarks>
/// The folder name is what identifies a skill, so a skill cannot point at a file outside
/// <c>.codai/skills</c> and a name that is not usable as a folder name is rejected rather
/// than resolved.
/// </remarks>
public sealed class SkillCatalog : ISkillCatalog
{
    /// <summary>Upper bound on how many skills a project may define.</summary>
    public const int MaxSkills = 200;

    private readonly IFileSystem _fileSystem;

    /// <summary>Creates the catalog.</summary>
    public SkillCatalog(IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        _fileSystem = fileSystem;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SkillDefinition>> LoadAsync(
        string rootDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);

        var directory = Path.Combine(
            rootDirectory,
            CodaiPaths.ToPlatformPath(CodaiPaths.SkillsDirectory));

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

            var skill = await TryReadAsync(rootDirectory, folder, folderName, cancellationToken)
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
            Relative(rootDirectory, file));
    }

    private static string Relative(string rootDirectory, string file) =>
        Path.GetRelativePath(rootDirectory, file).Replace('\\', '/');
}
