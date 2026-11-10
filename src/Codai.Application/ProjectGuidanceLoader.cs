using Codai.Core.Abstractions;
using Codai.Core.Customization;
using Codai.Core.IO;

namespace Codai.Application;

/// <summary>
/// Loads the initial instruction snapshot, skills, rules, and project memory.
/// </summary>
/// <remarks>
/// Guidance is loaded lazily before the first model request. Instruction discovery is
/// refreshed separately for each request and relevant file tool; other catalogs may be cached.
/// </remarks>
public sealed class ProjectGuidanceLoader
{
    private readonly SessionOptions _options;
    private readonly IProjectInstructionsProvider _instructionsProvider;
    private readonly ISkillCatalog _skillCatalog;
    private readonly IProjectRuleCatalog _ruleCatalog;
    private readonly IProjectMemoryCatalog _memoryCatalog;

    /// <summary>Creates the loader.</summary>
    public ProjectGuidanceLoader(
        SessionOptions options,
        IProjectInstructionsProvider instructionsProvider,
        ISkillCatalog skillCatalog,
        IProjectRuleCatalog ruleCatalog,
        IProjectMemoryCatalog? memoryCatalog = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(instructionsProvider);
        ArgumentNullException.ThrowIfNull(skillCatalog);
        ArgumentNullException.ThrowIfNull(ruleCatalog);

        _options = options;
        _instructionsProvider = instructionsProvider;
        _skillCatalog = skillCatalog;
        _ruleCatalog = ruleCatalog;
        _memoryCatalog = memoryCatalog ?? new ProjectMemoryCatalog(PhysicalFileSystem.Instance);
    }

    /// <summary>Loads enabled skills without reading unrelated project guidance.</summary>
    public Task<IReadOnlyList<SkillDefinition>> LoadSkillsAsync(CancellationToken cancellationToken = default) =>
        _skillCatalog.LoadAsync(_options.ProjectRoot, cancellationToken);

    /// <summary>Loads all discovered skills for manual commands, independently of automatic guidance.</summary>
    public async Task<IReadOnlyList<SkillDefinition>> LoadCommandSkillsAsync(CancellationToken cancellationToken = default) =>
        (await _skillCatalog.LoadAllAsync(_options.ProjectRoot, cancellationToken).ConfigureAwait(false))
            .Where(skill => skill.UserInvocable).ToArray();

    /// <summary>Loads the project's guidance.</summary>
    public async Task<ProjectGuidance> LoadAsync(CancellationToken cancellationToken = default)
    {
        var instructions = await _instructionsProvider
            .LoadAsync(_options.ProjectRoot, cancellationToken)
            .ConfigureAwait(false);

        var skills = await LoadSkillsAsync(cancellationToken)
            .ConfigureAwait(false);

        var rules = await _ruleCatalog
            .LoadAsync(_options.ProjectRoot, cancellationToken)
            .ConfigureAwait(false);

        var memory = await _memoryCatalog
            .LoadAsync(_options.ProjectRoot, cancellationToken)
            .ConfigureAwait(false);

        return new ProjectGuidance(instructions, skills) { Rules = rules, Memory = memory };
    }
}
