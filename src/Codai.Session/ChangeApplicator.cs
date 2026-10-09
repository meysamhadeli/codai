using Codai.Core.Abstractions;
using Codai.Core.Agents;
using Codai.Core.Diffs;

namespace Codai.Session;

/// <summary>
/// Turns a model response into accepted file changes: extracts the proposed changes,
/// asks the user about each one and writes the accepted ones to disk.
/// </summary>
/// <remarks>
/// A response can propose a change in two ways: by calling <c>edit_file</c>, which the
/// mode already filters out of the tool list, or by writing a diff into its prose, which
/// is how a model answers when it has no tool to call. The second route is why this type
/// checks the mode itself instead of relying on the tool list alone — otherwise "ask"
/// would still be able to change the project, just less politely.
/// </remarks>
public sealed class ChangeApplicator
{
    private readonly SessionOptions _options;
    private readonly ICodeChangeExtractor _extractor;
    private readonly IDiffApplier _diffApplier;
    private readonly AgentModeState _mode;
    private readonly IUserInterface _ui;

    /// <summary>Creates the applicator.</summary>
    /// <param name="options">Session configuration, used for the project root.</param>
    /// <param name="extractor">Finds the proposed changes in a response.</param>
    /// <param name="diffApplier">Writes an accepted change to disk.</param>
    /// <param name="mode">The mode the session is currently in.</param>
    /// <param name="ui">Console used to ask and to report.</param>
    public ChangeApplicator(
        SessionOptions options,
        ICodeChangeExtractor extractor,
        IDiffApplier diffApplier,
        AgentModeState mode,
        IUserInterface ui)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(extractor);
        ArgumentNullException.ThrowIfNull(diffApplier);
        ArgumentNullException.ThrowIfNull(mode);
        ArgumentNullException.ThrowIfNull(ui);

        _options = options;
        _extractor = extractor;
        _diffApplier = diffApplier;
        _mode = mode;
        _ui = ui;
    }

    /// <summary>
    /// Extracts and applies the changes in <paramref name="response"/>. Returns the
    /// number of changes that were proposed (accepted or not).
    /// </summary>
    public async Task<int> ApplyAsync(string response, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);

        var changes = _extractor.Extract(response);

        _ui.WriteLine(string.Empty);

        if (changes.Count == 0)
        {
            return 0;
        }

        if (!AgentModePolicy.AllowsWrites(_mode.Mode))
        {
            ReportRefused(changes);
            return changes.Count;
        }

        foreach (var change in changes)
        {
            await ApplyOneAsync(change, cancellationToken).ConfigureAwait(false);
        }

        return changes.Count;
    }

    /// <summary>
    /// Says what was proposed and why nothing was written. Staying silent would be the
    /// worst option: the user would read a diff, never be asked to accept it, and have no
    /// way to tell whether the file changed.
    /// </summary>
    private void ReportRefused(IReadOnlyList<CodeChange> changes)
    {
        var paths = string.Join(
            ", ",
            changes.Select(change => change.RelativePath));

        _ui.WriteError(
            $"🔒 {changes.Count} change(s) proposed ({paths}) were not applied: the session is " +
            $"in {AgentModeParser.ToName(_mode.Mode)} mode. Use " +
            $":mode {AgentModeParser.ToName(AgentMode.Agent)} to apply them.");
    }

    private async Task ApplyOneAsync(CodeChange change, CancellationToken cancellationToken)
    {
        var accepted = await _ui
            .ConfirmAsync(
                $"Do you want to accept the change for file {change.RelativePath} ? (y/n):",
                cancellationToken)
            .ConfigureAwait(false);

        if (!accepted)
        {
            _ui.WriteError($"❌ Changes to {change.RelativePath} rejected.");
            return;
        }

        try
        {
            var result = await _diffApplier
                .ApplyAsync(_options.ProjectRoot, change, cancellationToken)
                .ConfigureAwait(false);

            _ui.WriteSuccess(result.Deleted
                ? $"✔️ {change.RelativePath} deleted (the change resulted in an empty file)."
                : $"✔️ Changes accepted for {change.RelativePath}!");
        }
        catch (CodeChangeRejectedException ex)
        {
            // The model proposed a path outside the project; refuse and keep going.
            _ui.WriteError($"🚫 {ex.Message}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ui.WriteError($"Error applying changes: {ex.Message}");
        }
    }
}
