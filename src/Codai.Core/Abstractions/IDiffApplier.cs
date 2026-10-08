using Codai.Core.Diffs;

namespace Codai.Core.Abstractions;

/// <summary>
/// Applies accepted code changes to disk.
/// </summary>
public interface IDiffApplier
{
    /// <summary>
    /// Writes <paramref name="change"/> underneath <paramref name="rootDirectory"/>.
    /// Paths that resolve outside the root are rejected.
    /// </summary>
    Task<DiffApplyResult> ApplyAsync(
        string rootDirectory,
        CodeChange change,
        CancellationToken cancellationToken = default);
}

