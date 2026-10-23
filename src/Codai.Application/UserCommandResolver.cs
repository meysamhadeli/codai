using Codai.Core.Abstractions;
using Codai.Core.Customization;

namespace Codai.Application;

/// <summary>
/// Expands a <c>:command</c> the project defines in <c>.codai/commands</c> into the
/// request it stands for.
/// </summary>
/// <remarks>
/// A colon-prefixed line that is not a built-in session command is looked up in the
/// project's command catalog and its body becomes the prompt. That is what lets a team
/// keep a reviewed prompt — "review this diff", "write the migration" — in the repository
/// as a file, instead of every member pasting it and each paste drifting.
/// </remarks>
public sealed class UserCommandResolver
{
    private readonly ICommandCatalog _catalog;
    private readonly ISkillCatalog _skills;
    private readonly SessionOptions _options;

    /// <summary>Creates the resolver.</summary>
    public UserCommandResolver(ICommandCatalog catalog, ISkillCatalog skills, SessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(skills);
        ArgumentNullException.ThrowIfNull(options);

        _catalog = catalog;
        _skills = skills;
        _options = options;
    }

    /// <summary>Expands a project command line into the request to send.</summary>
    /// <returns>
    /// The expanded request, or <c>null</c> when the input does not name a project
    /// command. Null means "not one of mine", which the caller reports as an unknown
    /// command; it never means the colon-prefixed line should reach the model as a
    /// question.
    /// </returns>
    public async Task<CommandExpansion?> ExpandAsync(
        string input,
        CancellationToken cancellationToken = default)
    {
        if (!CommandInvocation.TryParse(input, out var invocation))
        {
            return null;
        }

        var definition = await _catalog
            .FindAsync(_options.ProjectRoot, invocation.Name, cancellationToken)
            .ConfigureAwait(false);

        if (definition is not null)
        {
            return new CommandExpansion(
                CommandExpander.Expand(definition, invocation.Arguments),
                definition.SourcePath);
        }

        var skill = await _skills.FindAsync(
            _options.ProjectRoot, invocation.Name, cancellationToken).ConfigureAwait(false);

        return skill is null
            ? null
            : new CommandExpansion(
                $"{skill.Body}\n\nUser request: {invocation.Arguments}",
                skill.SourcePath,
                IsSkill: true);
    }

    /// <summary>
    /// The names of the commands the project defines, for the message shown when a
    /// colon-prefixed line matches nothing.
    /// </summary>
    public async Task<IReadOnlyList<string>> NamesAsync(CancellationToken cancellationToken = default)
    {
        var commands = await _catalog
            .LoadAsync(_options.ProjectRoot, cancellationToken)
            .ConfigureAwait(false);

        return [.. commands.Select(command => ":" + command.Name)];
    }
}

/// <summary>
/// The request a project command stands for, and the file it came from.
/// </summary>
/// <param name="Request">The command body with the user's arguments substituted.</param>
/// <param name="SourcePath">The file the command was read from, shown to the user.</param>
/// <param name="IsSkill">True when the entry came from a skill rather than a command.</param>
public sealed record CommandExpansion(string Request, string SourcePath, bool IsSkill = false);
