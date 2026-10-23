using Codai.Core.Abstractions;
using Codai.Core.Customization;
using Codai.Core.IO;

namespace Codai.Application;

/// <summary>
/// Loads everything the project contributes to the prompt: the instructions file and the
/// list of skills.
/// </summary>
/// <remarks>
/// Both are read from <c>.codai</c> and from the instruction files the assistant knows
/// about. What was found is returned to the caller and never written to the session,
/// so starting a session stays quiet.
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

    /// <summary>Loads the project's guidance.</summary>
    public async Task<ProjectGuidance> LoadAsync(CancellationToken cancellationToken = default)
    {
        var instructions = await _instructionsProvider
            .LoadAsync(_options.ProjectRoot, cancellationToken)
            .ConfigureAwait(false);

        var skills = await _skillCatalog
            .LoadAsync(_options.ProjectRoot, cancellationToken)
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
