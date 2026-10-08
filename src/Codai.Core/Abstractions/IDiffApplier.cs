using Codai.Core.Models;

namespace Codai.Core.Abstractions;

/// <summary>
/// Applies accepted code changes to disk.
/// </summary>
public interface IDiffApplier
{
    Task<DiffApplyResult> ApplyAsync(
        string rootDirectory,
        CodeChange change,
        CancellationToken cancellationToken = default);
}

/// <summary>Outcome of applying a single change.</summary>
/// <param name="FullPath">Absolute path the change targeted.</param>
/// <param name="Written">True when the file was created or overwritten.</param>
/// <param name="Deleted">True when the file was deleted because the result was empty.</param>
public sealed record DiffApplyResult(string FullPath, bool Written, bool Deleted)
{
    public bool Changed => Written || Deleted;
}
