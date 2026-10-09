using System.Text.RegularExpressions;
using Codai.Core.Abstractions;
using Codai.Core.IO;

namespace Codai.Core.Customization;

/// <summary>
/// Loads <c>:commands</c> from <c>.codai/commands/*.md</c>.
/// </summary>
/// <remarks>
/// Files are read on every load rather than cached: a command is a small text file, and
/// re-reading means a command added or edited mid-session is picked up on the next line
/// instead of requiring a restart.
/// </remarks>
public sealed class CommandCatalog : ICommandCatalog
{
    /// <summary>Upper bound on how many commands a project may define.</summary>
    public const int MaxCommands = 200;

    /// <summary>Extension a command file must have.</summary>
    public const string Extension = ".md";

    /// <summary>Longest command name accepted.</summary>
    public const int MaxNameLength = 40;

    private readonly IFileSystem _fileSystem;

    /// <summary>Creates the catalog.</summary>
    public CommandCatalog(IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        _fileSystem = fileSystem;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CommandDefinition>> LoadAsync(
        string rootDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);

        var directory = Path.Combine(
            rootDirectory,
            CodaiPaths.ToPlatformPath(CodaiPaths.CommandsDirectory));

        if (!_fileSystem.DirectoryExists(directory))
        {
            return [];
        }

        var commands = new List<CommandDefinition>();

        foreach (var file in Enumerate(directory).Take(MaxCommands))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var name = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();

            if (!CustomizationNames.IsValid(name))
            {
                continue;
            }

            var definition = await TryReadAsync(rootDirectory, file, name, cancellationToken)
                .ConfigureAwait(false);

            if (definition is not null)
            {
                commands.Add(definition);
            }
        }

        commands.Sort(static (left, right) => string.CompareOrdinal(left.Name, right.Name));

        return commands;
    }

    /// <inheritdoc />
    public async Task<CommandDefinition?> FindAsync(
        string rootDirectory,
        string name,
        CancellationToken cancellationToken = default)
    {
        var commands = await LoadAsync(rootDirectory, cancellationToken).ConfigureAwait(false);

        return commands.FirstOrDefault(
            command => string.Equals(command.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private List<string> Enumerate(string directory)
    {
        try
        {
            return
            [
                .. _fileSystem
                    .EnumerateFiles(directory)
                    .Where(file => file.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(file => file, StringComparer.Ordinal),
            ];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private async Task<CommandDefinition?> TryReadAsync(
        string rootDirectory,
        string file,
        string name,
        CancellationToken cancellationToken)
    {
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

        var description = fields.TryGetValue("description", out var declared)
            ? MarkdownSummary.Truncate(declared)
            : MarkdownSummary.FromBody(body);

        var hint = fields.TryGetValue("argument-hint", out var declaredHint)
            ? declaredHint
            : fields.TryGetValue("argumenthint", out var alternate) ? alternate : string.Empty;

        return new CommandDefinition(
            name,
            description,
            hint.Trim(),
            body,
            Relative(rootDirectory, file));
    }

    private static string Relative(string rootDirectory, string file) =>
        Path.GetRelativePath(rootDirectory, file).Replace('\\', '/');
}

/// <summary>
/// Validation shared by commands and skills.
/// </summary>
internal static partial class CustomizationNames
{
    /// <summary>
    /// A command or skill name: letters, digits, dash, underscore and dot. It becomes a
    /// directory or file name and is typed by the user, so it may not contain a
    /// separator, whitespace or <c>..</c>.
    /// </summary>
    [GeneratedRegex(@"^[a-z0-9][a-z0-9._-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex NameRegex();

    /// <summary>True when <paramref name="name"/> is usable as a command or skill name.</summary>
    public static bool IsValid(string? name) =>
        !string.IsNullOrEmpty(name) &&
        name.Length <= CommandCatalog.MaxNameLength &&
        NameRegex().IsMatch(name);
}
