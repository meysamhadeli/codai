using Codai.Core.Abstractions;
using Codai.Core.Projects;

namespace Codai.Session;

/// <summary>
/// Scans the project once at the start of a session.
/// </summary>
/// <remarks>
/// A scan failure (missing directory, unreadable tree) is reported but does not abort
/// the session: the model still answers, just without project context.
/// </remarks>
public sealed class ProjectContextLoader
{
    private readonly SessionOptions _options;
    private readonly IProjectScanner _scanner;
    private readonly IUserInterface _ui;

    /// <summary>Creates the loader.</summary>
    public ProjectContextLoader(SessionOptions options, IProjectScanner scanner, IUserInterface ui)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(scanner);
        ArgumentNullException.ThrowIfNull(ui);

        _options = options;
        _scanner = scanner;
        _ui = ui;
    }

    /// <summary>
    /// Loads the project context, falling back to an empty context when the project
    /// cannot be read.
    /// </summary>
    public async Task<ProjectContext> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _ui
                .RunWithProgressAsync<ProjectContext>(
                    "Loading Context...",
                    ct => _scanner.ScanAsync(_options.ProjectRoot, ct),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is DirectoryNotFoundException or IOException or UnauthorizedAccessException)
        {
            _ui.WriteError(ex.Message);
            return ProjectContext.Empty;
        }
    }
}
