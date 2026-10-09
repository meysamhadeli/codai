using Codai.Core.Customization;

namespace Codai.Core.Abstractions;

/// <summary>
/// The user-defined <c>:commands</c> a project ships in <c>.codai/commands</c>.
/// </summary>
/// <remarks>
/// The catalog is read on demand rather than cached for the session, so a command added
/// while the session is running is usable on the next line.
/// </remarks>
public interface ICommandCatalog
{
    /// <summary>Loads every valid command in <paramref name="rootDirectory"/>, sorted by name.</summary>
    Task<IReadOnlyList<CommandDefinition>> LoadAsync(
        string rootDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>Finds one command by name, or <c>null</c> when the project has none by that name.</summary>
    Task<CommandDefinition?> FindAsync(
        string rootDirectory,
        string name,
        CancellationToken cancellationToken = default);
}
