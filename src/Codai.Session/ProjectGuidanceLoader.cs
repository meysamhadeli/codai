using Codai.Core.Abstractions;

namespace Codai.Session;

/// <summary>
/// Loads everything the project contributes to the prompt: the instructions file and the
/// list of skills.
/// </summary>
/// <remarks>
/// Both are read from <c>.codai</c> and from the instruction files the assistant knows
/// about, and both are reported to the user, because a project that is silently steering
/// the assistant is a project whose instructions are impossible to debug.
/// </remarks>
public sealed class ProjectGuidanceLoader
{
    private readonly SessionOptions _options;
    private readonly IProjectInstructionsProvider _instructionsProvider;
    private readonly ISkillCatalog _skillCatalog;
    private readonly IUserInterface _ui;

    /// <summary>Creates the loader.</summary>
    public ProjectGuidanceLoader(
        SessionOptions options,
        IProjectInstructionsProvider instructionsProvider,
        ISkillCatalog skillCatalog,
        IUserInterface ui)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(instructionsProvider);
        ArgumentNullException.ThrowIfNull(skillCatalog);
        ArgumentNullException.ThrowIfNull(ui);

        _options = options;
        _instructionsProvider = instructionsProvider;
        _skillCatalog = skillCatalog;
        _ui = ui;
    }

    /// <summary>Loads the project's guidance.</summary>
    public async Task<ProjectGuidance> LoadAsync(CancellationToken cancellationToken = default)
    {
        var instructions = await _instructionsProvider
            .LoadAsync(_options.ProjectRoot, cancellationToken)
            .ConfigureAwait(false);

        if (instructions is not null)
        {
            _ui.WriteInfo($"📄 Following project instructions from {instructions.SourcePath}");
        }

        var skills = await _skillCatalog
            .LoadAsync(_options.ProjectRoot, cancellationToken)
            .ConfigureAwait(false);

        if (skills.Count > 0)
        {
            _ui.WriteInfo(
                $"🧩 {skills.Count} skill(s) available: {string.Join(", ", skills.Select(skill => skill.Name))}");
        }

        return new ProjectGuidance(instructions, skills);
    }
}
