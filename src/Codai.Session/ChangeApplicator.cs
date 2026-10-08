using Codai.Core.Abstractions;
using Codai.Core.Diffs;

namespace Codai.Session;

/// <summary>
/// Turns a model response into accepted file changes: extracts the proposed changes,
/// asks the user about each one and writes the accepted ones to disk.
/// </summary>
public sealed class ChangeApplicator
{
    private readonly SessionOptions _options;
    private readonly ICodeChangeExtractor _extractor;
    private readonly IDiffApplier _diffApplier;
    private readonly IUserInterface _ui;

    /// <summary>Creates the applicator.</summary>
    public ChangeApplicator(
        SessionOptions options,
        ICodeChangeExtractor extractor,
        IDiffApplier diffApplier,
        IUserInterface ui)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(extractor);
        ArgumentNullException.ThrowIfNull(diffApplier);
        ArgumentNullException.ThrowIfNull(ui);

        _options = options;
        _extractor = extractor;
        _diffApplier = diffApplier;
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

        foreach (var change in changes)
        {
            await ApplyOneAsync(change, cancellationToken).ConfigureAwait(false);
        }

        return changes.Count;
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
